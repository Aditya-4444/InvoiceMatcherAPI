using InvoiceMatcher.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace InvoiceMatcher.Tests;

internal sealed class SqliteTestDb : IDbContextFactory<InvoiceDbContext>, IAsyncDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly DbContextOptions<InvoiceDbContext> options;

    private SqliteTestDb()
    {
        connection.Open();
        options = new DbContextOptionsBuilder<InvoiceDbContext>().UseSqlite(connection).Options;
    }

    public static async Task<SqliteTestDb> CreateAsync()
    {
        var db = new SqliteTestDb();
        await using var context = db.CreateDbContext();
        await context.Database.MigrateAsync();
        return db;
    }

    public InvoiceDbContext CreateDbContext() => new(options);

    public ValueTask DisposeAsync() => connection.DisposeAsync();
}
