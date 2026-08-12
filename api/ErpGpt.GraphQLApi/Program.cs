using ErpGpt.GraphQLApi.Data;
using ErpGpt.GraphQLApi.GraphQL;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// The API only ever reads. NoTracking means EF does not keep change-tracking
// snapshots of every row it returns, which is both faster and a safety net.
builder.Services.AddDbContextFactory<ErpDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Erp"))
           .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

builder.Services
    .AddGraphQLServer()
    .RegisterDbContextFactory<ErpDbContext>()
    .AddQueryType<Query>()

    // Computed fields need their inputs projected — see Types/EntityTypes.cs.
    .AddType<CustomerType>()
    .AddType<PersonType>()
    .AddType<SalesOrderLineType>()
    .AddType<ProductType>()

    // Batches customer name lookups — see CustomerNameDataLoader.
    .AddDataLoader<CustomerNameDataLoader>()

    // The four generic abilities every list endpoint advertises.
    .AddProjections()
    .AddFiltering()
    .AddSorting()

    // No endpoint may ever return more than 100 rows in one page.
    .ModifyPagingOptions(o =>
    {
        o.MaxPageSize = 100;
        o.DefaultPageSize = 25;
        o.IncludeTotalCount = true;
    })

    // Guard rails: a deeply nested query can join its way across the whole
    // database, so cap depth and give every request a hard time limit.
    .AddMaxExecutionDepthRule(12)
    // HotChocolate costs a query BEFORE running it. The default ceiling of
    // 1000 is too low here: passing `take` as a variable is costed at the
    // maximum page size, so a perfectly ordinary paged query with two levels
    // of includes measures ~1150-1600. Measured worst case on this schema is
    // 1611, so 2000 admits real queries and still rejects anything larger.
    // The page cap, depth limit and timeout are the substantive guards.
    .ModifyCostOptions(o =>
    {
        o.MaxFieldCost = 2_000;
        o.MaxTypeCost = 2_000;
    })
    .ModifyRequestOptions(o =>
    {
        o.ExecutionTimeout = TimeSpan.FromSeconds(30);
        o.IncludeExceptionDetails = builder.Environment.IsDevelopment();
    })

    // HotChocolate resolves schema-level services (like the error filter)
    // from their OWN container, which does not inherit the app's services —
    // so logging has to be registered here for the filter to be constructed.
    .ConfigureSchemaServices(services => services.AddLogging(b => b.AddConsole()))
    .AddErrorFilter<ErpErrorFilter>();

var app = builder.Build();

// Liveness probe: proves the API is up AND can actually reach the database.
app.MapGet("/health", async (IDbContextFactory<ErpDbContext> factory) =>
{
    await using var db = await factory.CreateDbContextAsync();
    return await db.Database.CanConnectAsync()
        ? Results.Ok(new { status = "healthy", database = "connected" })
        : Results.Problem("Cannot reach the ERP database.", statusCode: 503);
});

app.MapGraphQL(); // GraphQL IDE at /graphql

app.Run();
