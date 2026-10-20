using InvoiceMatcher.Domain;
using InvoiceMatcher.Extraction;
using InvoiceMatcher.Matching;

namespace InvoiceMatcher.Tests;

public class InvoiceMatcherServiceTests
{
    private readonly InvoiceMatcherService matcher = new();

    private static readonly PurchaseOrder[] PurchaseOrders =
    [
        Po("PO-1001", "Northwind Traders", 125.50m, PurchaseOrderStatus.Open),
        Po("PO-1002", "Contoso Ltd", 500.00m, PurchaseOrderStatus.Open),
        Po("PO-1003", "Contoso Ltd", 500.00m, PurchaseOrderStatus.Open),
        Po("PO-1004", "Fabrikam", 275.00m, PurchaseOrderStatus.Closed),
        Po("PO-1007", "Tailspin Toys", 88.25m, PurchaseOrderStatus.Open)
    ];

    [Fact]
    public void PoNumberOnOpenPoIsMatched()
    {
        var result = matcher.Match(Invoice("Northwind Traders", 125.50m, "PO-1001"), PurchaseOrders);

        Assert.Equal(InvoiceStatus.Matched, result.Status);
        Assert.Equal("PO-1001", result.MatchedPONumber);
    }

    [Fact]
    public void PoNumberOnClosedPoIsUnmatchedWithReason()
    {
        var result = matcher.Match(Invoice("Fabrikam", 275.00m, "PO-1004"), PurchaseOrders);

        Assert.Equal(InvoiceStatus.Unmatched, result.Status);
        Assert.Null(result.MatchedPONumber);
        Assert.Contains("closed", result.Reason);
    }

    [Fact]
    public void UnknownPoNumberIsUnmatchedWithReason()
    {
        var result = matcher.Match(Invoice("Fabrikam", 300.00m, "PO-9999"), PurchaseOrders);

        Assert.Equal(InvoiceStatus.Unmatched, result.Status);
        Assert.Contains("PO-9999", result.Reason);
    }

    [Fact]
    public void PoNumberTakesPrecedenceOverVendorAndAmount()
    {
        var result = matcher.Match(Invoice("Contoso Ltd", 500.00m, "PO-9999"), PurchaseOrders);

        Assert.Equal(InvoiceStatus.Unmatched, result.Status);
    }

    [Fact]
    public void FallbackWithSingleOpenCandidateIsMatched()
    {
        var result = matcher.Match(Invoice("  tailspin toys ", 88.25m, null), PurchaseOrders);

        Assert.Equal(InvoiceStatus.Matched, result.Status);
        Assert.Equal("PO-1007", result.MatchedPONumber);
    }

    [Fact]
    public void FallbackWithTwoOpenCandidatesIsAmbiguousException()
    {
        var result = matcher.Match(Invoice("Contoso Ltd", 500.00m, null), PurchaseOrders);

        Assert.Equal(InvoiceStatus.Exception, result.Status);
        Assert.Null(result.MatchedPONumber);
        Assert.Equal("Ambiguous: 2 candidate POs (PO-1002, PO-1003) matched on Vendor+Amount", result.Reason);
    }

    [Fact]
    public void FallbackIgnoresClosedPos()
    {
        var result = matcher.Match(Invoice("Fabrikam", 275.00m, null), PurchaseOrders);

        Assert.Equal(InvoiceStatus.Unmatched, result.Status);
    }

    [Fact]
    public void FallbackRequiresExactAmount()
    {
        var result = matcher.Match(Invoice("Contoso Ltd", 500.01m, null), PurchaseOrders);

        Assert.Equal(InvoiceStatus.Unmatched, result.Status);
    }

    [Fact]
    public void ExtractionWithNoFieldsIsUnreadableException()
    {
        var result = matcher.Match(new ExtractedInvoice(null, null, null, null, null), PurchaseOrders);

        Assert.Equal(InvoiceStatus.Exception, result.Status);
        Assert.Equal("Extraction returned no usable fields", result.Reason);
    }

    [Fact]
    public void MissingVendorOrAmountIsUnreadableException()
    {
        var withoutAmount = matcher.Match(Invoice("Contoso Ltd", null, "PO-1002"), PurchaseOrders);
        var withoutVendor = matcher.Match(Invoice(null, 500.00m, "PO-1002"), PurchaseOrders);

        Assert.Equal(InvoiceStatus.Exception, withoutAmount.Status);
        Assert.Equal(InvoiceStatus.Exception, withoutVendor.Status);
    }

    private static PurchaseOrder Po(string number, string vendor, decimal amount, PurchaseOrderStatus status) =>
        new() { PONumber = number, Vendor = vendor, Amount = amount, Status = status };

    private static ExtractedInvoice Invoice(string? vendor, decimal? amount, string? poNumber) =>
        new(vendor, amount, new DateTime(2025, 1, 10), poNumber, "INV-TEST");
}
