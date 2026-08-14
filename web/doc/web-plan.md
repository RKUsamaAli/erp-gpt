# `web` — Angular chat UI · WORKING PLAN

> **Temporary document.** This is a plan, not a decision record. Once the app is
> scaffolded and the questions at the bottom are answered, fold what survives into
> `web/README.md` + `docs/decisions.md` and delete this file.

**Scope:** an Angular 21 + Bootstrap 5 single-page chat UI, ChatGPT-style. **Chat
only** — no dashboards, no admin screens, no auth. Answers are mocked until the
Phase 4 agent exists.

---

## 0. Status as scaffolded — four deltas to settle

`web/` now exists with a working chat component. It diverges from this plan in four
ways. Each is cheap to change now and expensive later, so decide before writing more
code.

| # | Plan says | Repo has | Call |
|---|---|---|---|
| 1 | Angular **21** | **20.3.27** (`latest` on npm is 22.1.2, v21-lts is 21.2.20) | Upgrade or amend the plan |
| 2 | Bootstrap 5 | **not installed** — 413 lines of hand-rolled SCSS instead | Add it, or drop it from scope |
| 3 | Project name `web` → `dist/web/browser` | name is **`chat-app`** → `dist/chat-app/browser` | Affects the Pages workflow (§8) |
| 4 | UI never composes GraphQL (§7) | `services/graphql.service.ts` posts a query straight from the browser | Replace with the `ChatService` seam (§5) |

Delta 4 is the one that matters architecturally — see §7. The generated service also
points at `https://example.com/graphql` with a `askAssistant(input:)` query that does
not exist in this project's schema, so it cannot work as written regardless.

Already correct and worth keeping: standalone components, `signal()` state, SCSS,
`provideHttpClient(withFetch())`, Enter-to-send / Shift+Enter, suggestion chips,
auto-scroll.

Also present: `zone.js` + `provideZoneChangeDetection` — expected on Angular 20,
and the reason §3's zoneless note is moot unless delta 1 is actioned.

---

## 1. Where it goes, and what it's called

**Decision: `web/` at the repo root — a sibling of `api/`, not a child.**

```
erp-gpt/
├── api/          ← .NET only
├── web/          ← NEW: Angular 21 + Bootstrap 5
├── demo/         ← the static mockup — DELETED at cutover (§8)
├── kb/  db/  eval/  agent/  embeddings/  training/  docs/
```

Three reasons it lives outside `api/`:

1. **The repo is organised one top-level folder per concern**, each with an owner
   row in the README table. A frontend is its own concern; nesting it under `api/`
   would be the only exception to that pattern.
2. **CI path filters would misfire.** `.github/workflows/kb-check.yml` triggers on
   `api/**`. Put Angular under `api/` and every frontend commit runs the KB
   integrity job for no reason.
3. **`api/` is a .NET folder.** It holds three `.csproj` projects. Dropping
   `node_modules/` (~300 MB, ~40k files) inside it slows `dotnet` file globbing and
   muddles the mental model of "what is this folder".

### Why `web/` and not `clientapp/`

`ClientApp` is a **.NET-template convention**, not a general one — it comes from the
ASP.NET Core SPA templates (`dotnet new angular`), which scaffolded `ClientApp/`
*inside* the .NET web project. The name means "the SPA belonging to this ASP.NET
Core app". At repo root, as a sibling of `api/`, that is misleading: this app is not
owned by or nested inside the API.

`web/` also matches the repo's existing naming pattern — every folder is short,
lowercase, one word, named for a concern (`api`, `db`, `kb`, `eval`, `agent`,
`demo`, `docs`). And it pairs with `api/` semantically: **`api` is the machine
interface, `web` is the human interface.**

