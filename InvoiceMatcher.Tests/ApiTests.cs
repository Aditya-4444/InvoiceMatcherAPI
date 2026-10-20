using System.Net;
using System.Text.Json;
using InvoiceMatcher.Data;
using InvoiceMatcher.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceMatcher.Tests;

public sealed class ApiTests : IAsyncLifetime
{
    private readonly string folder = Directory.CreateTempSubdirectory("api-tests").FullName;
    private WebApplicationFactory<Program> factory = null!;
    private HttpClient client = null!;

    public async Task InitializeAsync()
    {
        File.WriteAllText(Path.Combine(folder, "pos.csv"), "PONumber,Vendor,Amount,Status\n");
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Invoices", $"Data Source={Path.Combine(folder, "test.db")}");
            builder.UseSetting("InvoiceProcessing:InboxPath", Path.Combine(folder, "inbox"));
            builder.UseSetting("InvoiceProcessing:ArchivePath", Path.Combine(folder, "archive"));
            builder.UseSetting("InvoiceProcessing:FailedPath", Path.Combine(folder, "failed"));
            builder.UseSetting("InvoiceProcessing:PosCsvPath", Path.Combine(folder, "pos.csv"));
        });
        client = factory.CreateClient();

        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<InvoiceDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        db.Invoices.AddRange(
            NewInvoice("a", InvoiceStatus.Matched),
            NewInvoice("b", InvoiceStatus.Matched),
            NewInvoice("c", InvoiceStatus.Unmatched),
            NewInvoice("d", InvoiceStatus.Exception));
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public async Task InvoicesCanBeFilteredByStatus()
    {
        var all = await GetJsonAsync("/invoices");
        Assert.Equal(4, all.GetProperty("total").GetInt32());

        var matched = await GetJsonAsync("/invoices?status=matched");
        Assert.Equal(2, matched.GetProperty("total").GetInt32());
        Assert.All(matched.GetProperty("items").EnumerateArray(), item => Assert.Equal("Matched", item.GetProperty("status").GetString()));

        var exception = await GetJsonAsync("/invoices?status=EXCEPTION");
        Assert.Equal(1, exception.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task PagingLimitsItemsButReportsTotal()
    {
        var page = await GetJsonAsync("/invoices?page=2&pageSize=3");

        Assert.Equal(4, page.GetProperty("total").GetInt32());
        Assert.Equal(1, page.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task UnknownStatusReturnsProblemDetails()
    {
        var response = await client.GetAsync("/invoices?status=bogus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.True(body.GetProperty("errors").TryGetProperty("status", out _));
    }

    [Fact]
    public async Task HealthReportsQueueDepthAndLastRun()
    {
        var health = await GetJsonAsync("/health");

        Assert.Equal("Healthy", health.GetProperty("status").GetString());
        var data = health.GetProperty("checks")[0].GetProperty("data");
        Assert.Equal(0, data.GetProperty("queueDepth").GetInt32());
        Assert.Equal("none yet", data.GetProperty("lastSuccessfulRun").GetString());
    }

    [Fact]
    public async Task SwaggerDocumentsOnlyInvoicesAndHealth()
    {
        var swagger = await GetJsonAsync("/swagger/v1/swagger.json");

        var paths = swagger.GetProperty("paths").EnumerateObject().Select(p => p.Name).Order().ToArray();
        Assert.Equal(["/health", "/invoices"], paths);
    }

    private async Task<JsonElement> GetJsonAsync(string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private static Invoice NewInvoice(string name, InvoiceStatus status) => new()
    {
        SourceFileRef = $"{name}.pdf",
        FileHash = name,
        Status = status,
        CreatedUtc = DateTimeOffset.UtcNow,
        UpdatedUtc = DateTimeOffset.UtcNow
    };
}
