using ErpGpt.GraphQLApi.Domain;

namespace ErpGpt.GraphQLApi.GraphQL;

// Shapes returned by the aggregation endpoints. They are plain records
// rather than entities, because they are computed answers, not table rows.

[GraphQLDescription("Headline sales numbers for a date range.")]
public record SalesSummary(
    [property: GraphQLDescription("Sum of every order's TotalDue in the range.")] decimal TotalRevenue,
    [property: GraphQLDescription("How many orders were placed in the range.")] int OrderCount,
    [property: GraphQLDescription("TotalRevenue divided by OrderCount — the average basket size.")] decimal AverageOrderValue,
    [property: GraphQLDescription("How many DISTINCT customers ordered in the range.")] int CustomerCount,
    DateOnly From,
    DateOnly To);

[GraphQLDescription("One customer's revenue, used for 'top/biggest customers' answers.")]
public record CustomerRevenue(
    int CustomerId,
    [property: GraphQLDescription("Store name or person's full name.")] string CustomerName,
    [property: GraphQLDescription("Sales region. Null when the customer is not assigned to one.")] string? Territory,
    decimal Revenue,
    int OrderCount);

[GraphQLDescription("One product's sales performance, used for 'best selling products' answers.")]
public record ProductSales(
    int ProductId,
    string ProductName,
    [property: GraphQLDescription("SKU code, e.g. 'BK-M68B-42'.")] string ProductNumber,
    [property: GraphQLDescription("Top-level category. Null for uncategorised products.")] string? Category,
    [property: GraphQLDescription("Revenue after line discounts.")] decimal Revenue,
    [property: GraphQLDescription("Total units sold across all orders in the range.")] int UnitsSold);

[GraphQLDescription("Revenue for one time bucket — a month, quarter or year.")]
public record PeriodRevenue(
    int Year,
    [property: GraphQLDescription("Month 1-12, quarter 1-4, or 1 when the interval is YEAR.")] int Period,
    [property: GraphQLDescription("Human-readable bucket label, e.g. '2024-03', '2024-Q1', '2024'.")] string Label,
    decimal Revenue,
    int OrderCount);

[GraphQLDescription("Revenue for one sales region.")]
public record TerritorySales(
    int TerritoryId,
    string Territory,
    [property: GraphQLDescription("Continent-level grouping, e.g. 'North America'.")] string Group,
    decimal Revenue,
    int OrderCount);

[GraphQLDescription("Revenue for one top-level product category.")]
public record CategorySales(
    int CategoryId,
    string Category,
    decimal Revenue,
    int UnitsSold);

[GraphQLDescription("Time bucket size for revenueByPeriod.")]
public enum Interval
{
    [GraphQLDescription("Group by calendar month.")] Month,
    [GraphQLDescription("Group by calendar quarter.")] Quarter,
    [GraphQLDescription("Group by calendar year.")] Year,
}