Considered and rejected: `frontend/` (implies a `backend/` that doesn't exist here —
ours is `api/`), `ui/` (usually connotes a shared component library, not an app),
`apps/web/` (Nx/Turborepo layout — overkill for a single JS app), `app/` (collides
with Angular's own `src/app/`).

> The **folder** name and the **Angular project** name are separate, and both are
> already set: folder `web/`, project `chat-app` (in `package.json` + `angular.json`).
> The project name drives the build output — **`web/dist/chat-app/browser`** — which
> is what the Pages workflow in §8 must point at. Renaming the project to `web` for
> consistency is a two-file edit, but only worth doing before the first deploy.

### README additions once it exists

| Folder | What lives here | Owner |
|---|---|---|
| `web/` | Angular 21 chat UI (Bootstrap 5) | *TBD* |

---

## 2. Toolchain — ready, one install outstanding

| | Status | Angular 21 needs |
|---|---|---|
| Node | **v22.22.3** ✅ | `^20.19` \|\| `^22.12` \|\| `^24` |
| npm | 10.9.8 ✅ | ships with Node |
| Angular CLI | **20.2 local devDependency** ⚠️ | v21 if delta 1 (§0) is actioned |

Managed by **nvm-windows** (`C:\nvm4w\nodejs`). Versions available locally:
`22.22.3` (active), `22.12.0`, `20.19.0`, `20.11.1`, `16.20.2`.

### Two nvm-windows behaviours that will cost an hour if unknown

**1. Global npm packages are per Node version.** The Angular CLI installed under
22.22.3 **disappears** if you `nvm use 16` — `ng` will report "not recognised" and
look like a broken install. It isn't; you're on the wrong Node. Confirm with
`node -v` before debugging anything CLI-shaped.

**2. `nvm use` is global, not per-terminal.** It repoints one symlink
(`C:\nvm4w\nodejs`), so switching for another project silently changes Node for
**every** open terminal, including one already running `ng serve`. Node 16 is still
installed here, so this is a live hazard, not a hypothetical.

### Pin the version so the failure is loud

`.nvmrc` alone is not enough on Windows — nvm-windows support for reading it varies
by release, so don't depend on it. Make npm enforce the floor instead, in
`web/package.json`:

```json
"engines": { "node": ">=22.12.0", "npm": ">=10.0.0" }
```

plus `web/.npmrc`:

```
engine-strict=true
```

Now a wrong Node **fails `npm install` with a readable message** rather than
producing a mysterious build error three steps later. Add `web/.nvmrc` containing
`22.22.3` as well — it documents intent for the team and works with nvm on
Mac/Linux.

> Version ranges above are from Angular's published support matrix. `ng version`
> after install is the authority — if the CLI disagrees, the CLI is right.

---

## 3. Scaffold

```bash
npm install -g @angular/cli@21
```

Remember this global install is bound to the active Node version (§2). To skip the
global entirely — worth it if Node switching is frequent — use `npx` instead and
drop the command above:

```bash
npx -p @angular/cli@21 ng new web --style=scss --routing=false --ssr=false --package-manager=npm
```

From the repo root:

```bash
ng new web --style=scss --routing=false --ssr=false --package-manager=npm
```

Flag notes:
- `--routing=false` — one screen. Chat selection is state, not a URL. (Revisit only
  if deep-linking to a thread, `/chat/:id`, becomes a requirement.)
- `--ssr=false` — this app is a thin client over an API; SSR buys nothing and adds a
  Node hosting requirement.
- Standalone components are the default in Angular 17+; no flag needed.
- **If the CLI offers zoneless**, accept it. The app is signal-driven and has no
  reason to carry Zone.js. If the prompt doesn't appear, don't fight it — this is
  not worth a config detour.

### Bootstrap 5

```bash
cd web && npm install bootstrap@5 bootstrap-icons
```

Wire it in `angular.json` under `projects.web.architect.build.options`:

```json
"styles": [
  "node_modules/bootstrap/dist/css/bootstrap.min.css",
  "node_modules/bootstrap-icons/font/bootstrap-icons.css",
  "src/styles.scss"
],
"scripts": ["node_modules/bootstrap/dist/js/bootstrap.bundle.min.js"]
```

The `scripts` entry is only needed for Bootstrap's JS components (dropdown, modal,
offcanvas). The offcanvas sidebar on mobile uses it — keep it.

