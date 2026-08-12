# API reference — request payloads

Every payload on this page was executed against the running API and the
responses below are the real output, trimmed for length.

**Endpoint:** `POST http://localhost:5000/graphql`  ·  **Header:** `Content-Type: application/json`

A GraphQL request body is always the same shape:

```json
{ "query": "...", "variables": { } }
```

`variables` is optional — you can inline values in the query instead, but
variables keep the query reusable and avoid quoting mistakes.

---

## Types and conventions

Getting these wrong is the most common cause of a rejected request.

| Thing | Value | Notes |
|---|---|---|
| Date arguments | `LocalDate` | Aggregations take `"2024-01-01"`. The GraphQL type is **`LocalDate`**, not `Date` — declare variables as `$from: LocalDate!`. |
| Timestamp fields | `DateTime` | `orderDate` and friends need a full ISO value with offset when filtered: `"2024-01-01T00:00:00Z"`. |
| Money / quantities | `Decimal` | Declare as `$minTotal: Decimal!`. |
| Page size | `take`, max **100** | Default 25. Asking for more is refused, not truncated. |
| Offset | `skip` | Together with `totalCount` this drives page numbers. |
| Sorting | `order: [{ field: ASC }]` | `ASC` or `DESC`; multiple keys allowed. |
| Filtering | `where: { ... }` | `eq` `neq` `gt` `gte` `lt` `lte` `in` `nin` `contains` `startsWith` `endsWith`, plus `and` / `or`. Reaches through relationships. |

### Query cost

Every request is costed before it runs; the ceiling is **2000**. Two
things are worth knowing:

- Passing `take` as a **variable** is costed at the maximum page size
  (100), not the value you send. The same query costs ~76 with
  `take: 3` inline and ~1143 with `$take`. Both are allowed here, but
  inline numbers leave far more headroom for nesting.
- Exceeding it returns HTTP **400** with code `HC0047` before any
  database work happens.

### Two kinds of error

- **Validation** errors (unknown field, cost, depth, bad variable type)
  return HTTP **400** and the query never runs.
- **Execution** errors (`INVALID_DATE_RANGE`, page cap) return HTTP
  **200** with an `errors` array — standard GraphQL behaviour.

---

## Contents

