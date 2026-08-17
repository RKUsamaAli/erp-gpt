# Frontend integration guide

Everything a frontend needs to call this API: the one endpoint, what you send,
what comes back, and which parts are the same everywhere versus which change
per call.

Read this first, then use [`API.md`](API.md) when you want the full recorded
response body for a specific endpoint.

- **Read-only.** There are no mutations. Nothing you send can change data.
- **One URL.** Every call is a `POST` to the same address.
- **16 endpoints**, but only 3 shapes to learn — see [Generic vs
  dynamic](#generic-vs-dynamic).

---

## 1. Where to send it

| Environment | URL |
|---|---|
| Deployed | `https://erpgpt-api.onrender.com/graphql` |
| Local | `http://localhost:5000/graphql` |
| Health check | `…/health` → `{"status":"healthy","database":"connected"}` |

Confirm the deployed hostname on the Render dashboard — Render appends a suffix
if the service name was taken.

> **First request after idle takes ~1 minute.** The deployed API is on a free
> plan and sleeps after 15 minutes without traffic. Don't set a short fetch
> timeout, and consider a loading state that tolerates it. Subsequent requests
> are fast.

CORS is open (`Access-Control-Allow-Origin: *`), so you can call it from any
origin, including `localhost:3000`, with no proxy.

---

## 2. The request envelope — **generic, identical for all 16 endpoints**

Always the same: `POST`, one header, a JSON body with two keys.

```http
POST /graphql
Content-Type: application/json
```

```json
{
  "query": "query TopCustomers($from: LocalDate!, $to: LocalDate!, $limit: Int!) { topCustomers(from: $from, to: $to, limit: $limit) { customerId customerName revenue } }",
  "variables": { "from": "2024-01-01", "to": "2024-12-31", "limit": 5 }
}
```

| Key | Required | What it is |
|---|---|---|
| `query` | yes | The GraphQL document, as a string. **Static** — write it once in your code. |
| `variables` | only if the query declares them | The values. **This is the dynamic part** — everything from user input goes here. |

**Put user input in `variables`, never in the query string.** Concatenating
values into `query` breaks on quotes and dates and makes every call a
different string. The query is a constant; `variables` is the payload.

---

## 3. The response envelope — **generic**

Success. `data` mirrors the shape you asked for, key for key:

```json
{ "data": { "topCustomers": [ { "customerId": 29641, "customerName": "Westside Plaza", "revenue": 475912.5591 } ] } }
```

Failure — `errors` is present:

```json
{ "errors": [ { "message": "…", "path": ["salesSummary"], "extensions": { "code": "INVALID_DATE_RANGE" } } ], "data": null }
```

Both keys can appear together: a partial failure returns `data` with the failed
field set to `null`. **Always check `errors` first** — do not assume `data` is
usable just because the HTTP status was 200.

---

## 4. Generic vs dynamic

This is the part worth internalising. The 16 endpoints fall into **three
groups**. Within a group the call shape is identical, and only the entity and
its fields change.

| Group | Count | Arguments | Returns |
|---|---|---|---|
| **Browse** | 7 | `skip`, `take`, `where`, `order` — **all generic** | A paged collection — **generic wrapper**, dynamic `items` |
| **Detail** | 3 | `id: Int!` — **generic** | One object, or `null` |
| **Aggregation** | 6 | `from`, `to` (+ `limit` or `interval`) — **generic** | A fixed result shape — **dynamic per endpoint** |

So: **argument shapes are generic; the selected fields and returned rows are
dynamic.** If you build one browse component, it works for all seven browse
endpoints by swapping the endpoint name and the field list.

---

## 5. Browse — 7 endpoints

`customers` · `orders` · `orderLines` · `products` · `productCategories` ·
`territories` · `salesPeople`

### Arguments — generic, all four optional

| Argument | Type | Default | Notes |
|---|---|---|---|
| `skip` | `Int` | `0` | Rows to skip. Page N (0-based) = `skip: N * take`. |
| `take` | `Int` | `25` | Rows to return. **Hard maximum 100** — asking for more is an error, not a silent clamp. |
| `where` | `<Entity>FilterInput` | none | Filtering. See [§8](#8-filtering--generic-grammar-dynamic-fields). |
| `order` | `[<Entity>SortInput!]` | none | Sorting. See [§9](#9-sorting--generic). |

### Response — generic wrapper, dynamic `items`

```graphql
{
  totalCount                                  # Int!  — total matching rows, ignores paging
  pageInfo { hasNextPage hasPreviousPage }    # Boolean!
  items { … }                                 # the dynamic part: the entity's fields
}
```

`totalCount` is the count of everything matching `where`, not the page size —
use it for the pager. Request only the sub-fields you need; unrequested columns
are never read from the database.

### Real example

Request:

```json
{
  "query": "query Customers($take: Int!, $region: String!) { customers(skip: 0, take: $take, where: { territory: { name: { eq: $region } } }, order: [{ customerId: ASC }]) { totalCount pageInfo { hasNextPage } items { customerId displayName isStore territory { name group } } } }",
  "variables": { "take": 2, "region": "Canada" }
}
```

Response:

```json
{
  "data": {
    "customers": {
      "totalCount": 1791,
      "pageInfo": { "hasNextPage": true },
      "items": [
        { "customerId": 10, "displayName": "Rural Cycle Emporium", "isStore": true,
          "territory": { "name": "Canada", "group": "North America" } },
        { "customerId": 11, "displayName": "Sharp Bikes", "isStore": true,
          "territory": { "name": "Canada", "group": "North America" } }
      ]
    }
  }
}
```

### The entities and their fields

Nested objects are joined in the **same** database query, so asking for
`customer { territory { name } }` costs no extra round trip.

**`customers` → Customer**

| Field | Type | |
|---|---|---|
| `customerId` | `Int!` | |
| `displayName` | `String` | Store name *or* person's full name — a customer has no name column of its own |
| `isStore` | `Boolean!` | |
| `personId` / `storeId` / `territoryId` | `Int` | |
| `person` | `Person` | `firstName` `lastName` `middleName` `fullName` `personType` `businessEntityId` |
| `store` | `Store` | |
| `territory` | `Territory` | |
| `orders` | `[SalesOrder!]!` | All of that customer's orders |

**`orders` → SalesOrder**

| Field | Type | |
|---|---|---|
| `salesOrderId` | `Int!` | |
| `orderDate` `dueDate` | `DateTime!` | |
| `shipDate` | `DateTime` | Nullable |
| `status` | `Short!` | Always `5` (Shipped) in this dataset |
| `onlineOrderFlag` | `Boolean!` | |
| `purchaseOrderNumber` | `String` | |
| `subTotal` `taxAmt` `freight` | `Decimal!` | |
| `totalDue` | `Decimal` | Stored, not computed — this is the revenue figure |
| `customer` | `Customer!` | |
| `salesPerson` | `SalesPerson` | |
| `territory` | `Territory` | |
| `shipToAddress` | `Address!` | |
| `lines` | `[SalesOrderLine!]!` | |

**`orderLines` → SalesOrderLine**

`salesOrderDetailId: Int!` · `salesOrderId: Int!` · `orderQty: Short!` ·
`unitPrice: Decimal!` · `unitPriceDiscount: Decimal!` · `lineTotal: Decimal!` ·
`carrierTrackingNumber: String` · `product: Product!` · `order: SalesOrder!`

**`products` → Product**

`productId: Int!` · `name: String!` · `productNumber: String!` ·
`listPrice: Decimal!` · `standardCost: Decimal!` · `color: String` ·
`size: String` · `weight: Decimal` · `makeFlag: Boolean!` ·
`finishedGoodsFlag: Boolean!` · `sellStartDate: DateTime!` ·
`sellEndDate: DateTime` · `discontinuedDate: DateTime` ·
`isCurrentlySold: Boolean!` · `subcategory: ProductSubcategory`

> `product.orderLines` is deliberately **not** exposed — one product can appear
> on thousands of lines. Use `orderLines(where: { productId: { eq: 782 } })`.

**`productCategories` → ProductCategory**

`productCategoryId: Int!` · `name: String!` ·
`subcategories: [ProductSubcategory!]!` (each `productSubcategoryId`, `name`,
`category`, `products`)

**`territories` → Territory**

`territoryId: Int!` · `name: String!` · `countryRegionCode: String!` ·
`group: String!` · `salesYtd: Decimal!` · `salesLastYear: Decimal!`

**`salesPeople` → SalesPerson**

`businessEntityId: Int!` · `person: Person!` · `territory: Territory` ·
`salesQuota: Decimal` · `bonus: Decimal!` · `commissionPct: Decimal!` ·
`salesYtd: Decimal!` · `salesLastYear: Decimal!`

---

## 6. Detail — 3 endpoints

`customer(id:)` · `order(id:)` · `product(id:)`

Argument is always `id: Int!`. Returns the single entity with the same fields
listed above, or `null` when nothing matches.

```json
{ "query": "query Customer($id: Int!) { customer(id: $id) { customerId displayName isStore territory { name } orders { salesOrderId totalDue } } }",
  "variables": { "id": 29641 } }
```

> **A missing id is not an error.** You get `HTTP 200` and
> `{"data":{"customer":null}}` with no `errors` key. Check for `null` yourself
> and render your own "not found" state.

---

## 7. Aggregation — 6 endpoints

Pre-computed figures. You cannot change how they are calculated — you choose an
endpoint and pass a date range. The maths runs in the database.

Every one takes `from: LocalDate!` and `to: LocalDate!` as `"YYYY-MM-DD"`
strings. `to` includes the whole of that day.

| Endpoint | Extra argument | Returns |
|---|---|---|
| `salesSummary` | — | one object |
| `topCustomers` | `limit: Int! = 10` | list |
| `topProducts` | `limit: Int! = 10` | list |
| `revenueByPeriod` | `interval: Interval! = MONTH` (`MONTH`, `QUARTER`, `YEAR`) | list |
| `salesByTerritory` | — | list |
| `salesByCategory` | — | list |

The result shape is **fixed per endpoint** — this is the dynamic part, and the
one place a generic component won't fit.

```jsonc
// salesSummary  → an object, not a list
{ "totalRevenue": 49020486.512, "orderCount": 14244,
  "averageOrderValue": 3441.48, "customerCount": 11145,
  "from": "2024-01-01", "to": "2024-12-31" }

// topCustomers  → territory is nullable
{ "customerId": 29641, "customerName": "Westside Plaza", "territory": "Southwest",
  "revenue": 475912.5591, "orderCount": 3 }

// topProducts   → category is nullable
{ "productId": 782, "productName": "Mountain-200 Black, 38",
  "productNumber": "BK-M68B-38", "category": "Bikes",
  "revenue": 2217564.762652, "unitsSold": 1472 }

// revenueByPeriod → `label` is display-ready ("2024-Q1", "2024-03", "2024")
{ "year": 2024, "period": 1, "label": "2024-Q1",
  "revenue": 8795430.0691, "orderCount": 1181 }

// salesByTerritory  → already sorted by revenue, highest first
{ "territoryId": 4, "territory": "Southwest", "group": "North America",
  "revenue": 10245166.7713, "orderCount": 2733 }

// salesByCategory
{ "categoryId": 1, "category": "Bikes", "revenue": 36313572.481212, "unitsSold": 37776 }
```

---

## 8. Filtering — generic grammar, dynamic fields

The **grammar is the same for every entity**; only the field names change. A
`where` is an object of `field: { operator: value }`.

```graphql
where: { listPrice: { gte: 500 } }
```

### Operators by field type — generic

| Field type | Operators |
|---|---|
| `Int` `Decimal` `Short` `DateTime` | `eq` `neq` `in` `nin` `gt` `gte` `lt` `lte` — each with an `n`-prefixed negation (`ngt`, `ngte`, `nlt`, `nlte`) |
| `String` | `eq` `neq` `contains` `startsWith` `endsWith` `in` `nin` — plus `ncontains`, `nstartsWith`, `nendsWith` |
| `Boolean` | `eq` `neq` |

### Combining — generic

```graphql
where: { and: [ { listPrice: { gte: 500 } }, { listPrice: { lte: 1500 } } ] }
where: { or:  [ { color: { eq: "Black" } }, { color: { eq: "Red" } } ] }
```

### Reaching through relationships — generic

Nest the relationship name; it becomes a join.

```graphql
# customers in Canada
where: { territory: { name: { eq: "Canada" } } }

# bikes between $500 and $1500
where: { and: [
  { listPrice: { gte: 500 } },
  { listPrice: { lte: 1500 } },
  { subcategory: { category: { name: { eq: "Bikes" } } } }
] }
```

### Dates in filters — the one trap

Date filters need the **full ISO form**. `"2024-01-01"` alone is rejected by
the `DateTime` scalar with a coercion error.

```graphql
where: { orderDate: { gte: "2024-01-01T00:00:00Z" } }        # correct
where: { orderDate: { gte: "2024-01-01" } }                  # error
```

The `Z` is accepted and then ignored — these columns carry no time zone.

Note the asymmetry: **aggregation arguments** use `LocalDate` (`"2024-01-01"`),
**filters** use `DateTime` (`"2024-01-01T00:00:00Z"`). Different scalars.

---

## 9. Sorting — generic

A list, so you can sort by several keys. Direction is `ASC` or `DESC`.

```graphql
order: [{ totalDue: DESC }]
order: [{ territoryId: ASC }, { totalDue: DESC }]   # multiple keys
order: [{ territory: { name: ASC } }]               # through a relationship
```

Sorting is subject to the same restriction as filtering — see the next
section.

---

## 10. Computed fields — selectable, but **not** filterable or sortable

Five fields are calculated rather than stored. You can **select** them
normally, but putting one in `where` or `order` fails with `QUERY_FAILED`,
because there is no column for the database to filter or sort on.

This catches people out: a data table that sorts by clicking a column header
will break on exactly these five.

| Computed field | On | Filter/sort on this instead |
|---|---|---|
| `displayName` | Customer | `store: { name: { contains: "Bike" } }` or `person: { lastName: { eq: "Smith" } }` |
| `isStore` | Customer | `storeId: { neq: null }` for stores, `personId: { eq: null }` likewise |
| `isCurrentlySold` | Product | `sellEndDate: { eq: null }` |
| `lineTotal` | SalesOrderLine | `unitPrice` — the nearest stored column |
| `fullName` | Person | `lastName` / `firstName` |

```graphql
# fails — QUERY_FAILED
customers(where: { isStore: { eq: true } })
orderLines(order: [{ lineTotal: DESC }])

# works
customers(where: { storeId: { neq: null } })      # 1336 rows
orderLines(order: [{ unitPrice: DESC }])
```

Everything else — every stored column, including through a relationship —
filters and sorts fine. Real `Boolean` columns such as `makeFlag` and
`onlineOrderFlag` are stored, so they are not affected.

---

## 11. Errors — how to branch

Two kinds, and they need different handling.

### HTTP 400 — the query itself is wrong

Unknown field, syntax error, missing variable. `data` is absent. **This is a
bug in your code, not something the user did** — it will fail identically on
every request. Fix the query.

```json
{ "errors": [ { "message": "The field `nope` does not exist on the type `Customer`.",
                "locations": [ { "line": 1, "column": 30 } ] } ] }
```

### HTTP 200 with `errors` — the query was valid but failed

Show these to the user. `extensions.code` is the stable identifier — branch on
it, never on `message` text.

| `code` | Meaning | What to do |
|---|---|---|
| `INVALID_DATE_RANGE` | `from` is after `to` | Message is user-ready: *"'from' (2025-01-01) is after 'to' (2024-01-01). Swap the two dates."* |
| `HC0051` | `take` exceeded 100 | Cap your page size at 100 |
| `QUERY_FAILED` | Query could not be completed | Log it; check field names and argument types |

```json
{ "errors": [ { "message": "The maximum allowed items per page were exceeded.",
                "path": ["orders"],
                "extensions": { "code": "HC0051", "requestedItems": 500, "maxAllowedItems": 100 } } ],
  "data": { "orders": null } }
```

---

## 12. A client to copy

```ts
const ENDPOINT = "https://erpgpt-api.onrender.com/graphql";

export class GraphQLError extends Error {
  constructor(message: string, readonly code?: string, readonly errors?: unknown[]) {
    super(message);
  }
}

export async function gql<T>(
  query: string,
  variables?: Record<string, unknown>,
): Promise<T> {
  const res = await fetch(ENDPOINT, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ query, variables }),
  });

  const json = await res.json();

  // Check errors before data: a 200 can still carry a failure.
  if (json.errors?.length) {
    const first = json.errors[0];
    throw new GraphQLError(first.message, first.extensions?.code, json.errors);
  }
  if (!res.ok) throw new GraphQLError(`HTTP ${res.status}`);

  return json.data as T;
}
```

The query is a module-level constant; only `variables` changes per call:

```ts
const TOP_CUSTOMERS = `
  query TopCustomers($from: LocalDate!, $to: LocalDate!, $limit: Int!) {
    topCustomers(from: $from, to: $to, limit: $limit) {
      customerId customerName territory revenue orderCount
    }
  }`;

type TopCustomers = {
  topCustomers: {
    customerId: number; customerName: string;
    territory: string | null;      // nullable
    revenue: number; orderCount: number;
  }[];
};

const { topCustomers } = await gql<TopCustomers>(TOP_CUSTOMERS, {
  from: "2024-01-01", to: "2024-12-31", limit: 5,
});
```

One paged fetch that works for **any** browse endpoint, because the arguments
and wrapper are generic:

```ts
const page = async (endpoint: string, fields: string, page = 0, size = 25, where?: object) =>
  gql<any>(
    `query P($skip: Int!, $take: Int!, $where: ${endpoint === "orders" ? "SalesOrder" : "Customer"}FilterInput) {
       ${endpoint}(skip: $skip, take: $take, where: $where) {
         totalCount pageInfo { hasNextPage } items { ${fields} }
       }
     }`,
    { skip: page * size, take: Math.min(size, 100), where },
  );
```

---

## 13. Gotchas

**Dates only exist between 2022-05-30 and 2025-06-29.** Anything outside that
returns an empty list with no error — it looks like a broken screen. Default
your date pickers inside this range.

**`Decimal` arrives as a JSON number**, not a string — e.g.
`36313572.481212`. Fine for display, but format for the user rather than
printing raw: `new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(v)`.

**`take` over 100 is an error, not a clamp.** Cap it before sending.

**Nullable fields to guard:** `displayName`, `territory` (on Customer,
`CustomerRevenue`), `category` (on `ProductSales`), `shipDate`, `totalDue`,
`salesQuota`, `color`, `size`, `weight`, `subcategory`.

**Query depth is capped at 12 and cost at 2000.** Deeply nested queries are
rejected before running. In practice only a problem if you nest
order → customer → orders → lines → product.

**Every order has `status: 5`.** There are no cancelled or pending orders here,
so a status filter or breakdown has nothing to show.

**Explore the schema interactively** at `/graphql` in a browser — it lists
every operation, argument and field with descriptions. Faster than reading this
file for a one-off question.
