using ErpGpt.Api.Data;
using ErpGpt.Api.GraphQL;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContextFactory<ErpDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Erp")));

builder.Services
    .AddGraphQLServer()
    .RegisterDbContextFactory<ErpDbContext>()
    .AddQueryType<Query>()
    .AddProjections()
    .AddFiltering()
    .AddSorting()
    .ModifyRequestOptions(o =>
        // Readable errors in dev; the agent's retry loop depends on them.
        o.IncludeExceptionDetails = builder.Environment.IsDevelopment());

var app = builder.Build();

// Dev convenience: create schema + seed. Swap for `dotnet ef` migrations
// once the schema settles (db/migrations/ is where they go).
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ErpDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.EnsureCreated();
    Seeder.Seed(db);
}

app.MapGraphQL(); // Banana Cake Pop IDE at /graphql

app.Run();
