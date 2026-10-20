using InvoiceMatcher.Domain;
using InvoiceMatcher.Extraction;

namespace InvoiceMatcher.Matching;

public sealed class InvoiceMatcherService : IInvoiceMatcher
{
    public const string UnreadableReason = "Extraction returned no usable fields";

    public MatchResult Match(ExtractedInvoice invoice, IReadOnlyCollection<PurchaseOrder> purchaseOrders)
    {
        if (string.IsNullOrWhiteSpace(invoice.Vendor) || invoice.Amount is null)
        {
            return new MatchResult(InvoiceStatus.Exception, UnreadableReason, null);
        }

        return string.IsNullOrWhiteSpace(invoice.PONumber)
            ? MatchByVendorAndAmount(invoice.Vendor, invoice.Amount.Value, purchaseOrders)
            : MatchByPoNumber(invoice.PONumber, purchaseOrders);
    }

    private static MatchResult MatchByPoNumber(string poNumber, IReadOnlyCollection<PurchaseOrder> purchaseOrders)
    {
        var number = poNumber.Trim();
        var purchaseOrder = purchaseOrders.FirstOrDefault(
            po => string.Equals(po.PONumber, number, StringComparison.OrdinalIgnoreCase));

        if (purchaseOrder is null)
        {
            return new MatchResult(InvoiceStatus.Unmatched, $"PO {number} was not found", null);
        }

        if (purchaseOrder.Status != PurchaseOrderStatus.Open)
        {
            return new MatchResult(InvoiceStatus.Unmatched, $"PO {purchaseOrder.PONumber} is closed", null);
        }

        return new MatchResult(InvoiceStatus.Matched, "Matched on PONumber", purchaseOrder.PONumber);
    }

    private static MatchResult MatchByVendorAndAmount(string vendor, decimal amount, IReadOnlyCollection<PurchaseOrder> purchaseOrders)
    {
        var candidates = purchaseOrders
            .Where(po => po.Status == PurchaseOrderStatus.Open
                && po.Amount == amount
                && string.Equals(po.Vendor.Trim(), vendor.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(po => po.PONumber)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return candidates.Length switch
        {
            0 => new MatchResult(InvoiceStatus.Unmatched, "No open PO matched on Vendor+Amount", null),
            1 => new MatchResult(InvoiceStatus.Matched, "Matched on Vendor+Amount", candidates[0]),
            _ => new MatchResult(
                InvoiceStatus.Exception,
                $"Ambiguous: {candidates.Length} candidate POs ({string.Join(", ", candidates)}) matched on Vendor+Amount",
                null)
        };
    }
}
