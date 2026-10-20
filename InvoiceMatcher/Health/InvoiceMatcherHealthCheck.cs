using InvoiceMatcher.Data;
using InvoiceMatcher.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace InvoiceMatcher.Health;

public sealed class InvoiceMatcherHealthCheck(
    IDbContextFactory<InvoiceDbContext> dbFactory,
    InvoiceQueue queue,
    ProcessingState state) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>
        {
            ["queueDepth"] = queue.Depth,
            ["lastSuccessfulRun"] = state.LastSuccessfulRunUtc?.ToString("O") ?? "none yet"
        };

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var reachable = await db.Database.CanConnectAsync(cancellationToken);
            data["databaseReachable"] = reachable;

            return reachable
                ? HealthCheckResult.Healthy("Database reachable", data)
                : HealthCheckResult.Unhealthy("Database is not reachable", data: data);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            data["databaseReachable"] = false;
            return HealthCheckResult.Unhealthy("Database is not reachable", ex, data);
        }
    }
}
