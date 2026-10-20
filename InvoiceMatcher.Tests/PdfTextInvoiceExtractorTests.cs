using InvoiceMatcher.Extraction;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace InvoiceMatcher.Tests;

public sealed class PdfTextInvoiceExtractorTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("pdf-tests").FullName;
    private readonly PdfTextInvoiceExtractor extractor = new();

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public async Task ExtractsAllLabelledFields()
    {
        var path = WritePdf("Vendor: Acme Supplies Ltd InvoiceNumber: ACM-1 InvoiceDate: 2026-09-15 PONumber: PO-1001 Amount: 12500.00");

        var result = await extractor.ExtractAsync(path, CancellationToken.None);

        Assert.Equal("Acme Supplies Ltd", result.Vendor);
        Assert.Equal(12500.00m, result.Amount);
        Assert.Equal(new DateTime(2026, 9, 15), result.InvoiceDate);
        Assert.Equal("PO-1001", result.PONumber);
        Assert.Equal("ACM-1", result.InvoiceNumber);
    }

    [Fact]
    public async Task MissingPoNumberIsNull()
    {
        var path = WritePdf("Vendor: Delta Packaging Co InvoiceNumber: DPC-1 InvoiceDate: 2026-09-27 Amount: 5600.00");

        var result = await extractor.ExtractAsync(path, CancellationToken.None);

        Assert.Null(result.PONumber);
        Assert.Equal("Delta Packaging Co", result.Vendor);
        Assert.Equal(5600.00m, result.Amount);
    }

    [Fact]
    public async Task FileThatIsNotAPdfYieldsNoFields()
    {
        var path = Path.Combine(folder, "garbage.pdf");
        await File.WriteAllTextAsync(path, "not a pdf");

        var result = await extractor.ExtractAsync(path, CancellationToken.None);

        Assert.Null(result.Vendor);
        Assert.Null(result.Amount);
    }

    private string WritePdf(string text)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(595, 842);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        page.AddText(text, 10, new PdfPoint(10, 800), font);

        var path = Path.Combine(folder, $"{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, builder.Build());
        return path;
    }
}