**Bootstrap carries layout, not identity.** Use its grid, utilities, form controls
and offcanvas; the chat-specific look (bubbles, streaming cursor, thread list) comes
from `demo/index.html`'s CSS, ported into component styles. Don't try to express a
message bubble as a stack of utility classes.

---

## 4. Structure

```
web/src/app/
├── app.ts                          root shell: sidebar + chat pane
├── app.config.ts                   providers (HttpClient, chat service token)
│
├── core/
│   ├── chat-service.ts             ← THE INTERFACE (see §5)
│   ├── mock-chat-service.ts        canned replies, simulated streaming
│   ├── http-chat-service.ts        stub — throws until Phase 4 lands
│   └── chat-store.ts               signal store: threads, active id, busy
│
├── models/
│   └── chat.models.ts              ChatThread, ChatMessage, AnswerBlock
│
└── chat/
    ├── sidebar/                    brand, New chat, thread list
    ├── chat-header/                title + subtitle
    ├── message-thread/             scroll container, empty state
    ├── message-bubble/             one turn; renders AnswerBlock[]
    ├── answer-block/               p | ul | table | code renderer
    ├── composer/                   auto-grow textarea, send/stop
    └── suggestion-chips/           the three starter prompts
```

Angular 21 idioms throughout: **standalone components**, **signals** for state
(`signal`, `computed`, `linkedSignal`), **`@if` / `@for`** control flow, and
`ChangeDetectionStrategy.OnPush` on every component. No `NgModule`, no RxJS
`BehaviorSubject` state, no `*ngIf`.

---

## 5. The contract that matters

Everything hinges on one interface. Get it right and swapping mock → real backend is
a one-line provider change.

```ts
export interface ChatService {
  ask(question: string, threadId: string): Observable<AnswerChunk>;
}
```

Provided by DI token so the swap is config, not a rewrite:

```ts
// app.config.ts
{ provide: CHAT_SERVICE, useClass: MockChatService }   // → HttpChatService later
```

**`MockChatService`** reproduces `demo/index.html`: three regex-matched canned
answers (top customers / Canada sales / low stock bikes), a fallback, a 450–800 ms
"thinking" delay, then chunk-by-chunk emission.

**`HttpChatService`** exists as a stub that throws `NotImplementedError`. It is
written but not wired, so the shape is proven before the backend arrives.

### Why answers are structured, not HTML strings

The demo pushes raw HTML through `innerHTML`. Porting that to Angular means
`DomSanitizer.bypassSecurityTrustHtml` on **model-generated content** — an XSS hole
the moment a real LLM is on the other end, and the model output is untrusted text
by the project's own architecture (`docs/architecture.md`, the validation gate).

Model a typed union instead, and let Angular's template escaping do its job:

```ts
export type AnswerBlock =
  | { kind: 'text';  text: string }
  | { kind: 'list';  items: string[] }
  | { kind: 'table'; headers: string[]; rows: string[][] }
  | { kind: 'code';  lang: string; source: string };
```

This is also the better fit for what the backend will actually return: the agent's
`AnswerRenderer` (step 7 in `agent/README.md`) turns **API rows** into an answer.
Rows are naturally a table, not a blob of markup. `bypassSecurityTrustHtml` should
appear nowhere in this codebase.

---

## 6. Feature checklist — ported from `demo/index.html`

The mockup is the spec. It already works; this is a port, not a redesign.

**Sidebar**
- [ ] Brand + "＋ New chat" button
- [ ] Recent thread list — title, time, message count
- [ ] Active thread highlighted; click to switch
- [ ] "No recent chats yet" empty state
- [ ] Threads with zero messages are hidden from the list
- [ ] Collapses to a Bootstrap offcanvas below `md`

