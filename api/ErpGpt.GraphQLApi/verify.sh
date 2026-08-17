#!/usr/bin/env bash
# Smoke-tests all 16 endpoints against a running API.
#
#   Terminal 1:  dotnet run
#   Terminal 2:  ./verify.sh
#
# Override the address with:  API=http://localhost:5100 ./verify.sh

API="${API:-http://localhost:5000}"
pass=0; fail=0

q() {
  curl -s -X POST "$API/graphql" \
       -H 'Content-Type: application/json' \
       -d "{\"query\":\"$1\"}"
}

check() {
  local name="$1" query="$2"
  local response
  response=$(q "$query")
  if echo "$response" | grep -q '"errors"'; then
    echo "  FAIL  $name"
    echo "        $(echo "$response" | cut -c1-140)"
    fail=$((fail + 1))
  else
    echo "  ok    $name"
    pass=$((pass + 1))
  fi
}

echo
echo "Verifying $API"
echo

if ! curl -sf "$API/health" > /dev/null; then
  echo "  The API is not answering at $API"
  echo "  Start it with 'dotnet run', and check the database is up:"
  echo "     cd .. && docker compose up -d"
  exit 1
fi
echo "  ok    /health  $(curl -s "$API/health")"
echo

echo "Browse (7)"
check "customers"         "{ customers(take:2){ totalCount items { customerId displayName isStore } } }"
check "orders"            "{ orders(take:2, where:{totalDue:{gt:1000}}, order:[{totalDue:DESC}]){ totalCount items { salesOrderId totalDue } } }"
check "orderLines"        "{ orderLines(take:2){ totalCount items { salesOrderId lineTotal } } }"
check "products"          "{ products(take:2, where:{listPrice:{gt:100}}){ totalCount items { productId name listPrice } } }"
check "productCategories" "{ productCategories(take:2){ totalCount items { name subcategories { name } } } }"
check "territories"       "{ territories(take:2){ totalCount items { name group } } }"
check "salesPeople"       "{ salesPeople(take:2){ totalCount items { businessEntityId person { fullName } } } }"

echo
echo "Date filtering (1)"
# Regression guard. Date columns are 'timestamp without time zone', and the
# GraphQL DateTime scalar hands EF a Kind=Utc value, which Npgsql refuses to
# write to such a column. Without the legacy-timestamp switch in Program.cs
# every one of these fails with QUERY_FAILED.
check "orders by orderDate" "{ orders(take:2, where:{ orderDate:{ gte:\\\"2024-01-01T00:00:00Z\\\" } }, order:[{ orderDate: ASC }]){ totalCount items { salesOrderId orderDate } } }"

echo
echo "Detail (3)"
check "customer(id)"      "{ customer(id:29641){ displayName orders { salesOrderId } } }"
check "order(id)"         "{ order(id:51131){ salesOrderId lines { lineTotal } } }"
check "product(id)"       "{ product(id:782){ name subcategory { category { name } } } }"

echo
echo "Aggregations (6)"
check "salesSummary"      "{ salesSummary(from:\\\"2024-01-01\\\", to:\\\"2024-12-31\\\"){ totalRevenue orderCount averageOrderValue customerCount } }"
check "topCustomers"      "{ topCustomers(from:\\\"2024-01-01\\\", to:\\\"2024-12-31\\\", limit:3){ customerName revenue } }"
check "topProducts"       "{ topProducts(from:\\\"2024-01-01\\\", to:\\\"2024-12-31\\\", limit:3){ productName revenue unitsSold } }"
check "revenueByPeriod"   "{ revenueByPeriod(from:\\\"2024-01-01\\\", to:\\\"2024-12-31\\\", interval: MONTH){ label revenue } }"
check "salesByTerritory"  "{ salesByTerritory(from:\\\"2024-01-01\\\", to:\\\"2024-12-31\\\"){ territory revenue } }"
check "salesByCategory"   "{ salesByCategory(from:\\\"2024-01-01\\\", to:\\\"2024-12-31\\\"){ category revenue } }"

echo
echo "Guard rails"
if q "{ orders(take:500){ items { salesOrderId } } }" | grep -q "maximum allowed items"; then
  echo "  ok    page cap rejects take:500"; pass=$((pass + 1))
else
  echo "  FAIL  page cap did not reject take:500"; fail=$((fail + 1))
fi
if q "{ salesSummary(from:\\\"2025-01-01\\\", to:\\\"2024-01-01\\\"){ totalRevenue } }" | grep -q "INVALID_DATE_RANGE"; then
  echo "  ok    reversed dates return INVALID_DATE_RANGE"; pass=$((pass + 1))
else
  echo "  FAIL  reversed dates were not rejected"; fail=$((fail + 1))
fi

echo
echo "  $pass passed, $fail failed"
echo
[ "$fail" -eq 0 ]
