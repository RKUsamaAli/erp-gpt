# Setup — from zero to running

Three tools, then three commands. ~15 minutes first time.

## 1. Install prerequisites (once)

**Docker Desktop** — runs the database
- Mac: https://www.docker.com/products/docker-desktop/ (pick Apple Silicon or Intel)
- Windows: same link; enable WSL 2 if the installer prompts
- After installing: **open Docker Desktop and wait for the whale icon to settle** — Docker must be running before any `docker` command works

**.NET SDK 8 or newer** — builds and runs the API
- Mac: `brew install --cask dotnet-sdk` (or download from https://dotnet.microsoft.com/download)
- Windows: download the SDK installer from the same page
- Verify in a **new** terminal: `dotnet --version` → should print 8.x, 9.x or 10.x

**Git** — you likely have it; `git --version` to check.

## 2. Clone and run

```bash
git clone https://github.com/HammadRehmanAwan/erp-gpt.git
cd erp-gpt/api

docker compose up -d                  # starts PostgreSQL + pgvector
dotnet run --project ErpGpt.Api       # builds, creates the DB, seeds 2 years of data
```

First run takes a few minutes: Docker pulls the Postgres image (~400 MB),
NuGet restores packages, and the seeder creates ~2,000 orders. Later runs
take seconds.

When you see `Now listening on: http://localhost:5000`, open:

**http://localhost:5000/graphql**

and run this query in the IDE to confirm your setup works:

```graphql
{ topCustomers(from: "2024-09-01", to: "2026-08-10", limit: 5)
  { name region totalRevenue orderCount } }
```

Five customers with revenue figures = you're done.

## 3. Daily commands (run from `erp-gpt/api`)

| Do | Command |
|---|---|
| Start the database | `docker compose up -d` |
| Stop the database | `docker compose stop` |
| Is it running? | `docker ps` → look for `api-db-1` |
| Run the API | `dotnet run --project ErpGpt.Api` |
| Auto-restart API on code changes | `dotnet watch --project ErpGpt.Api` |
| Regenerate KB metadata | `dotnet run --project ErpGpt.MetadataGen` |
| Reset database to fresh seed | `docker compose down -v && docker compose up -d`, then restart the API |
| Look inside Postgres directly | `docker exec -it api-db-1 psql -U erpgpt -d erpgpt` (then `\dt` to list tables, `\q` to quit) |

Note: the API runs in the foreground — it lives as long as that terminal
tab. Keep one tab for the server, work in others. Ctrl+C stops it.

## Troubleshooting

| Symptom | Fix |
|---|---|
| `Cannot connect to the Docker daemon` | Docker Desktop isn't open — launch it, wait for the whale icon |
| `Connection refused` / errors mentioning `localhost:5432` | Database isn't up — `docker compose up -d` first, check with `docker ps` |
| `NETSDK1045: The current .NET SDK does not support targeting .NET 8.0` | Your SDK is too old — install .NET 8+ (see step 1), then open a new terminal |
| `You must install or update .NET ... version '8.0.0'` | Pull the latest code (`git pull`) — the repo's roll-forward setting fixes this |
| `Address already in use` on port 5000 | The API is already running in another terminal — only one instance can hold the port. Ctrl+C the old one, or `lsof -ti :5000 \| xargs kill -9` if you can't find it. Rarely on Mac: AirPlay Receiver holds 5000 — disable it in System Settings, or change the port in `ErpGpt.Api/Properties/launchSettings.json` |
| Port 5432 already in use | Another Postgres is running on your machine — stop it, or change the mapping in `docker-compose.yml` to `"5433:5432"` and update `appsettings.json` to match |
| GraphQL IDE loads but queries return errors | Make sure you started with `dotnet run` from the repo — Development mode is what creates and seeds the database |

## Rules once you're in

1. Work on branches + PRs to `main` — no direct pushes.
2. Change or add an endpoint → update its `kb/<endpoint>.json` in the **same PR** (CI enforces this).
3. Seed data is deterministic (`Random(42)`) — everyone has byte-identical data, so query results are comparable across machines.
