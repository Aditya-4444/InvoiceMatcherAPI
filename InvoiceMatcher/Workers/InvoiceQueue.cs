using System.Threading.Channels;
using InvoiceMatcher.Options;
using Microsoft.Extensions.Options;

namespace InvoiceMatcher.Workers;

public sealed class InvoiceQueue(IOptions<InvoiceProcessingOptions> options)
{
    private readonly Channel<string> channel = Channel.CreateBounded<string>(
        new BoundedChannelOptions(options.Value.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });

    public int Depth => channel.Reader.Count;

    public ValueTask EnqueueAsync(string filePath, CancellationToken cancellationToken) =>
        channel.Writer.WriteAsync(filePath, cancellationToken);

    public IAsyncEnumerable<string> ReadAllAsync(CancellationToken cancellationToken) =>
        channel.Reader.ReadAllAsync(cancellationToken);
}
