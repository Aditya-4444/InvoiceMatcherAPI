using System.Threading.Channels;
using InvoiceMatcher.Options;
using Microsoft.Extensions.Options;

namespace InvoiceMatcher.Workers;

public sealed class InboxWatcher(
    InvoiceQueue queue,
    IOptions<InvoiceProcessingOptions> options,
    ILogger<InboxWatcher> logger) : BackgroundService
{
    private const string Filter = "*.pdf";

    private readonly InvoiceProcessingOptions settings = options.Value;
    private readonly Channel<string> discovered = Channel.CreateUnbounded<string>();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var inbox = Path.GetFullPath(settings.InboxPath);
        Directory.CreateDirectory(inbox);

        using var watcher = new FileSystemWatcher(inbox, Filter);
        watcher.Created += (_, e) => discovered.Writer.TryWrite(e.FullPath);
        watcher.Renamed += (_, e) => discovered.Writer.TryWrite(e.FullPath);
        watcher.Error += (_, e) => logger.LogError(e.GetException(), "Inbox watcher error; restart to rescan {Inbox}", inbox);
        watcher.EnableRaisingEvents = true;

        foreach (var filePath in Directory.EnumerateFiles(inbox, Filter))
        {
            discovered.Writer.TryWrite(filePath);
        }

        await foreach (var filePath in discovered.Reader.ReadAllAsync(stoppingToken))
        {
            if (await WaitUntilReadableAsync(filePath, stoppingToken))
            {
                await queue.EnqueueAsync(filePath, stoppingToken);
                logger.LogInformation("Queued {SourceFileRef}", Path.GetFileName(filePath));
            }
            else
            {
                logger.LogWarning("Skipping {SourceFileRef}: file is missing or still locked", Path.GetFileName(filePath));
            }
        }
    }

    private async Task<bool> WaitUntilReadableAsync(string filePath, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= settings.FileReadyAttempts; attempt++)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return true;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (IOException)
            {
                await Task.Delay(settings.FileReadyDelayMilliseconds, cancellationToken);
            }
        }

        return false;
    }
}