**Thread**
- [ ] User / assistant bubbles with `You` / `AI` avatars
- [ ] Empty state: heading + 3 suggestion chips that submit on click
- [ ] Auto-scroll to bottom on new content
- [ ] "Thinking…" animated dots before the first chunk

**Composer**
- [ ] Auto-grow textarea, capped at 160 px
- [ ] **Enter** sends · **Shift+Enter** newline
- [ ] Send disabled while empty or busy
- [ ] Send becomes **Stop** while streaming

**Streaming**
- [ ] Text types out progressively with a blinking cursor
- [ ] Tables and lists appear as complete blocks with a fade-in — *not* typed
      character by character (this is what makes the demo feel like ChatGPT)
- [ ] Stop cancels mid-stream and keeps the partial answer
- [ ] Switching thread or starting a new chat cancels an in-flight stream

**Threading**
- [ ] Thread title = first question, truncated to 42 chars
- [ ] Messages persist across thread switches
- [ ] In-memory only for v1 — see open question Q3

Cancellation is `takeUntil` / unsubscribe on an Observable, replacing the demo's
`AbortController`. When `HttpChatService` arrives, unsubscribing must also abort the
underlying HTTP request.

---

## 7. Talking to the API

Dev proxy, so the browser sees one origin and CORS never comes up —
`web/proxy.conf.json`:

```json
{ "/graphql": { "target": "http://localhost:5000", "secure": false } }
```

Referenced from `angular.json` → `serve.options.proxyConfig`. Then `ng serve` on
:4200 and the API on :5000.

**The chat UI does not call `/graphql` in v1.** That endpoint is a *data* API, and
turning a question into a query is exactly the agent's job — the layer that doesn't
exist yet. The proxy is set up now so the plumbing is proven, and a future
`/api/chat` route slots into the same config.

> Do not let the Angular app compose GraphQL from user text. That would put query
> construction in the browser and break the project's core rule — only the API knows
> about data (`docs/architecture.md`).

---

## 8. Cutover — replacing `demo/`

**Decided: `web` replaces `demo/`, and `demo/` is deleted.** Not on day one —
see the ordering rule at the end of this section.

### `demo/` holds two pages, and only one is replaced

| File | Fate |
|---|---|
| `demo/index.html` — the chat mockup | **Replaced.** Ported per §6, then deleted. |
| `demo/execution-flow.html` — *Execution Flow — ERP GPT* | **Not replaced by anything.** |

The execution-flow page is a separate visualization, not chat. Rebuilding it in
Angular is outside the "chat only" scope, so **carry it across as a static asset**:

```
web/public/execution-flow.html
```

Anything in Angular's `public/` is copied to the build output as-is, so it keeps
serving at `/execution-flow.html` with zero porting work and no routing. Revisit
only if it ever needs to share state with the chat.

### The Pages workflow has to start building

`.github/workflows/deploy-demo-pages.yml` currently uploads the `demo` folder
directly — no build step, because static HTML needs none. An Angular app does.
Replace the trigger and the upload:

```yaml
on:
  push:
    branches: [main]
    paths:
      - "web/**"
      - ".github/workflows/deploy-demo-pages.yml"
  workflow_dispatch:
```

```yaml
      - uses: actions/setup-node@v4
        with:
          node-version: 22
          cache: npm
          cache-dependency-path: web/package-lock.json

      - run: npm ci
        working-directory: web

      - run: npx ng build --base-href /erp-gpt/
        working-directory: web

      - uses: actions/upload-pages-artifact@v3
        with:
          path: web/dist/chat-app/browser   # project name, NOT folder name
```

Three things that will bite:

1. **`--base-href /erp-gpt/` is mandatory.** Project Pages serve from a subpath, not
   the domain root. Without it every asset 404s and you get a blank white page —
   the single most common Angular-on-Pages failure.
2. **Confirm the output path.** Angular 17+ emits `dist/<project>/browser`; the
   `browser` segment is easy to miss. Check it after the first local `ng build`.
