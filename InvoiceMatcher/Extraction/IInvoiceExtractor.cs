namespace InvoiceMatcher.Extraction;

public interface IInvoiceExtractor
{
    Task<ExtractedInvoice> ExtractAsync(string filePath, CancellationToken cancellationToken);
}