**Browse** — [`customers`](#customers), [`orders`](#orders), [`orderLines`](#orderLines), [`products`](#products), [`productCategories`](#productCategories), [`territories`](#territories), [`salesPeople`](#salesPeople)

**Detail** — [`customer(id)`](#customerid), [`order(id)`](#orderid), [`product(id)`](#productid)

**Aggregation** — [`salesSummary`](#salesSummary), [`topCustomers`](#topCustomers), [`topProducts`](#topProducts), [`revenueByPeriod`](#revenueByPeriod), [`salesByTerritory`](#salesByTerritory), [`salesByCategory`](#salesByCategory)

**Errors** — [`page cap exceeded`](#page-cap-exceeded), [`invalid date range`](#invalid-date-range)

---

## Browse

### customers

Page + filter + sort + column selection. `displayName` resolves the store or person name.

**Request payload**

```json
{
  "query": "query Customers($take: Int!, $region: String!) { customers(skip: 0, take: $take, where: { territory: { name: { eq: $region } } }, order: [{ customerId: ASC }]) { totalCount items { customerId displayName isStore territory { name group } } } }",
  "variables": {
    "take": 3,
    "region": "Canada"
  }
}
```

**curl**

```bash
curl -s -X POST http://localhost:5000/graphql \
  -H 'Content-Type: application/json' \
  -d '{ "query": "query Customers($take: Int!, $region: String!) { customers(skip: 0, take: $take, where: { territory: { name: { eq: $region } } }, order: [{ customerId: ASC }]) { totalCount items { customerId displayName isStore territory { name group } } } }", "variables": { "take": 3, "region": "Canada" } }'
```

**Response**

```json
{
  "data": {
    "customers": {
      "totalCount": 1791,
      "items": [
        {
          "customerId": 10,
          "displayName": "Rural Cycle Emporium",
          "isStore": true,
          "territory": {
            "name": "Canada",
            "group": "North America"
          }
        },
        {
          "customerId": 11,
          "displayName": "Sharp Bikes",
          "isStore": true,
          "territory": {
            "name": "Canada",
            "group": "North America"
          }
        },
        {
          "customerId": 12,
          "displayName": "Bikes and Motorbikes",
          "isStore": true,
          "territory": {
            "name": "Canada",
            "group": "North America"
          }
        }
      ]
    }
  }
}
```

### orders

Every ability at once: page, WHERE, sort, chosen columns, and two levels of includes.

**Request payload**

```json
{
  "query": "query Orders($minTotal: Decimal!) { orders(skip: 0, take: 2, where: { totalDue: { gt: $minTotal } }, order: [{ totalDue: DESC }]) { totalCount items { salesOrderId orderDate totalDue customer { displayName territory { name } } lines { orderQty unitPrice lineTotal product { name productNumber } } } } }",
  "variables": {
    "minTotal": 10000
  }
}
```

**curl**

```bash
curl -s -X POST http://localhost:5000/graphql \
  -H 'Content-Type: application/json' \
  -d '{ "query": "query Orders($minTotal: Decimal!) { orders(skip: 0, take: 2, where: { totalDue: { gt: $minTotal } }, order: [{ totalDue: DESC }]) { totalCount items { salesOrderId orderDate totalDue customer { displayName territory { name } } lines { orderQty unitPrice lineTotal product { name productNumber } } } } }", "variables": { "minTotal": 10000 } }'
```

**Response**

```json
{
  "data": {
    "orders": {
      "totalCount": 1878,
      "items": [
        {
          "salesOrderId": 51131,
          "orderDate": "2024-05-29T00:00:00Z",
          "totalDue": 187487.825,
          "customer": {
            "displayName": "Westside Plaza",
            "territory": {
              "name": "Southwest"
            }
          },
          "lines": [
            {
              "orderQty": 11,
              "unitPrice": 31.3142,
              "lineTotal": 337.567076,
              "product": {
                "name": "Short-Sleeve Classic Jersey, L",
                "productNumber": "SJ-0194-L"
              }
            },
            {
              "orderQty": 12,
              "unitPrice": 31.3142,
              "lineTotal": 368.254992,
              "product": {
                "name": "Short-Sleeve Classic Jersey, S",
                "productNumber": "SJ-0194
  ... (truncated)
```

### orderLines

Order lines on their own. This is how you get every line for a product (the `product.orderLines` navigation is deliberately not exposed).

**Request payload**

```json
{
  "query": "query OrderLines($productId: Int!) { orderLines(take: 3, where: { productId: { eq: $productId } }, order: [{ salesOrderId: ASC }]) { totalCount items { salesOrderId salesOrderDetailId orderQty unitPrice unitPriceDiscount lineTotal } } }",
  "variables": {
    "productId": 782
  }
}
```

**curl**

```bash
curl -s -X POST http://localhost:5000/graphql \
  -H 'Content-Type: application/json' \
  -d '{ "query": "query OrderLines($productId: Int!) { orderLines(take: 3, where: { productId: { eq: $productId } }, order: [{ salesOrderId: ASC }]) { totalCount items { salesOrderId salesOrderDetailId orderQty unitPrice unitPriceDiscount lineTotal } } }", "variables": { "productId": 782 } }'
```

**Response**

```json
{
  "data": {
    "orderLines": {
      "totalCount": 1252,
      "items": [
        {
          "salesOrderId": 46604,
          "salesOrderDetailId": 10669,
          "orderQty": 4,
          "unitPrice": 1229.4589,
          "unitPriceDiscount": 0.0,
          "lineTotal": 4917.8356
        },
        {
          "salesOrderId": 46610,
          "salesOrderDetailId": 10833,
          "orderQty": 1,
          "unitPrice": 1229.4589,
          "unitPriceDiscount": 0.0,
          "lineTotal": 1229.4589
        },
        {
          "salesOrderId": 46611,
          "salesOrderDetailId": 10865,
          "orderQty": 10,
          "unitPrice": 1229.4589,
          "unitPriceDiscount": 0.0,
          "lineTotal": 12294.589
        }
      ]
    }
  }
}
```

### products

Compound WHERE using `and`, reaching two relationships deep into category.

**Request payload**

```json
{
  "query": "query Products { products(take: 3, where: { and: [{ listPrice: { gte: 500 } }, { listPrice: { lte: 1500 } }, { subcategory: { category: { name: { eq: \"Bikes\" } } } }] }, order: [{ listPrice: ASC }]) { totalCount items { productId name productNumber color listPrice standardCost isCurrentlySold subcategory { name category { name } } } } }"
}
```

**curl**

```bash
curl -s -X POST http://localhost:5000/graphql \
  -H 'Content-Type: application/json' \
  -d '{ "query": "query Products { products(take: 3, where: { and: [{ listPrice: { gte: 500 } }, { listPrice: { lte: 1500 } }, { subcategory: { category: { name: { eq: \"Bikes\" } } } }] }, order: [{ listPrice: ASC }]) { totalCount items { productId name productNumber color listPrice standardCost isCurrentlySold subcategory { name category { name } } } } }" }'
```

**Response**

```json
{
  "data": {
    "products": {
      "totalCount": 58,
      "items": [
        {
          "productId": 989,
          "name": "Mountain-500 Black, 40",
          "productNumber": "BK-M18B-40",
          "color": "Black",
          "listPrice": 539.99,
          "standardCost": 294.5797,
          "isCurrentlySold": true,
          "subcategory": {
            "name": "Mountain Bikes",
            "category": {
              "name": "Bikes"
            }
          }
        },
        {
          "productId": 990,
          "name": "Mountain-500 Black, 42",
          "productNumber": "BK-M18B-42",
          "color": "Black",
          "listPrice": 539.99,
          "standardCost": 294.5797,
          "isCurrentlySold": true,
          "subcategory": {
            "name": "Mountain Bikes",
            "category": {
              "name": "Bikes"
            }
          }
        },
  ... (truncated)
```

### productCategories

The four categories with their subcategories — useful for building filter dropdowns.

**Request payload**

```json
{
  "query": "query Categories { productCategories { totalCount items { productCategoryId name subcategories { productSubcategoryId name } } } }"
}
```

**curl**

```bash
curl -s -X POST http://localhost:5000/graphql \
  -H 'Content-Type: application/json' \
  -d '{ "query": "query Categories { productCategories { totalCount items { productCategoryId name subcategories { productSubcategoryId name } } } }" }'
```

**Response**

```json
{
  "data": {
    "productCategories": {
      "totalCount": 4,
      "items": [
        {
          "productCategoryId": 1,
          "name": "Bikes",
          "subcategories": [
            {
              "productSubcategoryId": 1,
              "name": "Mountain Bikes"
            },
            {
              "productSubcategoryId": 2,
              "name": "Road Bikes"
            },
            {
              "productSubcategoryId": 3,
              "name": "Touring Bikes"
            }
          ]
        },
        {
          "productCategoryId": 2,
          "name": "Components",
          "subcategories": [
            {
              "productSubcategoryId": 4,
              "name": "Handlebars"
            },
            {
              "productSubcategoryId": 5,
              "name": "Bottom Brackets"
            },
            {
              "productSubcategoryId": 6,
  ... (truncated)
```

### territories

The ten sales regions.

**Request payload**

```json
{
  "query": "query Territories { territories(order: [{ name: ASC }]) { totalCount items { territoryId name countryRegionCode group salesYtd } } }"
}
```

**curl**

```bash
curl -s -X POST http://localhost:5000/graphql \
  -H 'Content-Type: application/json' \
  -d '{ "query": "query Territories { territories(order: [{ name: ASC }]) { totalCount items { territoryId name countryRegionCode group salesYtd } } }" }'
```

**Response**

```json
{
  "data": {
    "territories": {
      "totalCount": 10,
      "items": [
        {
          "territoryId": 9,
          "name": "Australia",
          "countryRegionCode": "AU",
          "group": "Pacific",
          "salesYtd": 5977814.9154
        },
        {
          "territoryId": 6,
          "name": "Canada",
          "countryRegionCode": "CA",
          "group": "North America",
          "salesYtd": 6771829.1376
        },
        {
          "territoryId": 3,
          "name": "Central",
          "countryRegionCode": "US",
          "group": "North America",
          "salesYtd": 3072175.118
        },
        {
          "territoryId": 7,
          "name": "France",
          "countryRegionCode": "FR",
          "group": "Europe",
          "salesYtd": 4772398.3078
        },
        {
          "territoryId": 8,
          "name": "Germany",
          "countryRegionCod
  ... (truncated)
```

### salesPeople

The sales team. Names come through the `person` relationship.

**Request payload**

```json
{
  "query": "query SalesPeople { salesPeople(take: 3, order: [{ businessEntityId: ASC }]) { totalCount items { businessEntityId person { fullName } territory { name } salesQuota commissionPct } } }"
}
```

**curl**

```bash
curl -s -X POST http://localhost:5000/graphql \
  -H 'Content-Type: application/json' \
  -d '{ "query": "query SalesPeople { salesPeople(take: 3, order: [{ businessEntityId: ASC }]) { totalCount items { businessEntityId person { fullName } territory { name } salesQuota commissionPct } } }" }'
```

**Response**

```json
{
  "data": {
    "salesPeople": {
      "totalCount": 17,
      "items": [
        {
          "businessEntityId": 274,
          "person": {
            "fullName": "Stephen Jiang"
          },
          "territory": null,
          "salesQuota": null,
          "commissionPct": 0.0
        },
        {
          "businessEntityId": 275,
          "person": {
            "fullName": "Michael Blythe"
          },
          "territory": {
            "name": "Northeast"
          },
          "salesQuota": 300000.0,
          "commissionPct": 0.012
        },
        {
          "businessEntityId": 276,
          "person": {
            "fullName": "Linda Mitchell"
          },
          "territory": {
            "name": "Southwest"
          },
          "salesQuota": 250000.0,
          "commissionPct": 0.015
        }
      ]
    }
  }
}
```

---

## Detail

### customer(id)

One customer with their full order history.

**Request payload**

```json
{
  "query": "query Customer($id: Int!) { customer(id: $id) { customerId displayName isStore territory { name group } orders { salesOrderId orderDate totalDue } } }",
  "variables": {
    "id": 29641
  }
}
```

**curl**

```bash
curl -s -X POST http://localhost:5000/graphql \
  -H 'Content-Type: application/json' \
  -d '{ "query": "query Customer($id: Int!) { customer(id: $id) { customerId displayName isStore territory { name group } orders { salesOrderId orderDate totalDue } } }", "variables": { "id": 29641 } }'
```

**Response**

```json
{
  "data": {
    "customer": {
      "customerId": 29641,
      "displayName": "Westside Plaza",
      "isStore": true,
      "territory": {
        "name": "Southwest",
        "group": "North America"
      },
      "orders": [
        {
          "salesOrderId": 51131,
          "orderDate": "2024-05-29T00:00:00Z",
          "totalDue": 187487.825
        },
        {
          "salesOrderId": 55282,
          "orderDate": "2024-08-29T00:00:00Z",
          "totalDue": 182018.6272
        },
        {
          "salesOrderId": 61184,
          "orderDate": "2024-11-29T00:00:00Z",
          "totalDue": 106406.1069
        },
        {
          "salesOrderId": 67305,
          "orderDate": "2025-02-28T00:00:00Z",
          "totalDue": 130907.0496
        }
      ]
    }
  }
}
```

### order(id)

One order with every line and the product on it.

**Request payload**

```json
{
  "query": "query Order($id: Int!) { order(id: $id) { salesOrderId orderDate status onlineOrderFlag subTotal taxAmt freight totalDue customer { displayName } lines { orderQty unitPrice lineTotal product { name productNumber } } } }",
  "variables": {
    "id": 51131
  }
}
```

**curl**

```bash
curl -s -X POST http://localhost:5000/graphql \
  -H 'Content-Type: application/json' \
  -d '{ "query": "query Order($id: Int!) { order(id: $id) { salesOrderId orderDate status onlineOrderFlag subTotal taxAmt freight totalDue customer { displayName } lines { orderQty unitPrice lineTotal product { name productNumber } } } }", "variables": { "id": 51131 } }'
```

**Response**

```json
{
  "data": {
    "order": {
      "salesOrderId": 51131,
      "orderDate": "2024-05-29T00:00:00Z",
      "status": 5,
      "onlineOrderFlag": false,
      "subTotal": 163930.3943,
      "taxAmt": 17948.5186,
      "freight": 5608.9121,
      "totalDue": 187487.825,
      "customer": {
        "displayName": "Westside Plaza"
      },
      "lines": [
        {
          "orderQty": 11,
          "unitPrice": 31.3142,
          "lineTotal": 337.567076,
          "product": {
            "name": "Short-Sleeve Classic Jersey, L",
            "productNumber": "SJ-0194-L"
          }
        },
        {
          "orderQty": 12,
          "unitPrice": 31.3142,
          "lineTotal": 368.254992,
          "product": {
            "name": "Short-Sleeve Classic Jersey, S",
            "productNumber": "SJ-0194-S"
          }
        },
        {
          "orderQty": 9,
          "unitPrice":
  ... (truncated)
```

### product(id)

One product with pricing and category.

**Request payload**

```json
{
  "query": "query Product($id: Int!) { product(id: $id) { productId name productNumber color size listPrice standardCost isCurrentlySold subcategory { name category { name } } } }",
  "variables": {
    "id": 782
  }
}
```

**curl**

```bash
curl -s -X POST http://localhost:5000/graphql \
  -H 'Content-Type: application/json' \
  -d '{ "query": "query Product($id: Int!) { product(id: $id) { productId name productNumber color size listPrice standardCost isCurrentlySold subcategory { name category { name } } } }", "variables": { "id": 782 } }'
```

**Response**

```json
{
  "data": {
    "product": {
      "productId": 782,
      "name": "Mountain-200 Black, 38",
      "productNumber": "BK-M68B-38",
      "color": "Black",
      "size": "38",
      "listPrice": 2294.99,
      "standardCost": 1251.9813,
      "isCurrentlySold": true,
      "subcategory": {
        "name": "Mountain Bikes",
        "category": {
          "name": "Bikes"
        }
      }
    }
  }
}
```

---

## Aggregation

### salesSummary

The headline numbers. This is the 'how much did we sell' endpoint.

**Request payload**

```json
{
  "query": "query Summary($from: LocalDate!, $to: LocalDate!) { salesSummary(from: $from, to: $to) { totalRevenue orderCount averageOrderValue customerCount from to } }",
  "variables": {
    "from": "2024-01-01",
    "to": "2024-12-31"
  }
}
```

**curl**

```bash
curl -s -X POST http://localhost:5000/graphql \
  -H 'Content-Type: application/json' \
  -d '{ "query": "query Summary($from: LocalDate!, $to: LocalDate!) { salesSummary(from: $from, to: $to) { totalRevenue orderCount averageOrderValue customerCount from to } }", "variables": { "from": "2024-01-01", "to": "2024-12-31" } }'
```

**Response**

```json
{
  "data": {
    "salesSummary": {
      "totalRevenue": 49020486.512,
      "orderCount": 14244,
      "averageOrderValue": 3441.48,
      "customerCount": 11145,
      "from": "2024-01-01",
      "to": "2024-12-31"
    }
  }
}
```

### topCustomers

Biggest customers by revenue. `limit` is clamped to 100.

**Request payload**

```json
{
  "query": "query TopCustomers($from: LocalDate!, $to: LocalDate!, $limit: Int!) { topCustomers(from: $from, to: $to, limit: $limit) { customerId customerName territory revenue orderCount } }",
  "variables": {
    "from": "2024-01-01",
    "to": "2024-12-31",
    "limit": 3
  }
}
```

**curl**

```bash
curl -s -X POST http://localhost:5000/graphql \
  -H 'Content-Type: application/json' \
  -d '{ "query": "query TopCustomers($from: LocalDate!, $to: LocalDate!, $limit: Int!) { topCustomers(from: $from, to: $to, limit: $limit) { customerId customerName territory revenue orderCount } }", "variables": { "from": "2024-01-01", "to": "2024-12-31", "limit": 3 } }'
```

**Response**

```json
{
  "data": {
    "topCustomers": [
      {
        "customerId": 29641,
        "customerName": "Westside Plaza",
        "territory": "Southwest",
        "revenue": 475912.5591,
        "orderCount": 3
      },
      {
        "customerId": 29913,
        "customerName": "Field Trip Store",
        "territory": "Central",
        "revenue": 470230.9927,
        "orderCount": 4
      },
      {
        "customerId": 29818,
        "customerName": "Brakes and Gears",
        "territory": "Northwest",
        "revenue": 431068.4796,
        "orderCount": 4
      }
    ]
  }
}
```

### topProducts

Best sellers by revenue and units, net of line discounts.

**Request payload**

```json
{
  "query": "query TopProducts($from: LocalDate!, $to: LocalDate!, $limit: Int!) { topProducts(from: $from, to: $to, limit: $limit) { productId productName productNumber category revenue unitsSold } }",
  "variables": {
    "from": "2024-01-01",
    "to": "2024-12-31",
    "limit": 3
  }
}
```

**curl**

```bash
curl -s -X POST http://localhost:5000/graphql \
  -H 'Content-Type: application/json' \
  -d '{ "query": "query TopProducts($from: LocalDate!, $to: LocalDate!, $limit: Int!) { topProducts(from: $from, to: $to, limit: $limit) { productId productName productNumber category revenue unitsSold } }", "variables": { "from": "2024-01-01", "to": "2024-12-31", "limit": 3 } }'
```

**Response**

```json
{
  "data": {
    "topProducts": [
      {
        "productId": 782,
        "productName": "Mountain-200 Black, 38",
        "productNumber": "BK-M68B-38",
        "category": "Bikes",
        "revenue": 2217564.762652,
        "unitsSold": 1472
      },
      {
        "productId": 783,
        "productName": "Mountain-200 Black, 42",
        "productNumber": "BK-M68B-42",
        "category": "Bikes",
        "revenue": 1932388.290685,
        "unitsSold": 1262
      },
      {
        "productId": 779,
        "productName": "Mountain-200 Silver, 38",
        "productNumber": "BK-M68S-38",
        "category": "Bikes",
        "revenue": 1817993.083232,
        "unitsSold": 1165
      }
    ]
  }
}
```

### revenueByPeriod

Trend over time. `interval` accepts MONTH, QUARTER or YEAR.

**Request payload**

```json
{
  "query": "query Trend($from: LocalDate!, $to: LocalDate!, $interval: Interval!) { revenueByPeriod(from: $from, to: $to, interval: $interval) { year period label revenue orderCount } }",
  "variables": {
    "from": "2024-01-01",
    "to": "2024-12-31",
    "interval": "QUARTER"
  }
}
```

**curl**

```bash
curl -s -X POST http://localhost:5000/graphql \
  -H 'Content-Type: application/json' \
  -d '{ "query": "query Trend($from: LocalDate!, $to: LocalDate!, $interval: Interval!) { revenueByPeriod(from: $from, to: $to, interval: $interval) { year period label revenue orderCount } }", "variables": { "from": "2024-01-01", "to": "2024-12-31", "interval": "QUARTER" } }'
```

**Response**

```json
{
  "data": {
    "revenueByPeriod": [
      {
        "year": 2024,
        "period": 1,
        "label": "2024-Q1",
        "revenue": 8795430.0691,
        "orderCount": 1181
      },
      {
        "year": 2024,
        "period": 2,
        "label": "2024-Q2",
        "revenue": 12220904.511,
        "orderCount": 1603
      },
      {
        "year": 2024,
        "period": 3,
        "label": "2024-Q3",
        "revenue": 14361948.2261,
        "orderCount": 5336
      },
      {
        "year": 2024,
        "period": 4,
        "label": "2024-Q4",
        "revenue": 13642203.7058,
        "orderCount": 6124
      }
    ]
  }
}
```

### salesByTerritory

Which region sells the most.

**Request payload**

```json
{
  "query": "query ByTerritory($from: LocalDate!, $to: LocalDate!) { salesByTerritory(from: $from, to: $to) { territoryId territory group revenue orderCount } }",
  "variables": {
    "from": "2024-01-01",
    "to": "2024-12-31"
  }
}
```

**curl**

```bash
curl -s -X POST http://localhost:5000/graphql \
  -H 'Content-Type: application/json' \
  -d '{ "query": "query ByTerritory($from: LocalDate!, $to: LocalDate!) { salesByTerritory(from: $from, to: $to) { territoryId territory group revenue orderCount } }", "variables": { "from": "2024-01-01", "to": "2024-12-31" } }'
```

**Response**

```json
{
  "data": {
    "salesByTerritory": [
      {
        "territoryId": 4,
        "territory": "Southwest",
        "group": "North America",
        "revenue": 10245166.7713,
        "orderCount": 2733
      },
      {
        "territoryId": 6,
        "territory": "Canada",
        "group": "North America",
        "revenue": 7012319.8015,
        "orderCount": 1892
      },
      {
        "territoryId": 1,
        "territory": "Northwest",
        "group": "North America",
        "revenue": 6763100.5847,
        "orderCount": 2061
      },
      {
        "territoryId": 9,
        "territory": "Australia",
        "group": "Pacific",
        "revenue": 4719847.8347,
        "orderCount": 3027
      },
      {
        "territoryId": 7,
        "territory": "France",
        "group": "Europe",
        "revenue": 4282110.3504,
        "orderCount": 1282
      },
      {
        "territ
  ... (truncated)
```

### salesByCategory

Revenue split across the four product categories.

**Request payload**

```json
{
  "query": "query ByCategory($from: LocalDate!, $to: LocalDate!) { salesByCategory(from: $from, to: $to) { categoryId category revenue unitsSold } }",
  "variables": {
    "from": "2024-01-01",
    "to": "2024-12-31"
  }
}
```

**curl**

```bash
curl -s -X POST http://localhost:5000/graphql \
  -H 'Content-Type: application/json' \
  -d '{ "query": "query ByCategory($from: LocalDate!, $to: LocalDate!) { salesByCategory(from: $from, to: $to) { categoryId category revenue unitsSold } }", "variables": { "from": "2024-01-01", "to": "2024-12-31" } }'
```

**Response**

```json
{
  "data": {
    "salesByCategory": [
      {
        "categoryId": 1,
        "category": "Bikes",
        "revenue": 36313572.481212,
        "unitsSold": 37776
      },
      {
        "categoryId": 2,
        "category": "Components",
        "revenue": 5612935.340965,
        "unitsSold": 24707
      },
      {
        "categoryId": 3,
        "category": "Clothing",
        "revenue": 1068708.928497,
        "unitsSold": 37208
      },
      {
        "categoryId": 4,
        "category": "Accessories",
        "revenue": 676672.750961,
        "unitsSold": 32245
      }
    ]
  }
}
```

---

## Errors

### page cap exceeded

Asking for more than 100 rows is refused rather than silently truncated.

**Request payload**

```json
{
  "query": "query TooBig { orders(take: 500) { items { salesOrderId } } }"
}
```

**curl**

```bash
curl -s -X POST http://localhost:5000/graphql \
  -H 'Content-Type: application/json' \
  -d '{ "query": "query TooBig { orders(take: 500) { items { salesOrderId } } }" }'
```

**Response**

```json
{
  "errors": [
    {
      "message": "The maximum allowed items per page were exceeded.",
      "path": [
        "orders"
      ],
      "extensions": {
        "code": "HC0051",
        "coordinate": "Query.orders",
        "requestedItems": 500,
        "maxAllowedItems": 100
      }
    }
  ],
  "data": {
    "orders": null
  }
}
```

### invalid date range

Reversed dates return a coded error saying exactly how to fix it.

**Request payload**

```json
{
  "query": "query BadRange { salesSummary(from: \"2025-01-01\", to: \"2024-01-01\") { totalRevenue } }"
}
```

**curl**

```bash
curl -s -X POST http://localhost:5000/graphql \
  -H 'Content-Type: application/json' \
  -d '{ "query": "query BadRange { salesSummary(from: \"2025-01-01\", to: \"2024-01-01\") { totalRevenue } }" }'
```

**Response**

```json
{
  "errors": [
    {
      "message": "'from' (2025-01-01) is after 'to' (2024-01-01). Swap the two dates.",
      "path": [
        "salesSummary"
      ],
      "extensions": {
        "code": "INVALID_DATE_RANGE"
      }
    }
  ],
  "data": null
}
```

---
