namespace InvoiceMatcher.Extraction;

public sealed record ExtractedInvoice(
    string? Vendor,
    decimal? Amount,
    DateTime? InvoiceDate,
    string? PONumber,
    string? InvoiceNumber);
