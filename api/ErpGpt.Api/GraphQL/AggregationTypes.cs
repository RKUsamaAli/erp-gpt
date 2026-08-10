using ErpGpt.Api.Domain;

namespace ErpGpt.Api.GraphQL;

// Result shapes for aggregation endpoints. Kept flat and boring on purpose:
// the model only ever needs to name fields, never construct them.

[Semantic("One customer's ranked revenue position over a date range.")]
public record CustomerRevenue(int CustomerId, string Name, string Region, decimal TotalRevenue, int OrderCount);

[Semantic("Revenue summed over one calendar period (month or quarter).")]
public record PeriodRevenue(int Year, int Period, decimal Revenue, int OrderCount);

[Semantic("Average order value for one calendar month.")]
public record PeriodAverage(int Year, int Month, decimal AverageOrderValue, int OrderCount);

[Semantic("Revenue summed by sales region over a date range.")]
public record RegionSales(string Region, decimal Revenue, int OrderCount);

[Semantic("One product's ranked sales over a date range.")]
public record ProductSales(int ProductId, string Sku, string Name, decimal Revenue, int UnitsSold);

[Semantic("Grouping interval for time-based aggregations.")]
public enum Interval
{
    [Semantic("Group by calendar month.")] Month,
    [Semantic("Group by calendar quarter.")] Quarter,
}
