namespace InvoiceMatcher.Workers;

public sealed class ProcessingState
{
    private long lastSuccessTicks;

    public DateTimeOffset? LastSuccessfulRunUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref lastSuccessTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    public void RecordSuccess() => Interlocked.Exchange(ref lastSuccessTicks, DateTimeOffset.UtcNow.UtcTicks);
}
