using ErpGpt.GraphQLApi.Data;
using ErpGpt.GraphQLApi.Domain;
using HotChocolate.Data;

namespace ErpGpt.GraphQLApi.GraphQL;

/// <summary>
/// The read surface of the API. Every operation a caller can invoke lives on
/// this class (split across two files: browse here, aggregations next door).
///
/// Every list endpoint below carries the same four abilities, which is the
/// whole point of the design — one endpoint answers many questions:
///
///   [UseOffsetPaging] -> skip / take / totalCount, capped at 100 rows
///   [UseFiltering]    -> where: { ... }  (the WHERE clause)
///   [UseSorting]      -> order: [{ ... }]
///   [UseProjection]   -> only the columns actually asked for are read
///
/// Attribute order matters: paging must come first, projection before
/// filtering and sorting.
/// </summary>
public partial class Query
{
    // ------------------------------------------------------------ browse

    [GraphQLDescription("Browse customers. Filter by territory, by whether they are a store or a person, or by name through the person/store relationship.")]
    [UseOffsetPaging(MaxPageSize = 100, IncludeTotalCount = true)]
    [UseProjection, UseFiltering, UseSorting]
    public IQueryable<Customer> GetCustomers(ErpDbContext db) => db.Customers;

    [GraphQLDescription("Browse orders. Filter by date range, value, online/offline, or by anything on the related customer. For totals across many orders use the aggregation endpoints instead — they are far cheaper.")]
    [UseOffsetPaging(MaxPageSize = 100, IncludeTotalCount = true)]
    [UseProjection, UseFiltering, UseSorting]
    public IQueryable<SalesOrder> GetOrders(ErpDbContext db) => db.Orders;

    [GraphQLDescription("Browse individual order lines. Use when the question is about products inside orders, e.g. 'every line containing a helmet'.")]
    [UseOffsetPaging(MaxPageSize = 100, IncludeTotalCount = true)]
    [UseProjection, UseFiltering, UseSorting]
    public IQueryable<SalesOrderLine> GetOrderLines(ErpDbContext db) => db.OrderLines;

    [GraphQLDescription("Browse the product catalogue. Filter by price, colour, size, or by subcategory and category.")]
    [UseOffsetPaging(MaxPageSize = 100, IncludeTotalCount = true)]
    [UseProjection, UseFiltering, UseSorting]
    public IQueryable<Product> GetProducts(ErpDbContext db) => db.Products;

    [GraphQLDescription("Browse the four top-level product categories and their subcategories. Useful for building filter lists.")]
    [UseOffsetPaging(MaxPageSize = 100, IncludeTotalCount = true)]
    [UseProjection, UseFiltering, UseSorting]
    public IQueryable<ProductCategory> GetProductCategories(ErpDbContext db) => db.ProductCategories;

    [GraphQLDescription("Browse sales regions. There are ten, e.g. Northwest, Canada, United Kingdom.")]
    [UseOffsetPaging(MaxPageSize = 100, IncludeTotalCount = true)]
    [UseProjection, UseFiltering, UseSorting]
    public IQueryable<Territory> GetTerritories(ErpDbContext db) => db.Territories;

    [GraphQLDescription("Browse the sales team. Join through person for their names.")]
    [UseOffsetPaging(MaxPageSize = 100, IncludeTotalCount = true)]
    [UseProjection, UseFiltering, UseSorting]
    public IQueryable<SalesPerson> GetSalesPeople(ErpDbContext db) => db.SalesPeople;

    // ------------------------------------------------------------ detail
    // A filter could do these, but a named id lookup reads better and is
    // easier for the Phase 4 AI layer to pick correctly.

    [GraphQLDescription("One customer by id, with their order history. Use when the user names or identifies a specific customer.")]
    [UseSingleOrDefault, UseProjection]
    public IQueryable<Customer> GetCustomer(ErpDbContext db, int id) =>
        db.Customers.Where(c => c.CustomerId == id);

    [GraphQLDescription("One order by id, with all its lines and products.")]
    [UseSingleOrDefault, UseProjection]
    public IQueryable<SalesOrder> GetOrder(ErpDbContext db, int id) =>
        db.Orders.Where(o => o.SalesOrderId == id);

    [GraphQLDescription("One product by id, with its category and pricing.")]
    [UseSingleOrDefault, UseProjection]
    public IQueryable<Product> GetProduct(ErpDbContext db, int id) =>
        db.Products.Where(p => p.ProductId == id);
}
