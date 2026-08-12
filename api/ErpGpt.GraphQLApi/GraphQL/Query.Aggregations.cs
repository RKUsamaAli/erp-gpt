using ErpGpt.GraphQLApi.Data;
using ErpGpt.GraphQLApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace ErpGpt.GraphQLApi.GraphQL;

/// <summary>
/// The "how much" endpoints. Two rules hold for every method here:
///
/// 1. The maths lives in C# and is tested. Callers pick an endpoint and pass
///    parameters; they never compose the aggregation themselves.
///
/// 2. GROUP BY happens in Postgres, not in memory. EF cannot translate a
///    record constructor inside GroupBy().Select(), so each query groups into
///    an ANONYMOUS type first and maps to the record afterwards. Getting this
///    backwards silently pulls every row into the API process.
///
/// Dates are DateOnly, so callers write "2024-01-01" rather than a full ISO
/// timestamp. `to` is inclusive of the whole day.
/// </summary>
public partial class Query
{
    private const int MaxLimit = 100;

    // ----------------------------------------------------------- helpers

    /// <summary>Turns the requested day range into the half-open [start, end)
    /// window the SQL actually uses, so orders placed late on the `to` day
    /// are still counted.</summary>
    private static (DateTime Start, DateTime End) Range(DateOnly from, DateOnly to)
    {
        if (from > to)
            throw new GraphQLException(ErrorBuilder.New()
                .SetMessage($"'from' ({from:yyyy-MM-dd}) is after 'to' ({to:yyyy-MM-dd}). Swap the two dates.")
                .SetCode("INVALID_DATE_RANGE").Build());

        return (from.ToDateTime(TimeOnly.MinValue), to.AddDays(1).ToDateTime(TimeOnly.MinValue));
    }

    private static int Cap(int limit) => Math.Clamp(limit, 1, MaxLimit);

    // --------------------------------------------------------- summary

    [GraphQLDescription("Headline sales figures for a date range: total revenue, order count, average order value and how many distinct customers ordered. This is the endpoint for 'how much did we sell' and 'how many orders'.")]
    public async Task<SalesSummary> GetSalesSummary(ErpDbContext db, DateOnly from, DateOnly to)
    {
        var (start, end) = Range(from, to);
        var orders = db.Orders.Where(o => o.OrderDate >= start && o.OrderDate < end);

        // Three small aggregate queries, each one fully translated to SQL.
        var revenue = await orders.SumAsync(o => o.TotalDue ?? 0m);
        var orderCount = await orders.CountAsync();
        var customerCount = await orders.Select(o => o.CustomerId).Distinct().CountAsync();

        return new SalesSummary(
            revenue,
            orderCount,
            orderCount == 0 ? 0m : Math.Round(revenue / orderCount, 2),
            customerCount,
            from, to);
    }

    // --------------------------------------------------------- rankings

    [GraphQLDescription("Rank customers by revenue in a date range. Use for any 'biggest', 'top' or 'best' customers question.")]
    public async Task<List<CustomerRevenue>> GetTopCustomers(
        ErpDbContext db, DateOnly from, DateOnly to, int limit = 10)
    {
        var (start, end) = Range(from, to);

        var rows = await db.Orders
            .Where(o => o.OrderDate >= start && o.OrderDate < end)
            .GroupBy(o => new
            {
                o.CustomerId,
                // The name lives in store OR person — resolved here in SQL.
                Name = o.Customer.Store != null
                    ? o.Customer.Store.Name
                    : o.Customer.Person != null
                        ? o.Customer.Person.FirstName + " " + o.Customer.Person.LastName
                        : null,
                Territory = o.Customer.Territory != null ? o.Customer.Territory.Name : null,
            })
            .Select(g => new
            {
                g.Key.CustomerId,
                g.Key.Name,
                g.Key.Territory,
                Revenue = g.Sum(x => x.TotalDue ?? 0m),
                OrderCount = g.Count(),
            })
            .OrderByDescending(x => x.Revenue)
            .Take(Cap(limit))
            .ToListAsync();

        return rows
            .Select(r => new CustomerRevenue(
                r.CustomerId, r.Name ?? $"Customer {r.CustomerId}", r.Territory, r.Revenue, r.OrderCount))
            .ToList();
    }

