namespace InvoiceMatcher.Domain;

public sealed class PurchaseOrder
{
    public long Id { get; set; }
    public string PONumber { get; set; } = string.Empty;
    public string Vendor { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public PurchaseOrderStatus Status { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}
