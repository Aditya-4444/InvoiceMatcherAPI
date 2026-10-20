using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using InvoiceMatcher.Data;
using InvoiceMatcher.Domain;
using InvoiceMatcher.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InvoiceMatcher.Workers;

public sealed class PoCsvImporter(
    IDbContextFactory<InvoiceDbContext> dbFactory,
    IOptions<InvoiceProcessingOptions> options,
    ILogger<PoCsvImporter> logger)
{
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd-MM-yyyy"];

    public async Task ImportAsync(CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(options.Value.PosCsvPath);
        if (!File.Exists(path))
        {
            logger.LogWarning("PO file {Path} was not found; no purchase orders imported", path);
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.PurchaseOrders.ToDictionaryAsync(x => x.PONumber, cancellationToken);

        using var reader = new StreamReader(path);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture) { TrimOptions = TrimOptions.Trim, MissingFieldFound = null });

        var now = DateTimeOffset.UtcNow;
        var imported = 0;
        var skipped = 0;

        await csv.ReadAsync();
        csv.ReadHeader();
        while (await csv.ReadAsync())
        {
            var line = csv.Parser.Row;
            try
            {
                var row = csv.GetRecord<PoRow>();
                if (string.IsNullOrWhiteSpace(row.PONumber) || string.IsNullOrWhiteSpace(row.Vendor))
                {
                    throw new FormatException("PONumber and Vendor are required");
                }

                var invoiceDate = ParseDate(row.InvoiceDate);

                if (!existing.TryGetValue(row.PONumber, out var purchaseOrder))
                {
                    purchaseOrder = new PurchaseOrder { PONumber = row.PONumber, CreatedUtc = now };
                    db.PurchaseOrders.Add(purchaseOrder);
                    existing[row.PONumber] = purchaseOrder;
                }

                purchaseOrder.Vendor = row.Vendor;
                purchaseOrder.Amount = row.Amount;
                purchaseOrder.Status = row.Status;
                purchaseOrder.InvoiceNumber = string.IsNullOrWhiteSpace(row.InvoiceNumber) ? null : row.InvoiceNumber;
                purchaseOrder.InvoiceDate = invoiceDate;
                purchaseOrder.UpdatedUtc = now;
                imported++;
            }
            catch (Exception ex) when (ex is CsvHelperException or FormatException)
            {
                skipped++;
                logger.LogWarning("Skipping line {Line} of {Path}: {Reason}", line, path, ex.InnerException?.Message ?? ex.Message);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Imported {Count} purchase orders from {Path} ({Skipped} skipped)", imported, path, skipped);
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new FormatException($"InvoiceDate '{value}' is not in yyyy-MM-dd or dd-MM-yyyy format");
    }

    private sealed class PoRow
    {
        public string PONumber { get; set; } = string.Empty;
        public string Vendor { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public PurchaseOrderStatus Status { get; set; }

        [Optional]
        public string? InvoiceNumber { get; set; }

        [Optional]
        public string? InvoiceDate { get; set; }
    }
}