    [GraphQLDescription("Rank products by revenue and units sold in a date range. Use for 'best selling' or 'top products'. Revenue is net of line discounts.")]
    public async Task<List<ProductSales>> GetTopProducts(
        ErpDbContext db, DateOnly from, DateOnly to, int limit = 10)
    {
        var (start, end) = Range(from, to);

        var rows = await db.OrderLines
            .Where(l => l.Order.OrderDate >= start && l.Order.OrderDate < end)
            .GroupBy(l => new
            {
                l.ProductId,
                l.Product.Name,
                l.Product.ProductNumber,
                Category = l.Product.Subcategory != null ? l.Product.Subcategory.Category.Name : null,
            })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.Name,
                g.Key.ProductNumber,
                g.Key.Category,
                Revenue = g.Sum(x => x.OrderQty * x.UnitPrice * (1 - x.UnitPriceDiscount)),
                UnitsSold = g.Sum(x => (int)x.OrderQty),
            })
            .OrderByDescending(x => x.Revenue)
            .Take(Cap(limit))
            .ToListAsync();

        return rows
            .Select(r => new ProductSales(
                r.ProductId, r.Name, r.ProductNumber, r.Category, r.Revenue, r.UnitsSold))
            .ToList();
    }

    // ------------------------------------------------------- over time

    [GraphQLDescription("Revenue per month, quarter or year across a date range. Use for trends, 'revenue each month', or comparing periods.")]
    public async Task<List<PeriodRevenue>> GetRevenueByPeriod(
        ErpDbContext db, DateOnly from, DateOnly to, Interval interval = Interval.Month)
    {
        var (start, end) = Range(from, to);
        var orders = db.Orders.Where(o => o.OrderDate >= start && o.OrderDate < end);

        // One branch per bucket size — each is a separate translatable query.
        // A single clever expression would not survive translation.
        switch (interval)
        {
            case Interval.Year:
            {
                var rows = await orders
                    .GroupBy(o => o.OrderDate.Year)
                    .Select(g => new { Year = g.Key, Revenue = g.Sum(x => x.TotalDue ?? 0m), Count = g.Count() })
                    .OrderBy(x => x.Year)
                    .ToListAsync();

                return rows
                    .Select(r => new PeriodRevenue(r.Year, 1, $"{r.Year}", r.Revenue, r.Count))
                    .ToList();
            }

            case Interval.Quarter:
            {
                var rows = await orders
                    .GroupBy(o => new { o.OrderDate.Year, Quarter = (o.OrderDate.Month - 1) / 3 + 1 })
                    .Select(g => new { g.Key.Year, g.Key.Quarter, Revenue = g.Sum(x => x.TotalDue ?? 0m), Count = g.Count() })
                    .OrderBy(x => x.Year).ThenBy(x => x.Quarter)
                    .ToListAsync();

                return rows
                    .Select(r => new PeriodRevenue(r.Year, r.Quarter, $"{r.Year}-Q{r.Quarter}", r.Revenue, r.Count))
                    .ToList();
            }

            default:
            {
                var rows = await orders
                    .GroupBy(o => new { o.OrderDate.Year, o.OrderDate.Month })
                    .Select(g => new { g.Key.Year, g.Key.Month, Revenue = g.Sum(x => x.TotalDue ?? 0m), Count = g.Count() })
                    .OrderBy(x => x.Year).ThenBy(x => x.Month)
                    .ToListAsync();

                return rows
                    .Select(r => new PeriodRevenue(r.Year, r.Month, $"{r.Year}-{r.Month:00}", r.Revenue, r.Count))
                    .ToList();
            }
        }
    }

    // ------------------------------------------------------ breakdowns

    [GraphQLDescription("Revenue by sales region for a date range. Use for 'sales by region' or 'which region sells the most'.")]
    public async Task<List<TerritorySales>> GetSalesByTerritory(
        ErpDbContext db, DateOnly from, DateOnly to)
    {
        var (start, end) = Range(from, to);

        var rows = await db.Orders
            .Where(o => o.OrderDate >= start && o.OrderDate < end && o.Territory != null)
            .GroupBy(o => new { o.Territory!.TerritoryId, o.Territory.Name, o.Territory.Group })
            .Select(g => new
            {
                g.Key.TerritoryId,
                g.Key.Name,
                g.Key.Group,
                Revenue = g.Sum(x => x.TotalDue ?? 0m),
                Count = g.Count(),
            })
            .OrderByDescending(x => x.Revenue)
            .ToListAsync();

        return rows
            .Select(r => new TerritorySales(r.TerritoryId, r.Name, r.Group, r.Revenue, r.Count))
            .ToList();
    }

    [GraphQLDescription("Revenue by top-level product category (Bikes, Components, Clothing, Accessories) for a date range.")]
    public async Task<List<CategorySales>> GetSalesByCategory(
        ErpDbContext db, DateOnly from, DateOnly to)
    {
        var (start, end) = Range(from, to);

        var rows = await db.OrderLines
            .Where(l => l.Order.OrderDate >= start && l.Order.OrderDate < end
                     && l.Product.Subcategory != null)
            .GroupBy(l => new
            {
                l.Product.Subcategory!.Category.ProductCategoryId,
                l.Product.Subcategory.Category.Name,
            })
            .Select(g => new
            {
                g.Key.ProductCategoryId,
                g.Key.Name,
                Revenue = g.Sum(x => x.OrderQty * x.UnitPrice * (1 - x.UnitPriceDiscount)),
                UnitsSold = g.Sum(x => (int)x.OrderQty),
            })
            .OrderByDescending(x => x.Revenue)
            .ToListAsync();

        return rows
            .Select(r => new CategorySales(r.ProductCategoryId, r.Name, r.Revenue, r.UnitsSold))
            .ToList();
    }
}
