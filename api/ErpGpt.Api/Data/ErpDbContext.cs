using ErpGpt.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ErpGpt.Api.Data;

public class ErpDbContext(DbContextOptions<ErpDbContext> options) : DbContext(options)
{
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<StockItem> StockItems => Set<StockItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // ---- money precision, uniform
        foreach (var prop in b.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(decimal)))
            prop.SetPrecision(18);

        // ---- uniqueness
        b.Entity<Product>().HasIndex(p => p.Sku).IsUnique();
        b.Entity<Region>().HasIndex(r => r.Name).IsUnique();
        b.Entity<Category>().HasIndex(c => c.Name).IsUnique();
        b.Entity<Invoice>().HasIndex(i => i.OrderId).IsUnique(); // one invoice per order
        b.Entity<StockItem>().HasIndex(s => new { s.ProductId, s.WarehouseCode }).IsUnique();

        // ---- every column the API filters or groups by gets an index
        b.Entity<Order>().HasIndex(o => o.OrderDate);
        b.Entity<Order>().HasIndex(o => o.Status);
        b.Entity<Order>().HasIndex(o => o.CustomerId);
        b.Entity<OrderLine>().HasIndex(l => l.OrderId);
        b.Entity<OrderLine>().HasIndex(l => l.ProductId);
        b.Entity<Invoice>().HasIndex(i => i.DueDate);
        b.Entity<Customer>().HasIndex(c => c.RegionId);
        b.Entity<Customer>().HasIndex(c => c.City);
        b.Entity<StockMovement>().HasIndex(m => new { m.ProductId, m.MovedAt });

        // ---- relationships: explicit, restrict deletes on transactional data
        b.Entity<Customer>()
            .HasOne(c => c.Region).WithMany(r => r.Customers)
            .HasForeignKey(c => c.RegionId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<Product>()
            .HasOne(p => p.Category).WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<Product>()
            .HasOne(p => p.Supplier).WithMany(s => s.Products)
            .HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<Order>()
            .HasOne(o => o.Customer).WithMany(c => c.Orders)
            .HasForeignKey(o => o.CustomerId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<OrderLine>()
            .HasOne(l => l.Order).WithMany(o => o.Lines)
            .HasForeignKey(l => l.OrderId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<OrderLine>()
            .HasOne(l => l.Product).WithMany(p => p.OrderLines)
            .HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<Invoice>()
            .HasOne(i => i.Order).WithOne(o => o.Invoice)
            .HasForeignKey<Invoice>(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}
