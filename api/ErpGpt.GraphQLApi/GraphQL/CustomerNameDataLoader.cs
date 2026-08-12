using ErpGpt.GraphQLApi.Data;
using Microsoft.EntityFrameworkCore;

namespace ErpGpt.GraphQLApi.GraphQL;

/// <summary>
/// Loads customer names in batches.
///
/// Why this exists: a customer's name lives in EITHER the store OR the person
/// table, so `displayName` depends on two relationships. Projection only reads
/// the columns a query actually asks for, and it will not pull in a
/// relationship the caller never mentioned — so a computed property would
/// quietly fall back to "Customer 123" whenever `store` was not selected.
///
/// A DataLoader fixes that correctly: HotChocolate collects every customer id
/// in the result, then asks for all their names in ONE extra query, no matter
/// how many rows the page holds.
/// </summary>
public class CustomerNameDataLoader(
    IDbContextFactory<ErpDbContext> dbFactory,
    IBatchScheduler batchScheduler,
    DataLoaderOptions options)
    : BatchDataLoader<int, string>(batchScheduler, options)
{
    protected override async Task<IReadOnlyDictionary<int, string>> LoadBatchAsync(
        IReadOnlyList<int> customerIds,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var rows = await db.Customers
            .Where(c => customerIds.Contains(c.CustomerId))
            .Select(c => new
            {
                c.CustomerId,
                Name = c.Store != null
                    ? c.Store.Name
                    : c.Person != null
                        ? c.Person.FirstName + " " + c.Person.LastName
                        : null,
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            r => r.CustomerId,
            r => r.Name ?? $"Customer {r.CustomerId}");
    }
}
