namespace InvoiceMatcher.Options;

public sealed class InvoiceProcessingOptions
{
    public const string SectionName = "InvoiceProcessing";

    public string InboxPath { get; set; } = "../data/inbox";
    public string ArchivePath { get; set; } = "../data/archive";
    public string FailedPath { get; set; } = "../data/failed";
    public string PosCsvPath { get; set; } = "../data/pos.csv";
    public int QueueCapacity { get; set; } = 100;
    public int MaxAttempts { get; set; } = 3;
    public int RetryBackoffMilliseconds { get; set; } = 250;
    public int FileReadyAttempts { get; set; } = 10;
    public int FileReadyDelayMilliseconds { get; set; } = 250;
}
