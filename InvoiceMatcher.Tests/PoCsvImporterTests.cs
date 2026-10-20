using InvoiceMatcher.Domain;
using InvoiceMatcher.Options;
using InvoiceMatcher.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace InvoiceMatcher.Tests;

public sealed class PoCsvImporterTests : IAsyncLifetime
{
    private readonly string folder = Directory.CreateTempSubdirectory("po-tests").FullName;
    private SqliteTestDb db = null!;

    public async Task InitializeAsync() => db = await SqliteTestDb.CreateAsync();

    public async Task DisposeAsync()
    {
        await db.DisposeAsync();
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public async Task ImportsRowsWithInvoiceColumnsAndBothDateFormats()
    {
        await ImportAsync(
            "PONumber,Vendor,Amount,Status,InvoiceNumber,InvoiceDate\n" +
            "PO-1,Acme,10.50,Open,INV-1,2026-09-15\n" +
            "PO-2,Beta,20,Closed,INV-2,16-09-2026\n");

        var pos = await LoadAsync();
        Assert.Equal(2, pos.Count);
        Assert.Equal(new DateTime(2026, 9, 15), pos[0].InvoiceDate);
        Assert.Equal(new DateTime(2026, 9, 16), pos[1].InvoiceDate);
        Assert.Equal(PurchaseOrderStatus.Closed, pos[1].Status);
    }

    [Fact]
    public async Task FileWithoutInvoiceColumnsStillImports()
    {
        await ImportAsync("PONumber,Vendor,Amount,Status\nPO-1,Acme,10.50,Open\n");

        var po = Assert.Single(await LoadAsync());
        Assert.Null(po.InvoiceNumber);
        Assert.Null(po.InvoiceDate);
    }

    [Fact]
    public async Task BadRowsAreSkippedAndGoodRowsStillImport()
    {
        await ImportAsync(
            "PONumber,Vendor,Amount,Status,InvoiceNumber,InvoiceDate\n" +
            "PO-1,Acme,10.50,Open,INV-1,not-a-date\n" +
            "PO-2,Beta,abc,Open,,\n" +
            "PO-3,Gamma,30,Maybe,,\n" +
            ",NoNumber,5,Open,,\n" +
            "PO-4,Delta,40,Open,INV-4,2026-09-20\n");

        var po = Assert.Single(await LoadAsync());
        Assert.Equal("PO-4", po.PONumber);
    }

    [Fact]
    public async Task ReimportUpdatesExistingRowsWithoutDuplicates()
    {
        await ImportAsync("PONumber,Vendor,Amount,Status\nPO-1,Acme,10,Open\n");
        await ImportAsync("PONumber,Vendor,Amount,Status\nPO-1,Acme,99,Closed\n");

        var po = Assert.Single(await LoadAsync());
        Assert.Equal(99m, po.Amount);
        Assert.Equal(PurchaseOrderStatus.Closed, po.Status);
    }

    private async Task ImportAsync(string csv)
    {
        var path = Path.Combine(folder, "pos.csv");
        await File.WriteAllTextAsync(path, csv);
        var importer = new PoCsvImporter(
            db,
            Microsoft.Extensions.Options.Options.Create(new InvoiceProcessingOptions { PosCsvPath = path }),
            NullLogger<PoCsvImporter>.Instance);
        await importer.ImportAsync(CancellationToken.None);
    }

    private async Task<List<PurchaseOrder>> LoadAsync()
    {
        await using var context = db.CreateDbContext();
        return await context.PurchaseOrders.AsNoTracking().OrderBy(x => x.PONumber).ToListAsync();
    }
}
