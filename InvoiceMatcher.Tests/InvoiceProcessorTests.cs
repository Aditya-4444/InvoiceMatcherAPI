using InvoiceMatcher.Data;
using InvoiceMatcher.Domain;
using InvoiceMatcher.Extraction;
using InvoiceMatcher.Matching;
using InvoiceMatcher.Options;
using InvoiceMatcher.Workers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace InvoiceMatcher.Tests;

public sealed class InvoiceProcessorTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly string inbox = Directory.CreateTempSubdirectory("invoice-tests").FullName;
    private string ArchiveFolder => Path.Combine(inbox, "archive");
    private string FailedFolder => Path.Combine(inbox, "failed");
    private readonly FakeExtractor extractor = new();
    private TestDbFactory dbFactory = null!;
    private InvoiceProcessor processor = null!;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        dbFactory = new TestDbFactory(new DbContextOptionsBuilder<InvoiceDbContext>().UseSqlite(connection).Options);

        await using var db = dbFactory.CreateDbContext();
        await db.Database.MigrateAsync();
        db.PurchaseOrders.Add(new PurchaseOrder
        {
            PONumber = "PO-1001",
            Vendor = "Northwind Traders",
            Amount = 125.50m,
            Status = PurchaseOrderStatus.Open
        });
        await db.SaveChangesAsync();

        processor = new InvoiceProcessor(
            dbFactory,
            extractor,
            new InvoiceMatcherService(),
            new ProcessingState(),
            Microsoft.Extensions.Options.Options.Create(new InvoiceProcessingOptions
            {
                MaxAttempts = 3,
                RetryBackoffMilliseconds = 1,
                ArchivePath = ArchiveFolder,
                FailedPath = FailedFolder
            }),
            NullLogger<InvoiceProcessor>.Instance);
    }

    public async Task DisposeAsync()
    {
        await connection.DisposeAsync();
        Directory.Delete(inbox, recursive: true);
    }

    [Fact]
    public async Task ProcessingTheSameFileTwiceLeavesOneRecord()
    {
        var path = WriteFile("inv-001.pdf", "content-a");

        await processor.ProcessAsync(path, CancellationToken.None);
        await processor.ProcessAsync(path, CancellationToken.None);

        var invoice = Assert.Single(await LoadInvoicesAsync());
        Assert.Equal(InvoiceStatus.Matched, invoice.Status);
        Assert.Equal("PO-1001", invoice.MatchedPONumber);
        Assert.Equal("inv-001.pdf", invoice.SourceFileRef);
        Assert.Equal(1, extractor.Calls);
    }

    [Fact]
    public async Task SameContentUnderAnotherNameLeavesOneRecord()
    {
        await processor.ProcessAsync(WriteFile("inv-001.pdf", "content-a"), CancellationToken.None);
        await processor.ProcessAsync(WriteFile("copy-of-inv-001.pdf", "content-a"), CancellationToken.None);

        var invoice = Assert.Single(await LoadInvoicesAsync());
        Assert.Equal("inv-001.pdf", invoice.SourceFileRef);
    }

    [Fact]
    public async Task DifferentContentCreatesSeparateRecords()
    {
        await processor.ProcessAsync(WriteFile("a.pdf", "content-a"), CancellationToken.None);
        await processor.ProcessAsync(WriteFile("b.pdf", "content-b"), CancellationToken.None);

        Assert.Equal(2, (await LoadInvoicesAsync()).Count);
    }

    [Fact]
    public async Task TransientFailureIsRetriedUntilItSucceeds()
    {
        extractor.FailuresBeforeSuccess = 2;

        await processor.ProcessAsync(WriteFile("inv-001.pdf", "content-a"), CancellationToken.None);

        var invoice = Assert.Single(await LoadInvoicesAsync());
        Assert.Equal(InvoiceStatus.Matched, invoice.Status);
        Assert.Equal(3, invoice.AttemptCount);
    }

    [Fact]
    public async Task ThirdFailureMovesInvoiceToExceptionWithLastError()
    {
        extractor.FailuresBeforeSuccess = int.MaxValue;

        await processor.ProcessAsync(WriteFile("inv-001.pdf", "content-a"), CancellationToken.None);

        var invoice = Assert.Single(await LoadInvoicesAsync());
        Assert.Equal(InvoiceStatus.Exception, invoice.Status);
        Assert.Equal(3, invoice.AttemptCount);
        Assert.Equal(3, extractor.Calls);
        Assert.Contains("extractor unavailable", invoice.Reason);
    }

    [Fact]
    public async Task UnreadableInvoiceIsRecordedWithoutRetry()
    {
        extractor.Result = new ExtractedInvoice(null, null, null, null, null);

        await processor.ProcessAsync(WriteFile("inv-006.pdf", "garbage"), CancellationToken.None);

        var invoice = Assert.Single(await LoadInvoicesAsync());
        Assert.Equal(InvoiceStatus.Exception, invoice.Status);
        Assert.Equal("Extraction returned no usable fields", invoice.Reason);
        Assert.Equal("inv-006.pdf", invoice.SourceFileRef);
        Assert.Equal(1, invoice.AttemptCount);
        Assert.Equal(1, extractor.Calls);
    }

    [Fact]
    public async Task ResettingAnExceptionToPendingReprocessesTheSameRecord()
    {
        extractor.FailuresBeforeSuccess = int.MaxValue;
        var path = WriteFile("inv-001.pdf", "content-a");
        await processor.ProcessAsync(path, CancellationToken.None);

        extractor.FailuresBeforeSuccess = 0;
        await using (var db = dbFactory.CreateDbContext())
        {
            var failed = await db.Invoices.SingleAsync();
            failed.Status = InvoiceStatus.Pending;
            failed.AttemptCount = 0;
            await db.SaveChangesAsync();
        }

        await processor.ProcessAsync(Path.Combine(FailedFolder, "inv-001.pdf"), CancellationToken.None);

        var invoice = Assert.Single(await LoadInvoicesAsync());
        Assert.Equal(InvoiceStatus.Matched, invoice.Status);
        Assert.Equal(1, invoice.AttemptCount);
        Assert.True(File.Exists(Path.Combine(ArchiveFolder, "inv-001.pdf")));
        Assert.False(File.Exists(Path.Combine(FailedFolder, "inv-001.pdf")));
    }

    [Fact]
    public async Task ProcessedFileMovesToArchive()
    {
        var path = WriteFile("inv-001.pdf", "content-a");

        await processor.ProcessAsync(path, CancellationToken.None);

        Assert.False(File.Exists(path));
        Assert.True(File.Exists(Path.Combine(ArchiveFolder, "inv-001.pdf")));
    }

    [Fact]
    public async Task UnreadableFileIsArchivedNotFailed()
    {
        extractor.Result = new ExtractedInvoice(null, null, null, null, null);
        var path = WriteFile("inv-006.pdf", "garbage");

        await processor.ProcessAsync(path, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(ArchiveFolder, "inv-006.pdf")));
        Assert.False(Directory.Exists(FailedFolder));
    }

    [Fact]
    public async Task FileThatFailsAllAttemptsMovesToFailedFolder()
    {
        extractor.FailuresBeforeSuccess = int.MaxValue;
        var path = WriteFile("inv-001.pdf", "content-a");

        await processor.ProcessAsync(path, CancellationToken.None);

        Assert.False(File.Exists(path));
        Assert.True(File.Exists(Path.Combine(FailedFolder, "inv-001.pdf")));
        Assert.False(Directory.Exists(ArchiveFolder));
    }

    [Fact]
    public async Task NameClashInTargetFolderKeepsBothFilesAndUpdatesSourceFileRef()
    {
        Directory.CreateDirectory(ArchiveFolder);
        File.WriteAllText(Path.Combine(ArchiveFolder, "inv-001.pdf"), "older");

        await processor.ProcessAsync(WriteFile("inv-001.pdf", "content-a"), CancellationToken.None);

        var invoice = Assert.Single(await LoadInvoicesAsync());
        Assert.NotEqual("inv-001.pdf", invoice.SourceFileRef);
        Assert.True(File.Exists(Path.Combine(ArchiveFolder, invoice.SourceFileRef)));
        Assert.Equal("older", File.ReadAllText(Path.Combine(ArchiveFolder, "inv-001.pdf")));
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(inbox, name);
        File.WriteAllText(path, content);
        return path;
    }

    private async Task<List<Invoice>> LoadInvoicesAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.Invoices.AsNoTracking().ToListAsync();
    }

    private sealed class FakeExtractor : IInvoiceExtractor
    {
        public int Calls { get; private set; }
        public int FailuresBeforeSuccess { get; set; }
        public ExtractedInvoice Result { get; set; } =
            new("Northwind Traders", 125.50m, new DateTime(2025, 1, 10), "PO-1001", "INV-001");

        public Task<ExtractedInvoice> ExtractAsync(string filePath, CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls <= FailuresBeforeSuccess)
            {
                throw new IOException("extractor unavailable");
            }

            return Task.FromResult(Result);
        }
    }

    private sealed class TestDbFactory(DbContextOptions<InvoiceDbContext> options) : IDbContextFactory<InvoiceDbContext>
    {
        public InvoiceDbContext CreateDbContext() => new(options);
    }
}
