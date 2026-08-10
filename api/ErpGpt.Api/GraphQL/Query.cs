using ErpGpt.Api.Data;
using ErpGpt.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ErpGpt.Api.GraphQL;

/// <summary>
/// The cage. Every operation the AI can ever invoke is defined here.
/// Rules (api/README.md):
///   - aggregations are computed HERE in tested LINQ, never composed by the model
///   - every list endpoint is paged with a hard cap (MaxPageSize)
///   - errors are readable: the retry loop feeds them back to the model
/// Change an endpoint => update its kb/ file in the same PR (CI enforces).
/// </summary>
public class Query
{
    private const int MaxTop = 100; // server-side ceiling for ranking endpoints

    // ------------------------------------------------------------ helpers

    private static IQueryable<Order> RevenueOrders(ErpDbContext db, DateOnly from, DateOnly to) =>
        db.Orders.Where(o => o.Status != OrderStatus.Cancelled
                          && o.OrderDate >= from && o.OrderDate <= to);

    private static void ValidateRange(DateOnly from, DateOnly to)
    {
        if (from > to)
            throw new GraphQLException(ErrorBuilder.New()
                .SetMessage($"'from' ({from:yyyy-MM-dd}) is after 'to' ({to:yyyy-MM-dd}). Swap the dates.")
                .SetCode("INVALID_DATE_RANGE").Build());
    }

    private static int Cap(int? limit) => Math.Clamp(limit ?? 10, 1, MaxTop);

    // ------------------------------------------------------- list (paged)

    [Semantic("Browse customers. Supports filtering (city, region, isActive), sorting, and paging.")]
    [UsePaging(MaxPageSize = 100, IncludeTotalCount = true)]
    [UseProjection, UseFiltering, UseSorting]
    public IQueryable<Customer> GetCustomers(ErpDbContext db) => db.Customers;

    [Semantic("Browse orders. Filter by status or date; note that revenue questions should use the aggregation endpoints instead.")]
    [UsePaging(MaxPageSize = 100, IncludeTotalCount = true)]
    [UseProjection, UseFiltering, UseSorting]
    public IQueryable<Order> GetOrders(ErpDbContext db) => db.Orders;

    [Semantic("Browse the product catalogue. Filter by category, supplier, or isActive.")]
    [UsePaging(MaxPageSize = 100, IncludeTotalCount = true)]
    [UseProjection, UseFiltering, UseSorting]
    public IQueryable<Product> GetProducts(ErpDbContext db) => db.Products;

    [Semantic("Browse suppliers.")]
    [UsePaging(MaxPageSize = 100, IncludeTotalCount = true)]
    [UseProjection, UseFiltering, UseSorting]
    public IQueryable<Supplier> GetSuppliers(ErpDbContext db) => db.Suppliers;

    [Semantic("Browse invoices. 'Outstanding' means paidOn is null; 'overdue' additionally means dueDate is past.")]
    [UsePaging(MaxPageSize = 100, IncludeTotalCount = true)]
    [UseProjection, UseFiltering, UseSorting]
    public IQueryable<Invoice> GetInvoices(ErpDbContext db) => db.Invoices;

    [Semantic("Browse stock per product per warehouse. 'Low stock' means quantityOnHand <= reorderLevel.")]
    [UsePaging(MaxPageSize = 100, IncludeTotalCount = true)]
    [UseProjection, UseFiltering, UseSorting]
    public IQueryable<StockItem> GetStockItems(ErpDbContext db) => db.StockItems;

    // ----------------------------------------------------------- detail

    [Semantic("Full detail for ONE customer by id, including recent orders. Use when the user names a specific customer.")]
    [UseProjection]
    public IQueryable<Customer> GetCustomerDetail(ErpDbContext db, int id) =>
        db.Customers.Where(c => c.Id == id);

    [Semantic("Full detail for ONE order by id, including its lines and invoice.")]
    [UseProjection]
    public IQueryable<Order> GetOrderDetail(ErpDbContext db, int id) =>
        db.Orders.Where(o => o.Id == id);

    // ---------------------------------------------------------- ranking
    // Pattern for ALL aggregations below: SQL does joins/filters/per-row math
    // (what it translates well); the final GroupBy happens in memory (which
    // EF cannot reliably translate when combined with Distinct().Count() and
    // record constructors). At seed scale (~2k orders) this is instant; if
    // data grows 100x, revisit with raw SQL views.

