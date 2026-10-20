using InvoiceMatcher.Data;
using InvoiceMatcher.Domain;
using InvoiceMatcher.Endpoints.Contracts;
using Microsoft.EntityFrameworkCore;

namespace InvoiceMatcher.Endpoints;

public static class InvoiceEndpoints
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    public static IEndpointRouteBuilder MapInvoiceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/invoices", ListAsync)
            .WithTags("Invoices")
            .WithSummary("Lists invoices, optionally filtered by status (pending, matched, unmatched or exception).")
            .Produces<PagedResult<InvoiceResponse>>(StatusCodes.Status200OK)
            .ProducesValidationProblem();
        return app;
    }

    private static async Task<IResult> ListAsync(
        string? status,
        int? page,
        int? pageSize,
        IDbContextFactory<InvoiceDbContext> dbFactory,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Invoices.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<InvoiceStatus>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = [$"Unknown status '{status}'. Use pending, matched, unmatched or exception."]
                });
            }

            query = query.Where(x => x.Status == parsed);
        }

        var currentPage = Math.Max(page ?? 1, 1);
        var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);

        var total = await query.CountAsync(cancellationToken);
        var invoices = await query
            .OrderByDescending(x => x.Id)
            .Skip((currentPage - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        return Results.Ok(new PagedResult<InvoiceResponse>(
            invoices.Select(InvoiceResponse.From).ToList(), currentPage, size, total));
    }
}
