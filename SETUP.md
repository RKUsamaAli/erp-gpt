# Setup — from zero to running

Three tools, four commands. ~20 minutes first time.

## 1. Install prerequisites (once)

**Docker Desktop** — runs the database
- Mac: https://www.docker.com/products/docker-desktop/ (pick Apple Silicon or Intel)
- Windows: same link; enable WSL 2 if the installer prompts
- After installing: **open Docker Desktop and wait for the whale icon to settle** — Docker must be running before any `docker` command works

**.NET SDK 8 or newer** — builds and runs the API
- Mac: `brew install --cask dotnet-sdk` (or download from https://dotnet.microsoft.com/download)
- Windows: download the SDK installer from the same page
- Verify in a **new** terminal: `dotnet --version` → should print 8.x, 9.x or 10.x

**Git** — `git --version` to check.

## 2. Clone and start the database

```bash
git clone https://github.com/HammadRehmanAwan/erp-gpt.git
cd erp-gpt/api

docker compose up -d      # starts PostgreSQL 16 + pgvector
```

First time pulls the Postgres image (~400 MB). Confirm it's up with
`docker ps` — you should see `api-db-1`.

## 3. Load the dataset (once)

We use **AdventureWorks** — a realistic ERP dataset: 31,465 sales orders,
19,820 customers, 504 products across 68 tables in 5 schemas
(sales, production, purchasing, person, humanresources).

```bash
# Requires GitHub CLI (brew install gh) authenticated once: gh auth login
gh release download data-v1 --repo HammadRehmanAwan/erp-gpt --pattern adventureworks.dump
docker cp adventureworks.dump api-db-1:/tmp/
docker exec api-db-1 pg_restore -U erpgpt -d erpgpt --clean --if-exists /tmp/adventureworks.dump
```

Takes about a minute. A few `--clean` warnings on a fresh database are
normal (it tries to drop objects that don't exist yet) — ignore them.

**Verify it worked:**

```bash
docker exec api-db-1 psql -U erpgpt -d erpgpt -P pager=off -c "SELECT (SELECT count(*) FROM sales.salesorderheader) AS orders, (SELECT count(*) FROM sales.customer) AS customers, (SELECT count(*) FROM production.product) AS products;"
```

Expected: `31465 | 19820 | 504`. Different numbers mean the restore didn't
complete — rerun step 3.

The data lives in a Docker volume, so it survives restarts. You only repeat
this step after a `docker compose down -v`.

## 4. Run the API

```bash
dotnet run --project ErpGpt.Api
```

When you see `Now listening on: http://localhost:5000`, open:

**http://localhost:5000/graphql**

The API runs in the foreground — it lives as long as that terminal tab.
Keep one tab for the server and work in others. Ctrl+C stops it.

## 5. Querying the data

### Through GraphQL (the normal way)

In the IDE at `/graphql`, click **Create Document**, paste a query, and
press Cmd+Alt+Enter (or the Run button):

```graphql
{ topCustomers(from: "2013-01-01", to: "2014-06-30", limit: 5)
  { name region totalRevenue orderCount } }
```

**Browse Schema** in the same IDE lists every available operation with its
parameters and descriptions — the full menu of what the API can do.

> Note on dates: this dump's order data runs **2022-05-30 → 2025-06-29**
> (it is a date-shifted AdventureWorks, not the original 2011–2014 vintage).
> Queries outside that window return empty results. Verify on your own copy
> with `SELECT min(orderdate), max(orderdate) FROM sales.salesorderheader;`

### Straight SQL (for exploring the raw tables)

```bash
docker exec -it api-db-1 psql -U erpgpt -d erpgpt
```

Inside psql:

| Command | Does |
|---|---|
| `\dn` | list schemas |
| `\dt sales.*` | list tables in the sales schema |
| `\d sales.salesorderheader` | show one table's columns |
| `SELECT * FROM sales.customer LIMIT 5;` | read rows (semicolon required) |
| `\q` | quit |

If output opens a pager and you're stuck at `(END)`, press `q`.

Or use a GUI: **DBeaver** / **pgAdmin** → new PostgreSQL connection →
host `localhost`, port `5432`, database `erpgpt`, user `erpgpt`,
password `devonly`.

## 6. Daily commands (from `erp-gpt/api`)

| Do | Command |
|---|---|
| Start the database | `docker compose up -d` |
| Stop the database | `docker compose stop` |
| Is it running? | `docker ps` → look for `api-db-1` |
| Run the API | `dotnet run --project ErpGpt.Api` |
| Auto-restart API on code changes | `dotnet watch --project ErpGpt.Api` |
| Regenerate KB metadata | `dotnet run --project ErpGpt.MetadataGen` |
| Wipe everything and start over | `docker compose down -v && docker compose up -d`, then redo step 3 |

## Troubleshooting

| Symptom | Fix |
|---|---|
| `Cannot connect to the Docker daemon` | Docker Desktop isn't open — launch it, wait for the whale icon |
| `Connection refused` / errors mentioning `localhost:5432` | Database isn't up — `docker compose up -d`, check `docker ps` |
| `NETSDK1045: does not support targeting .NET 8.0` | SDK too old — install .NET 8+, then open a new terminal |
| `You must install or update .NET ... version '8.0.0'` | `git pull` — the repo's roll-forward setting fixes this |
| `Address already in use` on port 5000 | The API is already running in another terminal — only one instance can hold the port. Ctrl+C the old one, or `lsof -ti :5000 \| xargs kill -9`. Rarely on Mac: AirPlay Receiver holds 5000 — disable it in System Settings, or change the port in `ErpGpt.Api/Properties/launchSettings.json` |
| Port 5432 already in use | Another Postgres running locally — stop it, or map `"5433:5432"` in `docker-compose.yml` and update `appsettings.json` |
| GraphQL queries return empty lists | Check your date range — AdventureWorks data is 2011–2014 |
| `relation "sales.customer" does not exist` | Dataset not restored — do step 3 |

## Rules once you're in

1. Work on branches + PRs to `main` — no direct pushes.
2. Change or add an endpoint → update its `kb/<endpoint>.json` in the **same PR** (CI enforces this).
3. Everyone runs the same AdventureWorks dump, so query results are
   comparable across machines. If you need a clean slate, redo step 3.
