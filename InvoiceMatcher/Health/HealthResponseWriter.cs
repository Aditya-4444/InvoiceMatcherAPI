using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace InvoiceMatcher.Health;

public static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var body = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                data = entry.Value.Data
            })
        };

        return context.Response.WriteAsJsonAsync(body, context.RequestAborted);
    }
}
