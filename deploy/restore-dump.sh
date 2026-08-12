#!/usr/bin/env bash
#
# Loads AdventureWorks into the Render Postgres that render.yaml created.
# Run once, after the Blueprint finishes provisioning.
#
#   ./deploy/restore-dump.sh "<External Database URL from the Render dashboard>"
#
# Everything runs inside the pgvector/pgvector:pg16 image, so nobody needs a
# matching pg_restore installed locally — the dump is v16 and older clients
# reject it. That is the same image docker-compose.yml pulls, so it is normally
# already cached on a developer machine.

set -euo pipefail

DB_URL="${1:-}"
DUMP="${2:-$(dirname "$0")/../api/adventureworks.dump}"

if [[ -z "$DB_URL" ]]; then
  echo "usage: $0 <external-database-url> [path-to-dump]" >&2
  echo "Find the URL under: Render dashboard > erpgpt-db > Connections >" >&2
  echo "External Database URL." >&2
  exit 1
fi

if [[ ! -f "$DUMP" ]]; then
  echo "Dump not found at: $DUMP" >&2
  echo "Fetch it with:" >&2
  echo "  gh release download data-v1 --repo HammadRehmanAwan/erp-gpt \\" >&2
  echo "     --pattern adventureworks.dump --dir api/" >&2
  exit 1
fi

DUMP_ABS="$(cd "$(dirname "$DUMP")" && pwd)/$(basename "$DUMP")"

# Render refuses unencrypted connections; the dashboard URL omits the mode.
case "$DB_URL" in
  *sslmode=*) ;;
  *\?*)       DB_URL="${DB_URL}&sslmode=require" ;;
  *)          DB_URL="${DB_URL}?sslmode=require" ;;
esac

echo "==> Restoring $(du -h "$DUMP_ABS" | cut -f1) dump into Render Postgres…"
# --no-owner/--no-privileges are required: the dump was taken as the local
# "erpgpt" role, which does not exist on Render. Without them every ownership
# statement fails. --clean makes the script safe to re-run.
docker run --rm -v "$DUMP_ABS:/tmp/aw.dump:ro" pgvector/pgvector:pg16 \
  pg_restore --no-owner --no-privileges --clean --if-exists \
  --dbname "$DB_URL" /tmp/aw.dump

# The vector store for the RAG step lives in this same instance (decision O4).
echo "==> Enabling pgvector…"
docker run --rm pgvector/pgvector:pg16 psql "$DB_URL" -v ON_ERROR_STOP=1 \
  -c "CREATE EXTENSION IF NOT EXISTS vector;"

echo "==> Verifying…"
docker run --rm pgvector/pgvector:pg16 psql "$DB_URL" -P pager=off -c \
  "SELECT (SELECT count(*) FROM sales.salesorderheader) AS orders,
          (SELECT count(*) FROM sales.customer)         AS customers,
          (SELECT count(*) FROM production.product)     AS products,
          pg_size_pretty(pg_database_size(current_database())) AS size;"

echo
echo "Expected: 31465 | 19820 | 504 | ~113 MB"
echo "Different numbers mean the restore did not complete — just run this again."