    [Semantic("Rank customers by total revenue in a date range. Use for any 'biggest/top/best customers' question. Excludes cancelled orders automatically.")]
    public async Task<List<CustomerRevenue>> GetTopCustomers(
        ErpDbContext db, DateOnly from, DateOnly to, int? limit = 10)
    {
        ValidateRange(from, to);
        var perOrder = await RevenueOrders(db, from, to)
            .Select(o => new
            {
                o.Id,
                o.CustomerId,
                CustomerName = o.Customer.Name,
                Region = o.Customer.Region.Name,
                Total = o.Lines.Sum(l => l.Quantity * l.UnitPrice),
            })
            .ToListAsync(); // SQL ends here

        return perOrder
            .GroupBy(x => new { x.CustomerId, x.CustomerName, x.Region })
            .Select(g => new CustomerRevenue(
                g.Key.CustomerId, g.Key.CustomerName, g.Key.Region,
                g.Sum(x => x.Total), g.Count()))
            .OrderByDescending(c => c.TotalRevenue)
            .Take(Cap(limit))
            .ToList();
    }

    [Semantic("Rank products by revenue and units sold in a date range. Use for 'best selling / top products'. Excludes cancelled orders automatically.")]
    public async Task<List<ProductSales>> GetTopProducts(
        ErpDbContext db, DateOnly from, DateOnly to, int? limit = 10)
    {
        ValidateRange(from, to);
        var perLine = await RevenueOrders(db, from, to)
            .SelectMany(o => o.Lines)
            .Select(l => new
            {
                l.ProductId,
                l.Product.Sku,
                l.Product.Name,
                l.Quantity,
                Value = l.Quantity * l.UnitPrice,
            })
            .ToListAsync();

        return perLine
            .GroupBy(x => new { x.ProductId, x.Sku, x.Name })
            .Select(g => new ProductSales(
                g.Key.ProductId, g.Key.Sku, g.Key.Name,
                g.Sum(x => x.Value), g.Sum(x => x.Quantity)))
            .OrderByDescending(p => p.Revenue)
            .Take(Cap(limit))
            .ToList();
    }

    // ------------------------------------------------------ aggregation

    [Semantic("Revenue per calendar month or quarter across a date range. Use for 'revenue each month', 'quarterly sales', trends over time. Excludes cancelled orders.")]
    public async Task<List<PeriodRevenue>> GetRevenueByPeriod(
        ErpDbContext db, DateOnly from, DateOnly to, Interval interval = Interval.Month)
    {
        ValidateRange(from, to);
        var perOrder = await RevenueOrders(db, from, to)
            .Select(o => new
            {
                o.OrderDate,
                Total = o.Lines.Sum(l => l.Quantity * l.UnitPrice),
            })
            .ToListAsync();

        return perOrder
            .GroupBy(x => new
            {
                x.OrderDate.Year,
                Period = interval == Interval.Month ? x.OrderDate.Month : (x.OrderDate.Month - 1) / 3 + 1,
            })
            .Select(g => new PeriodRevenue(g.Key.Year, g.Key.Period, g.Sum(x => x.Total), g.Count()))
            .OrderBy(p => p.Year).ThenBy(p => p.Period)
            .ToList();
    }

    [Semantic("Average order value per month over a date range. 'Basket size' questions come here. Cancelled orders excluded.")]
    public async Task<List<PeriodAverage>> GetAverageOrderValue(
        ErpDbContext db, DateOnly from, DateOnly to)
    {
        ValidateRange(from, to);
        var perOrder = await RevenueOrders(db, from, to)
            .Select(o => new
            {
                o.OrderDate.Year,
                o.OrderDate.Month,
                Total = o.Lines.Sum(l => l.Quantity * l.UnitPrice),
            })
            .ToListAsync();

        return perOrder
            .GroupBy(x => new { x.Year, x.Month })
            .Select(g => new PeriodAverage(g.Key.Year, g.Key.Month, g.Average(x => x.Total), g.Count()))
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .ToList();
    }

    [Semantic("Revenue by sales region over a date range. Use for 'sales by region', 'which region sells the most'. Cancelled orders excluded.")]
    public async Task<List<RegionSales>> GetSalesByRegion(
        ErpDbContext db, DateOnly from, DateOnly to)
    {
        ValidateRange(from, to);
        var perOrder = await RevenueOrders(db, from, to)
            .Select(o => new
            {
                Region = o.Customer.Region.Name,
                Total = o.Lines.Sum(l => l.Quantity * l.UnitPrice),
            })
            .ToListAsync();

        return perOrder
            .GroupBy(x => x.Region)
            .Select(g => new RegionSales(g.Key, g.Sum(x => x.Total), g.Count()))
            .OrderByDescending(r => r.Revenue)
            .ToList();
    }

    // TODO next (same pattern; add kb/ file in the same PR):
    //   topSuppliers, stockValuation, outstandingByAge,
    //   periodComparison, salesTrend, growthByCategory, productDetail
}
