using InvoiceMatcher.Domain;
using InvoiceMatcher.Extraction;

namespace InvoiceMatcher.Matching;

public interface IInvoiceMatcher
{
    MatchResult Match(ExtractedInvoice invoice, IReadOnlyCollection<PurchaseOrder> purchaseOrders);
}
