namespace InvoiceMatcher.Domain;

public sealed class Invoice
{
    public long Id { get; set; }
    public string? Vendor { get; set; }
    public decimal? Amount { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public string? PONumber { get; set; }
    public string? InvoiceNumber { get; set; }
    public string SourceFileRef { get; set; } = string.Empty;
    public InvoiceStatus Status { get; set; }
    public string? Reason { get; set; }
    public string? MatchedPONumber { get; set; }
    public int AttemptCount { get; set; }
    public string FileHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}
