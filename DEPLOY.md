# Deploy — shared test environment on Render

Puts the GraphQL API, the AdventureWorks database, and a browser-based SQL
client on the internet so the whole team can test against one instance.
About 15 minutes, most of it waiting for the first build.

Everything below is defined in [`render.yaml`](render.yaml), so Render creates
all three services from one click rather than three separate setups.

## What the team gets

| URL | What it is |
|---|---|
| `https://erpgpt-api.onrender.com/graphql` | GraphQL IDE — the testing surface |
| `https://erpgpt-api.onrender.com/health` | Liveness + database connectivity |
| `https://erpgpt-db-ui.onrender.com` | pgweb — browse tables, run SELECTs |

Render assigns the real subdomains on creation; if the names are taken you get
a suffix. The dashboard shows the actual URLs.

## Before you start

- A [Render](https://render.com) account (sign in with GitHub).
- The repo pushed to GitHub, with `render.yaml` on the branch you deploy.
- Docker running locally — only for the one-time dataset load.

## 1. Push the deployment files

```bash
git add render.yaml DEPLOY.md deploy/ api/ErpGpt.GraphQLApi/Dockerfile \
        api/ErpGpt.GraphQLApi/.dockerignore api/ErpGpt.GraphQLApi/Program.cs
git commit -m "chore(deploy): host the GraphQL API and a DB browser on Render"
git push
```

## 2. Create everything (one click)

1. Render dashboard → **New** → **Blueprint**.
2. Pick the `erp-gpt` repo, choose the branch, → **Connect**.
3. Render reads `render.yaml` and lists `erpgpt-api`, `erpgpt-db-ui` and
   `erpgpt-db`. → **Apply**.

First build takes ~5 minutes (it restores NuGet packages and compiles). The
API will show as unhealthy until step 3 — it has no data yet.

## 3. Load AdventureWorks (once)

Render's database starts empty. Copy the connection string from
**Dashboard → `erpgpt-db` → Connections → External Database URL**, then:

```bash
./deploy/restore-dump.sh "postgresql://…the external URL…"
```

Takes about a minute and prints `31465 | 19820 | 504 | ~113 MB` when it worked.
The script is safe to re-run.

If you don't have the dump locally:

```bash
gh release download data-v1 --repo HammadRehmanAwan/erp-gpt \
   --pattern adventureworks.dump --dir api/
```

The script also enables `pgvector`, so the RAG step has its vector store in the
same instance (decision O4).

## 4. Verify

Three layers, cheapest first. Run them in order — each one only makes sense if
the previous passed.

**Did the data land?** Re-runnable any time, and it asserts rather than asking
you to eyeball numbers:

```bash
./deploy/restore-dump.sh --verify "postgresql://…external URL…"
```

```
  ok    sales orders           31465
  ok    customers              19820
  ok    products               504
  ok    ERP schemas            5
  ok    pgvector               1
        newest order           2025-06-29
        database size          114 MB
```

Exit code is 0 only when every row matches. A half-finished restore fails on
the count, an empty database fails with "nothing has been restored yet", and
the wrong URL fails the same way — the internal hostname doesn't resolve from
your laptop.

**Can the API reach it?** This is the private-network wiring, not the data:

```bash
curl https://erpgpt-api.onrender.com/health
# {"status":"healthy","database":"connected"}
```

`connected` means the injected `DATABASE_URL` parsed and Postgres answered.
On a free plan the first call can take a minute while the service wakes.

**Does the whole thing work?** `verify.sh` already smoke-tests all 16
endpoints, and it takes any address:

```bash
API=https://erpgpt-api.onrender.com ./api/ErpGpt.GraphQLApi/verify.sh
```

18 checks — browse, detail, aggregations, plus the page cap and
`INVALID_DATE_RANGE` guard rails. It exercises specific rows
(`customer(id:29641)`, `order(id:51131)`, `product(id:782)`), so passing means
the restore is genuinely intact and not merely the right size.

Then send the team the two URLs. Nothing to install on their side.

> **Dates matter.** This dump's orders run **2022-05-30 → 2025-06-29**. Queries
> outside that window return empty results, which reads as a broken API.

```graphql
{ topCustomers(from: "2023-01-01", to: "2024-06-30", limit: 5)
  { customerId customerName territory revenue orderCount } }
```

## Updating

`autoDeploy` is on: pushing to the deployed branch rebuilds and redeploys the
API automatically. `buildFilters` limits that to commits touching
`api/ErpGpt.GraphQLApi/**`, so `kb/` and `docs/` changes don't trigger builds.

## Cost — $0

Every service is on a free plan. Four limits come with that, and all four are
survivable for a test environment, but tell the team about the first two or
they will file bugs against them.

**Requests are slow after idle.** Free web services sleep after 15 minutes
without traffic and take about a minute to wake. The first `/graphql` request
of the morning looks like a hang, then everything is normal.

**The database expires.** A free Postgres is deleted **30 days after
creation** (plus a 14-day grace period to upgrade). Put a reminder in the
calendar now. If it lapses, you get a new empty database and redo step 3 —
which is why that step is a script and not a list of commands.

**No backups.** Free instances have no point-in-time recovery. Nothing here is
irreplaceable — the dump is the source of truth — but don't let anything
one-of-a-kind accumulate in this database.

**One free database per workspace**, and 750 free instance hours per workspace
per calendar month across all services. Sleeping services don't burn hours, so
two services on one workspace stay inside it comfortably.

When this stops being disposable, it's two words in `render.yaml`:

| Service | Free → | Cost |
|---|---|---|
| `erpgpt-api` | `plan: starter` | $7/mo — no cold starts |
| `erpgpt-db` | `plan: basic-256mb` | $6/mo — no expiry, backups included |

The 114 MB dataset is far inside the free 1 GB cap, so storage is never the
reason to upgrade.

## Access and exposure

Both URLs are public — anyone who has them can query the API and read the
database through pgweb. That was a deliberate choice for a test environment on
public sample data. Two things bound the damage:

- pgweb runs with `--readonly`, which rejects anything but reads, and
  `--lock-session`, which stops anyone pointing it at another database.
- The API is read-only by design; `MaxPageSize`, a depth limit of 12 and a
  30-second execution timeout are already enforced in `Program.cs`.

`--readonly` is a guard in pgweb's own query layer, not a database permission —
pgweb says as much on startup. Before any real or customer data goes near this
environment, do the two things that actually close it:

1. Add `--auth-user` / `--auth-pass` to the `dockerCommand` in `render.yaml`,
   and put the API behind the `[Authorize]` wiring that `api/README.md` still
   lists as a TODO.
2. Give pgweb its own `GRANT SELECT`-only Postgres role instead of the owner.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| API deploy fails on health check | Dataset not loaded — do step 3, then **Manual Deploy** |
| `Cannot load library libgssapi_krb5.so.2` in API logs | Harmless. Npgsql probes for Kerberos, which the .NET runtime image doesn't ship. `/health` returning `connected` proves the connection is fine |
| pgweb: `SSL is not enabled on the server` | The `--ssl=require` flag is missing from `dockerCommand`. Render refuses unencrypted connections |
| `pg_restore: error: unsupported version` | Local pg_restore is older than 16. Use `deploy/restore-dump.sh`, which runs v16 in a container |
| Restore fails on `ALTER ... OWNER TO erpgpt` | Missing `--no-owner --no-privileges`. Render's DB user is generated, not `erpgpt`. The script passes both |
| Queries return empty arrays | Date range — the data is 2022–2025, not 2011–2014 |
| First request of the day takes ~1 min | Free plan cold start after 15 min idle. Expected. `plan: starter` removes it |
| Database vanished / `does not exist` after a month | Free Postgres expires 30 days after creation. Create a new one and re-run step 3, or move to `basic-256mb` |