3. Rename the workflow file and its `name:` — "deploy-demo-pages" stops being true.
   Keep the `concurrency: group: pages` block exactly as it is.

### Links that break

| Where | Now | After |
|---|---|---|
| `README.md:76` | Folder: `demo/` | `web/` |
| `README.md:77` | Pages URL | unchanged — same URL, new contents |
| `README.md:78` | htmlpreview link | **delete** |
| `demo/README.md` | whole file | deleted with the folder |

The **htmlpreview links cannot survive.** That service fetches one static HTML file
straight from the repo; it cannot run a build. Losing the zero-deploy preview of a
branch is a genuine cost of this move — the replacement is the Pages URL after
merge, or `ng serve` locally.

### Ordering rule

**Do not delete `demo/` until the Angular app is deployed and verified on the live
Pages URL.** The link is public and in the README. Sequence:

1. Build `web` to step 8 of §9 while `demo/` stays put and keeps deploying.
2. One PR: switch the workflow, copy `execution-flow.html` into `public/`, update
   the README links, delete `demo/`.
3. Merge, wait for the deploy, open the Pages URL, check **both** the chat and
   `/execution-flow.html`.
4. Only then is the cutover done. If the deploy fails, revert that one PR and
   `demo/` comes straight back.

---

## 9. Explicitly out of scope for v1

Auth/login · real LLM answers · conversation memory and follow-ups
("only active ones") · markdown rendering · file upload · voice · charts · dark-mode
toggle (ship one theme) · thread rename/delete · i18n.

Each is a real feature; none is needed to prove the shell works.

---

## 10. Build order

| # | Step | Done when |
|---|---|---|
| 1 | ~~Upgrade Node~~ (done — v22.22.3), install CLI | `ng version` prints Angular 21 |
| 2 | `ng new web`, add Bootstrap | `ng serve` shows the default page |
| 3 | Models + `ChatService` interface + `MockChatService` | Unit test: `ask()` emits chunks then completes |
| 4 | Static shell — sidebar, header, thread, composer | Matches `demo/index.html` with hard-coded messages |
| 5 | `ChatStore` signals — threads, switching, new chat | Thread list works; messages survive switching |
| 6 | Wire composer → service → thread | Canned answers render end to end |
| 7 | Streaming + cursor + stop | Feels like the demo |
| 8 | Responsive: offcanvas sidebar below `md` | Usable at 375 px |
| 9 | **Cutover PR** (§8) — workflow, `public/execution-flow.html`, README links, delete `demo/` | Pages URL serves the chat **and** `/execution-flow.html` |
| 10 | `web/README.md`, delete this file | A teammate can run it from the README alone |

Steps 3–7 are the app. 1–2 are setup, 8 is polish, 9 is the public switchover — and
it is the only step that can break something a non-developer looks at, so it goes
last and alone in its own PR.

---

## 11. Open questions

**Q1 — Owner.** Every other folder in the README table has named owners.
`web/` needs one.

**~~Q2 — Does this replace `demo/`?~~ RESOLVED: yes.** `web` replaces it and
`demo/` is deleted at cutover. Mechanics, ordering and the
`execution-flow.html` carve-out are in §8.

**Q3 — Do threads survive a page refresh?** In-memory is fine for a demo.
`localStorage` is ~20 lines. Real persistence belongs in the `ChatThread` /
`ChatTurn` entities already sketched in `agent/README.md` — don't build a
client-side scheme that later has to be unpicked.

**Q4 — Angular 21 specifics to confirm at scaffold time**, not guessed at now:
whether the CLI defaults to zoneless, and whether signal-based forms are stable
enough for the composer (a plain `signal` + `[(ngModel)]`-free textarea binding is
the safe fallback).

**Q5 — What does `POST /api/chat` return?** Blocked on Phase 4. `AnswerBlock[]` in
§5 is this document's proposal; the agent's `AnswerRenderer` has to agree with it.
Settle it when the agent is built — and when it is settled, it belongs in
`docs/decisions.md`, not here.
