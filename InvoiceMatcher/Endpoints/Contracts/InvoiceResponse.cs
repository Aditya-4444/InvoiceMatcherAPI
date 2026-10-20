using InvoiceMatcher.Domain;

namespace InvoiceMatcher.Endpoints.Contracts;

public sealed record InvoiceResponse(
    long Id,
    string? Vendor,
    decimal? Amount,
    DateTime? InvoiceDate,
    string? PONumber,
    string? InvoiceNumber,
    InvoiceStatus Status,
    string? Reason,
    string? MatchedPONumber,
    int AttemptCount,
    string SourceFileRef,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc)
{
    public static InvoiceResponse From(Invoice invoice) => new(
        invoice.Id,
        invoice.Vendor,
        invoice.Amount,
        invoice.InvoiceDate,
        invoice.PONumber,
        invoice.InvoiceNumber,
        invoice.Status,
        invoice.Reason,
        invoice.MatchedPONumber,
        invoice.AttemptCount,
        invoice.SourceFileRef,
        invoice.CreatedUtc,
        invoice.UpdatedUtc);
}
