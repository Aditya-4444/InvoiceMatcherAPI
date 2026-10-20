using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;

namespace InvoiceMatcher.Extraction;

public sealed partial class PdfTextInvoiceExtractor : IInvoiceExtractor
{
    // PdfPig does not reliably keep line breaks, so each value ends where the next label begins.
    [GeneratedRegex(
        @"(?<label>Vendor|Amount|InvoiceDate|PONumber|InvoiceNumber):[ \t]*(?<value>.*?)(?=\s*(?:Vendor|Amount|InvoiceDate|PONumber|InvoiceNumber):|$)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LabelledField();

    public Task<ExtractedInvoice> ExtractAsync(string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fields = LabelledField().Matches(ReadText(filePath))
            .GroupBy(m => m.Groups["label"].Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Groups["value"].Value.Trim(), StringComparer.OrdinalIgnoreCase);

        string? Field(string label) =>
            fields.TryGetValue(label, out var value) && value.Length > 0 ? value : null;

        decimal? amount = decimal.TryParse(Field("Amount"), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedAmount)
            ? parsedAmount
            : null;
        DateTime? invoiceDate = DateTime.TryParse(Field("InvoiceDate"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)
            ? parsedDate
            : null;

        return Task.FromResult(new ExtractedInvoice(
            Field("Vendor"),
            amount,
            invoiceDate,
            Field("PONumber"),
            Field("InvoiceNumber")));
    }

    private static string ReadText(string filePath)
    {
        try
        {
            using var document = PdfDocument.Open(filePath);
            return string.Join('\n', document.GetPages().Select(page => page.Text));
        }
        catch (PdfDocumentFormatException)
        {
            return string.Empty;
        }
    }
}
