using ErpGpt.GraphQLApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace ErpGpt.GraphQLApi.Data;

/// <summary>
/// Maps the 12 entities we expose onto the existing AdventureWorks tables.
///
/// IMPORTANT: this context NEVER creates or migrates the database. The
/// database is the source of truth and already exists — we only read it.
/// There is no Seeder and no EnsureCreated() call anywhere in this project.
/// </summary>
public class ErpDbContext(DbContextOptions<ErpDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<SalesOrder> Orders => Set<SalesOrder>();
    public DbSet<SalesOrderLine> OrderLines => Set<SalesOrderLine>();
    public DbSet<Territory> Territories => Set<Territory>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<SalesPerson> SalesPeople => Set<SalesPerson>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductSubcategory> ProductSubcategories => Set<ProductSubcategory>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<StateProvince> StateProvinces => Set<StateProvince>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // ---------------------------------------------------------- sales
        b.Entity<Customer>(e =>
        {
            e.ToTable("customer", "sales");
            e.HasKey(x => x.CustomerId);
            e.HasOne(x => x.Person).WithMany().HasForeignKey(x => x.PersonId);
            e.HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreId);
            e.HasOne(x => x.Territory).WithMany().HasForeignKey(x => x.TerritoryId);
            e.HasMany(x => x.Orders).WithOne(o => o.Customer).HasForeignKey(o => o.CustomerId);
        });

        b.Entity<Store>(e =>
        {
            e.ToTable("store", "sales");
            e.HasKey(x => x.BusinessEntityId);
            e.HasOne(x => x.SalesPerson).WithMany().HasForeignKey(x => x.SalesPersonId);
        });

        b.Entity<SalesPerson>(e =>
        {
            e.ToTable("salesperson", "sales");
            e.HasKey(x => x.BusinessEntityId);
            // Shares its primary key with Person — a one-to-one on the same id.
            e.HasOne(x => x.Person).WithMany().HasForeignKey(x => x.BusinessEntityId);
            e.HasOne(x => x.Territory).WithMany().HasForeignKey(x => x.TerritoryId);
        });

        b.Entity<Territory>(e =>
        {
            e.ToTable("salesterritory", "sales");
            e.HasKey(x => x.TerritoryId);
            // "group" is a reserved SQL word; the provider quotes it for us.
            e.Property(x => x.Group).HasColumnName("group");
        });

        b.Entity<SalesOrder>(e =>
        {
            e.ToTable("salesorderheader", "sales");
            e.HasKey(x => x.SalesOrderId);
            e.HasOne(x => x.SalesPerson).WithMany().HasForeignKey(x => x.SalesPersonId);
            e.HasOne(x => x.Territory).WithMany().HasForeignKey(x => x.TerritoryId);
            e.HasOne(x => x.ShipToAddress).WithMany().HasForeignKey(x => x.ShipToAddressId);
            e.HasMany(x => x.Lines).WithOne(l => l.Order).HasForeignKey(l => l.SalesOrderId);
        });

        b.Entity<SalesOrderLine>(e =>
        {
            e.ToTable("salesorderdetail", "sales");
            // Composite primary key, exactly as the table defines it.
            e.HasKey(x => new { x.SalesOrderId, x.SalesOrderDetailId });
            e.HasOne(x => x.Product).WithMany(p => p.OrderLines).HasForeignKey(x => x.ProductId);
        });

        // ----------------------------------------------------- production
        b.Entity<Product>(e =>
        {
            e.ToTable("product", "production");
            e.HasKey(x => x.ProductId);
            e.HasOne(x => x.Subcategory).WithMany(s => s.Products)
             .HasForeignKey(x => x.ProductSubcategoryId);
        });

        b.Entity<ProductSubcategory>(e =>
        {
            e.ToTable("productsubcategory", "production");
            e.HasKey(x => x.ProductSubcategoryId);
            e.HasOne(x => x.Category).WithMany(c => c.Subcategories)
             .HasForeignKey(x => x.ProductCategoryId);
        });

        b.Entity<ProductCategory>(e =>
        {
            e.ToTable("productcategory", "production");
            e.HasKey(x => x.ProductCategoryId);
        });

        // --------------------------------------------------------- person
        b.Entity<Person>(e =>
        {
            e.ToTable("person", "person");
            e.HasKey(x => x.BusinessEntityId);
        });

        b.Entity<Address>(e =>
        {
            e.ToTable("address", "person");
            e.HasKey(x => x.AddressId);
            e.HasOne(x => x.StateProvince).WithMany().HasForeignKey(x => x.StateProvinceId);
        });

        b.Entity<StateProvince>(e =>
        {
            e.ToTable("stateprovince", "person");
            e.HasKey(x => x.StateProvinceId);
            e.HasOne(x => x.Territory).WithMany().HasForeignKey(x => x.TerritoryId);
        });

        ApplyAdventureWorksNamingRules(b);
    }

    /// <summary>
    /// AdventureWorks column names are all lowercase with no separators
    /// (SalesOrderId -> salesorderid), so one rule replaces ~120 lines of
    /// HasColumnName(). Any column named explicitly above is left alone.
    ///
    /// It also pins every date column to `timestamp without time zone`,
    /// which is what these tables actually use — without this, Npgsql
    /// assumes `timestamptz` and silently shifts values by the time zone.
    /// </summary>
    private static void ApplyAdventureWorksNamingRules(ModelBuilder b)
    {
        foreach (var entity in b.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                if (property.GetColumnName() == property.Name)
                    property.SetColumnName(property.Name.ToLowerInvariant());

                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                    property.SetColumnType("timestamp without time zone");
            }
        }
    }
}
