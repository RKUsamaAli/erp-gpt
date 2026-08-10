# Database

**Owners: Hammad, Usama, Nazim**

The only place real business data lives. Nothing else in this repo may
contain business data.

## Naming convention — decided, not negotiable mid-build

- **Tables:** PascalCase, singular — `Customer`, `OrderLine`, `StockItem`
- **Columns:** PascalCase — `TotalRevenue`, `CreatedAt`
- **Primary keys:** `Id` (int identity)
- **Foreign keys:** `<Table>Id` — `CustomerId`, `SupplierId`
- **Every FK explicitly declared** as a constraint, not implied by a column
  name. GraphQL resolvers lean on these for nested queries.

> If the team prefers snake_case, fine — change this file **before** the
> first migration, then never again. Mixed conventions make the GraphQL
> schema ugly and the KB descriptions inconsistent, which hurts retrieval.

## Core entities (v1)

Customer, Supplier, Product, Order, OrderLine, Invoice, StockItem,
StockMovement. Regions/categories as lookup tables.

## Indexes

Index every column the API will filter or group by — date columns
(`OrderDate`, `InvoiceDate`) and all FKs, minimum.

## Seed data rules (`seed/`)

Seed data quality decides whether aggregations can be tested and whether the
KB team can write believable example questions. Requirements:

- Realistic-looking names — not `Customer1`, `Customer2`
- Dates spread across **two full years** (period comparisons need history)
- Some cancelled orders (aggregations must exclude them correctly)
- Some nulls where the schema allows them
- A few deliberate edge cases: a customer with zero orders, an order with
  one line, a product never sold
- Enough volume for GROUP BY results to be interesting: ~50 customers,
  ~20 products, ~2,000 orders

## Deliverable

An ER diagram generated into `docs/` (SchemaSpy, DBeaver export, or
similar). This unblocks step 2 and gives the KB team the vocabulary of the
domain.
