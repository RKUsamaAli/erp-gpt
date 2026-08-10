# GraphQL API + Metadata Generator

**Owners: Hammad, Usama, Nazim** · Stack: ASP.NET Core 8, HotChocolate 14,
EF Core 8, PostgreSQL (pgvector image)

Two projects:

| Project | Roadmap step | What it is |
|---|---|---|
| `ErpGpt.Api` | 1 | The cage — every operation the AI can invoke |
| `ErpGpt.MetadataGen` | 2 | Introspects the live model + schema → `kb/metadata/` |

## Why PostgreSQL (decision O4)

**pgvector.** The vector store for RAG (step 4) lives inside the same
Postgres instance as the ERP data — one service, one backup, one connection
string. Plus: free, identical in Docker on every machine, first-class EF
provider (Npgsql). The SQL Server case is existing licenses/DBA expertise —
confirm with the team, but everything here targets Postgres.

## Why introspection for the Metadata Generator

Three options: hand-write the metadata (drifts immediately), parse source
(fragile), or introspect the running system (cannot drift — generated from
the code that executes). We introspect: EF `IModel` for tables/keys/
relationships, the HotChocolate schema for operations/parameters/returns.

What introspection can't know — *meanings* ("Cancelled = excluded from all
revenue") — lives in `[Semantic("...")]` attributes on entities, properties
and enum members (`Domain/Entities.cs`). The generator harvests them, so
meanings change in the same PR as the code.

## Run it

```bash
# 1. database (pgvector image)
cd api && docker compose up -d

# 2. API — creates schema, seeds deterministic data (Random(42)),
#    GraphQL IDE at http://localhost:5000/graphql
dotnet run --project ErpGpt.Api

# 3. metadata → kb/metadata/{entities.json, operations.json, schema.graphql}
dotnet run --project ErpGpt.MetadataGen
```

Try in the IDE:

```graphql
{ topCustomers(from: "2026-01-01", to: "2026-08-10", limit: 5)
  { name region totalRevenue orderCount } }

{ revenueByPeriod(from: "2024-09-01", to: "2026-08-10", interval: QUARTER)
  { year period revenue orderCount } }
```

## Endpoint status

| Done (10) | TODO (same pattern) |
|---|---|
| customers, orders, products, suppliers, invoices, stockItems (paged lists) | topSuppliers |
| customerDetail, orderDetail | productDetail, stockValuation |
| topCustomers, topProducts | outstandingByAge |
| revenueByPeriod, averageOrderValue, salesByRegion | periodComparison, salesTrend, growthByCategory |

Adding one: resolver in `GraphQL/Query.cs` with a `[Semantic]` meaning →
re-run MetadataGen → add `kb/<name>.json` with example_questions → same PR.

## Non-negotiables (unchanged)

1. Aggregations computed here in tested LINQ — never composed by the model.
   Cancelled orders excluded inside `RevenueOrders()`, one place.
2. Hard caps: `MaxPageSize = 100` on lists, `Cap()` on rankings.
3. Readable errors with codes (`INVALID_DATE_RANGE` …) — the agent retry
   loop feeds them back to the model.
4. Auth attributes before anything AI-facing goes live (TODO: wire
   `[Authorize]` once identity is decided).
5. Seed is deterministic (`Random(42)`) — eval expectations reproducible on
   every machine. Dev uses `EnsureCreated`; switch to `dotnet ef` migrations
   into `db/migrations/` once the schema settles.
