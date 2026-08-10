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

    [Semantic("Rank customers by total revenue in a date range. Use for any 'biggest/top/best customers' question. Excludes cancelled orders automatically.")]
    public async Task<List<CustomerRevenue>> GetTopCustomers(
        ErpDbContext db, DateOnly from, DateOnly to, int? limit = 10)
    {
        ValidateRange(from, to);
        return await RevenueOrders(db, from, to)
            .SelectMany(o => o.Lines, (o, l) => new { o.CustomerId, o.Customer.Name, Region = o.Customer.Region.Name, o.Id, Value = l.Quantity * l.UnitPrice })
            .GroupBy(x => new { x.CustomerId, x.Name, x.Region })
            .Select(g => new CustomerRevenue(
                g.Key.CustomerId, g.Key.Name, g.Key.Region,
                g.Sum(x => x.Value),
                g.Select(x => x.Id).Distinct().Count()))
            .OrderByDescending(c => c.TotalRevenue)
            .Take(Cap(limit))
            .ToListAsync();
    }

    [Semantic("Rank products by revenue and units sold in a date range. Use for 'best selling / top products'. Excludes cancelled orders automatically.")]
    public async Task<List<ProductSales>> GetTopProducts(
        ErpDbContext db, DateOnly from, DateOnly to, int? limit = 10)
    {
        ValidateRange(from, to);
        return await RevenueOrders(db, from, to)
            .SelectMany(o => o.Lines)
            .GroupBy(l => new { l.ProductId, l.Product.Sku, l.Product.Name })
            .Select(g => new ProductSales(
                g.Key.ProductId, g.Key.Sku, g.Key.Name,
                g.Sum(l => l.Quantity * l.UnitPrice),
                g.Sum(l => l.Quantity)))
            .OrderByDescending(p => p.Revenue)
            .Take(Cap(limit))
            .ToListAsync();
    }

    // ------------------------------------------------------ aggregation

    [Semantic("Revenue per calendar month or quarter across a date range. Use for 'revenue each month', 'quarterly sales', trends over time. Excludes cancelled orders.")]
    public async Task<List<PeriodRevenue>> GetRevenueByPeriod(
        ErpDbContext db, DateOnly from, DateOnly to, Interval interval = Interval.Month)
    {
        ValidateRange(from, to);
        var lines = RevenueOrders(db, from, to)
            .SelectMany(o => o.Lines, (o, l) => new { o.Id, o.OrderDate, Value = l.Quantity * l.UnitPrice });

        var grouped = interval == Interval.Month
            ? lines.GroupBy(x => new { x.OrderDate.Year, Period = x.OrderDate.Month })
            : lines.GroupBy(x => new { x.OrderDate.Year, Period = (x.OrderDate.Month - 1) / 3 + 1 });

        return await grouped
            .Select(g => new PeriodRevenue(
                g.Key.Year, g.Key.Period,
                g.Sum(x => x.Value),
                g.Select(x => x.Id).Distinct().Count()))
            .OrderBy(p => p.Year).ThenBy(p => p.Period)
            .ToListAsync();
    }

    [Semantic("Average order value per month over a date range. 'Basket size' questions come here. Cancelled orders excluded.")]
    public async Task<List<PeriodAverage>> GetAverageOrderValue(
        ErpDbContext db, DateOnly from, DateOnly to)
    {
        ValidateRange(from, to);
        var perOrder = RevenueOrders(db, from, to)
            .Select(o => new { o.OrderDate.Year, o.OrderDate.Month, Total = o.Lines.Sum(l => l.Quantity * l.UnitPrice) });

        return await perOrder
            .GroupBy(x => new { x.Year, x.Month })
            .Select(g => new PeriodAverage(g.Key.Year, g.Key.Month, g.Average(x => x.Total), g.Count()))
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .ToListAsync();
    }

    [Semantic("Revenue by sales region over a date range. Use for 'sales by region', 'which region sells the most'. Cancelled orders excluded.")]
    public async Task<List<RegionSales>> GetSalesByRegion(
        ErpDbContext db, DateOnly from, DateOnly to)
    {
        ValidateRange(from, to);
        return await RevenueOrders(db, from, to)
            .SelectMany(o => o.Lines, (o, l) => new { o.Id, Region = o.Customer.Region.Name, Value = l.Quantity * l.UnitPrice })
            .GroupBy(x => x.Region)
            .Select(g => new RegionSales(
                g.Key,
                g.Sum(x => x.Value),
                g.Select(x => x.Id).Distinct().Count()))
            .OrderByDescending(r => r.Revenue)
            .ToListAsync();
    }

    // TODO next (same pattern; add kb/ file in the same PR):
    //   topSuppliers, stockValuation, outstandingByAge,
    //   periodComparison, salesTrend, growthByCategory, productDetail
}
