namespace InvoiceMatcher.Workers;

public sealed class InvoiceWorker(
    InvoiceQueue queue,
    InvoiceProcessor processor,
    ILogger<InvoiceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var filePath in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await processor.ProcessAsync(filePath, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Unexpected failure while processing {SourceFileRef}", Path.GetFileName(filePath));
            }
        }
    }
}
