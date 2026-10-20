using System.Text.Json.Serialization;
using InvoiceMatcher.Data;
using InvoiceMatcher.Endpoints;
using InvoiceMatcher.Extraction;
using InvoiceMatcher.Health;
using InvoiceMatcher.Matching;
using InvoiceMatcher.Options;
using InvoiceMatcher.Workers;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<InvoiceProcessingOptions>(
    builder.Configuration.GetSection(InvoiceProcessingOptions.SectionName));
builder.Services.AddDbContextFactory<InvoiceDbContext>(
    options => options.UseSqlite(builder.Configuration.GetConnectionString("Invoices")));
builder.Services.ConfigureHttpJsonOptions(
    options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddSingleton<InvoiceQueue>();
builder.Services.AddSingleton<ProcessingState>();
builder.Services.AddSingleton<IInvoiceExtractor, PdfTextInvoiceExtractor>();
builder.Services.AddSingleton<IInvoiceMatcher, InvoiceMatcherService>();
builder.Services.AddSingleton<InvoiceProcessor>();
builder.Services.AddSingleton<PoCsvImporter>();
builder.Services.AddHostedService<InvoiceWorker>();
builder.Services.AddHostedService<InboxWatcher>();

builder.Services.AddHealthChecks().AddCheck<InvoiceMatcherHealthCheck>("invoice-matcher");
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

var stopping = app.Lifetime.ApplicationStopping;
await using (var db = await app.Services.GetRequiredService<IDbContextFactory<InvoiceDbContext>>().CreateDbContextAsync(stopping))
{
    await db.Database.MigrateAsync(stopping);
}

await app.Services.GetRequiredService<PoCsvImporter>().ImportAsync(stopping);

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapInvoiceEndpoints();
app.MapGet("/health", async (HttpContext context, HealthCheckService healthChecks) =>
    {
        var report = await healthChecks.CheckHealthAsync(context.RequestAborted);
        context.Response.StatusCode = report.Status == HealthStatus.Unhealthy
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status200OK;
        await HealthResponseWriter.WriteAsync(context, report);
    })
    .WithTags("Health")
    .WithSummary("Reports queue depth, the last successful run and database reachability.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status503ServiceUnavailable);

await app.RunAsync();

public partial class Program;
