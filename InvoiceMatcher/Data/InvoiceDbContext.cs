using InvoiceMatcher.Domain;
using Microsoft.EntityFrameworkCore;

namespace InvoiceMatcher.Data;

public sealed class InvoiceDbContext(DbContextOptions<InvoiceDbContext> options) : DbContext(options)
{
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.Property(x => x.Status).HasConversion<string>();
            entity.Property(x => x.SourceFileRef).IsRequired();
            entity.Property(x => x.FileHash).IsRequired();
            entity.HasIndex(x => x.FileHash).IsUnique();
            entity.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<PurchaseOrder>(entity =>
        {
            entity.Property(x => x.Status).HasConversion<string>();
            entity.Property(x => x.PONumber).IsRequired();
            entity.Property(x => x.Vendor).IsRequired();
            entity.HasIndex(x => x.PONumber).IsUnique();
        });
    }
}
