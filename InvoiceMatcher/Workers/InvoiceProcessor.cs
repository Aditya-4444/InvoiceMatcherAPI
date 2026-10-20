using System.Security.Cryptography;
using InvoiceMatcher.Data;
using InvoiceMatcher.Domain;
using InvoiceMatcher.Extraction;
using InvoiceMatcher.Matching;
using InvoiceMatcher.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InvoiceMatcher.Workers;

public sealed class InvoiceProcessor(
    IDbContextFactory<InvoiceDbContext> dbFactory,
    IInvoiceExtractor extractor,
    IInvoiceMatcher matcher,
    ProcessingState state,
    IOptions<InvoiceProcessingOptions> options,
    ILogger<InvoiceProcessor> logger)
{
    private readonly InvoiceProcessingOptions settings = options.Value;

    public async Task ProcessAsync(string filePath, CancellationToken cancellationToken)
    {
        var sourceFileRef = Path.GetFileName(filePath);
        if (!File.Exists(filePath))
        {
            logger.LogWarning("Invoice file {SourceFileRef} no longer exists", sourceFileRef);
            return;
        }

        var fileHash = await ComputeHashAsync(filePath, cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var invoice = await db.Invoices.SingleOrDefaultAsync(x => x.FileHash == fileHash, cancellationToken);
        if (invoice is { Status: not InvoiceStatus.Pending })
        {
            logger.LogInformation(
                "Skipping {SourceFileRef}: content already processed as invoice {InvoiceId}",
                sourceFileRef, invoice.Id);
            MoveTo(filePath, settings.ArchivePath);
            return;
        }

        if (invoice is null)
        {
            var now = DateTimeOffset.UtcNow;
            invoice = new Invoice
            {
                SourceFileRef = sourceFileRef,
                FileHash = fileHash,
                Status = InvoiceStatus.Pending,
                CreatedUtc = now,
                UpdatedUtc = now
            };
            db.Invoices.Add(invoice);
        }

        var completed = await RunAttemptsAsync(db, invoice, filePath, cancellationToken);

        var movedTo = MoveTo(filePath, completed ? settings.ArchivePath : settings.FailedPath);
        if (movedTo is not null && Path.GetFileName(movedTo) != invoice.SourceFileRef)
        {
            invoice.SourceFileRef = Path.GetFileName(movedTo);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private string? MoveTo(string filePath, string folder)
    {
        var sourceFolder = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        var targetFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (string.Equals(sourceFolder, targetFolder, StringComparison.OrdinalIgnoreCase))
        {
            return filePath;
        }

        try
        {
            Directory.CreateDirectory(targetFolder);
            var name = Path.GetFileName(filePath);
            var target = Path.Combine(targetFolder, name);
            if (File.Exists(target))
            {
                target = Path.Combine(
                    targetFolder,
                    $"{Path.GetFileNameWithoutExtension(name)}-{DateTime.UtcNow:yyyyMMddHHmmssfff}{Path.GetExtension(name)}");
            }

            File.Move(filePath, target);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not move {SourceFileRef} to {Folder}", Path.GetFileName(filePath), targetFolder);
            return null;
        }
    }

    private async Task<bool> RunAttemptsAsync(InvoiceDbContext db, Invoice invoice, string filePath, CancellationToken cancellationToken)
    {
        string? lastError = null;

        while (invoice.AttemptCount < settings.MaxAttempts)
        {
            invoice.AttemptCount++;
            try
            {
                var extracted = await extractor.ExtractAsync(filePath, cancellationToken);
                var purchaseOrders = await db.PurchaseOrders.AsNoTracking().ToListAsync(cancellationToken);
                Apply(invoice, extracted, matcher.Match(extracted, purchaseOrders));
                await db.SaveChangesAsync(cancellationToken);

                state.RecordSuccess();
                logger.LogInformation(
                    "Invoice {SourceFileRef} finished as {Status}: {Reason}",
                    invoice.SourceFileRef, invoice.Status, invoice.Reason);
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex.Message;
                logger.LogWarning(
                    ex, "Attempt {Attempt} of {MaxAttempts} failed for {SourceFileRef}",
                    invoice.AttemptCount, settings.MaxAttempts, invoice.SourceFileRef);

                invoice.UpdatedUtc = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);

                if (invoice.AttemptCount < settings.MaxAttempts)
                {
                    await Task.Delay(settings.RetryBackoffMilliseconds * invoice.AttemptCount, cancellationToken);
                }
            }
        }

        invoice.Status = InvoiceStatus.Exception;
        invoice.Reason = $"Processing failed after {invoice.AttemptCount} attempts: {lastError}";
        invoice.UpdatedUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogError("Invoice {SourceFileRef} moved to exceptions: {Reason}", invoice.SourceFileRef, invoice.Reason);
        return false;
    }

    private static void Apply(Invoice invoice, ExtractedInvoice extracted, MatchResult result)
    {
        invoice.Vendor = extracted.Vendor;
        invoice.Amount = extracted.Amount;
        invoice.InvoiceDate = extracted.InvoiceDate;
        invoice.PONumber = extracted.PONumber;
        invoice.InvoiceNumber = extracted.InvoiceNumber;
        invoice.Status = result.Status;
        invoice.Reason = result.Reason;
        invoice.MatchedPONumber = result.MatchedPONumber;
        invoice.UpdatedUtc = DateTimeOffset.UtcNow;
    }

    private static async Task<string> ComputeHashAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}
