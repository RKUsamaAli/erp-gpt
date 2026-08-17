#!/usr/bin/env bash
#
# Loads AdventureWorks into the Render Postgres that render.yaml created, and
# checks what actually landed.
#
#   ./deploy/restore-dump.sh "<External Database URL>"            restore, then verify
#   ./deploy/restore-dump.sh --verify "<External Database URL>"   verify only
#
# Find the URL under: Render dashboard > erpgpt-db > Connections > External
# Database URL. The internal URL will not resolve from your machine.
#
# Everything runs inside the pgvector/pgvector:pg16 image, so nobody needs a
# matching pg_restore installed locally — the dump is v16 and older clients
# reject it. That is the same image docker-compose.yml pulls, so it is normally
# already cached on a developer machine.

set -euo pipefail

VERIFY_ONLY=0
if [[ "${1:-}" == "--verify" ]]; then
  VERIFY_ONLY=1
  shift
fi

DB_URL="${1:-}"
DUMP="${2:-$(dirname "$0")/../api/adventureworks.dump}"

if [[ -z "$DB_URL" ]]; then
  echo "usage: $0 [--verify] <external-database-url> [path-to-dump]" >&2
  echo "Find the URL under: Render dashboard > erpgpt-db > Connections >" >&2
  echo "External Database URL." >&2
  exit 1
fi

# Render refuses unencrypted connections; the dashboard URL omits the mode.
case "$DB_URL" in
  *sslmode=*) ;;
  *\?*)       DB_URL="${DB_URL}&sslmode=require" ;;
  *)          DB_URL="${DB_URL}?sslmode=require" ;;
esac

psql_q() {
  docker run --rm pgvector/pgvector:pg16 psql "$DB_URL" "$@"
}

if [[ $VERIFY_ONLY -eq 0 ]]; then
  if [[ ! -f "$DUMP" ]]; then
    echo "Dump not found at: $DUMP" >&2
    echo "Fetch it with:" >&2
    echo "  gh release download data-v1 --repo HammadRehmanAwan/erp-gpt \\" >&2
    echo "     --pattern adventureworks.dump --dir api/" >&2
    exit 1
  fi

  DUMP_ABS="$(cd "$(dirname "$DUMP")" && pwd)/$(basename "$DUMP")"

  echo "==> Restoring $(du -h "$DUMP_ABS" | cut -f1) dump into Render Postgres…"
  # --no-owner/--no-privileges are required: the dump was taken as the local
  # "erpgpt" role, which does not exist on Render. Without them every ownership
  # statement fails. --clean makes the script safe to re-run.
  docker run --rm -v "$DUMP_ABS:/tmp/aw.dump:ro" pgvector/pgvector:pg16 \
    pg_restore --no-owner --no-privileges --clean --if-exists \
    --dbname "$DB_URL" /tmp/aw.dump

  # The vector store for the RAG step lives in this same instance (decision O4).
  echo "==> Enabling pgvector…"
  psql_q -v ON_ERROR_STOP=1 -c "CREATE EXTENSION IF NOT EXISTS vector;"
fi

echo "==> Verifying…"

# One round trip, pipe-separated, so each number can be asserted rather than
# eyeballed. A missing table means the restore never completed, and psql fails
# here rather than further downstream in the API.
if ! raw="$(psql_q -tA -F'|' -v ON_ERROR_STOP=1 -c \
  "SELECT (SELECT count(*) FROM sales.salesorderheader),
          (SELECT count(*) FROM sales.customer),
          (SELECT count(*) FROM production.product),
          (SELECT count(*) FROM information_schema.schemata
             WHERE schema_name IN
               ('sales','production','person','purchasing','humanresources')),
          (SELECT count(*) FROM pg_extension WHERE extname = 'vector'),
          (SELECT max(orderdate)::date FROM sales.salesorderheader),
          pg_size_pretty(pg_database_size(current_database()));" 2>&1)"; then
  echo >&2
  echo "  Could not read the database. Either nothing has been restored yet," >&2
  echo "  or this is the internal URL rather than the external one." >&2
  echo >&2
  echo "$raw" | tail -3 >&2
  exit 1
fi

IFS='|' read -r orders customers products schemas vector latest size <<<"$raw"

fail=0
check() {
  if [[ "$2" == "$3" ]]; then
    printf '  ok    %-22s %s\n' "$1" "$2"
  else
    printf '  FAIL  %-22s %s (expected %s)\n' "$1" "$2" "$3"
    fail=1
  fi
}

check "sales orders"   "$orders"    31465
check "customers"      "$customers" 19820
check "products"       "$products"  504
check "ERP schemas"    "$schemas"   5
check "pgvector"       "$vector"    1
printf '        %-22s %s\n' "newest order" "$latest"
printf '        %-22s %s\n' "database size" "$size"

echo
if [[ $fail -eq 0 ]]; then
  echo "Restore verified — the database matches the dump."
  echo "Now check the API end to end:"
  echo "  API=https://erpgpt-api.onrender.com ./api/ErpGpt.GraphQLApi/verify.sh"
else
  echo "Restore is incomplete. Re-run without --verify to load it again." >&2
  exit 1
fi
