using ErpGpt.GraphQLApi.Data;
using ErpGpt.GraphQLApi.GraphQL;
using HotChocolate.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;

// Every date column here is `timestamp without time zone` — AdventureWorks
// dates are plain business dates, and ErpDbContext pins them that way on
// purpose. GraphQL's DateTime scalar is offset-aware, so a filter literal
// arrives as Kind=Utc and Npgsql refuses to write it to such a column:
// "Cannot write DateTime with Kind=UTC to PostgreSQL type 'timestamp without
// time zone'". That made every `where: { orderDate: ... }` fail.
//
// This switch tells Npgsql to accept any Kind for those columns and ignore
// the offset, which is the correct reading when the column has no time zone.
// Set before the first connection is built, and deliberately preferred over
// swapping the GraphQL scalar, which would change how every date is
// serialised to callers.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// Managed hosts (Render, Railway, Fly) choose the port for us and pass it in.
// Nothing sets PORT locally, so this is a no-op on a developer machine.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// The API only ever reads. NoTracking means EF does not keep change-tracking
// snapshots of every row it returns, which is both faster and a safety net.
builder.Services.AddDbContextFactory<ErpDbContext>(options =>
    options.UseNpgsql(ResolveConnectionString(builder.Configuration))
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

app.MapGraphQL() // GraphQL IDE at /graphql
   // The IDE is how the team exercises the API, so it stays on when deployed —
   // it is off by default outside Development.
   .WithOptions(o => o.Tool.Enable = true);

app.Run();

// Managed Postgres is handed to the app as a single URL, and the host offers no
// way to template it into appsettings. Npgsql only speaks key=value, so the URL
// is translated here. Local development still reads ConnectionStrings:Erp.
static string ResolveConnectionString(IConfiguration configuration)
{
    var url = Environment.GetEnvironmentVariable("DATABASE_URL");

    if (string.IsNullOrWhiteSpace(url))
        return configuration.GetConnectionString("Erp")
            ?? throw new InvalidOperationException(
                "No database configured. Set DATABASE_URL or ConnectionStrings:Erp.");

    var uri = new Uri(url);
    var credentials = uri.UserInfo.Split(':', 2);

    return new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.Port > 0 ? uri.Port : 5432,
        Database = uri.AbsolutePath.TrimStart('/'),
        Username = Uri.UnescapeDataString(credentials[0]),
        Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : string.Empty,

        // Hosted Postgres presents a certificate from its own CA, which the
        // container has no reason to trust. Prefer encrypts when the server
        // offers it without demanding a verifiable chain, and still leaves a
        // plain local socket working — one setting covers both environments.
        SslMode = SslMode.Prefer,
    }.ConnectionString;
}
