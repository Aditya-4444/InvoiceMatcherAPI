using InvoiceMatcher.Domain;

namespace InvoiceMatcher.Matching;

public sealed record MatchResult(InvoiceStatus Status, string Reason, string? MatchedPONumber);
