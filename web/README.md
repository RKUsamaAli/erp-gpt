# Web — Angular chat UI

**Owner: unassigned** · Stack: Angular 21, Bootstrap 5.3, TypeScript 5.9,
Node 22 · Icons are inline SVG, not an icon font

The human interface to the ERP. Someone types *"who were our biggest customers
last quarter?"* and the answer appears here — no SQL, no schema knowledge, no
clicking through screens.

Replaces [`demo/`](../demo/) once it is deployed. Until then `demo/` stays as
the public GitHub Pages link; see the cutover section of
[`docs/web-plan.md`](docs/web-plan.md).

## The one rule

**This app never composes a query.** It hands a question to a `ChatService` and
renders whatever structured blocks come back. Turning a question into an API
call is the agent's job, and only the API knows about data
([`docs/architecture.md`](../docs/architecture.md)).

```
ChatComponent → CHAT_SERVICE ─┬─ MockChatService   canned ERP answers (today)
                              └─ HttpChatService   throws until the Phase 4
                                                   agent exists (../agent/)

SidebarComponent → CHAT_STORE ─┬─ LocalChatStore   localStorage (today)
                               └─ ApiChatStore     when ChatThread / ChatTurn
                                                   land (../agent/README.md)
```

Swapping either implementation is one line in
[`src/app/app.config.ts`](src/app/app.config.ts).

> **Projects and chats are defined client-side for now.** `agent/README.md`
> plans `ChatThread` / `ChatTurn` entities server-side (roadmap steps 9–12).
> These are the same idea, so the two shapes will need reconciling — which is
> why nothing reaches storage except through `CHAT_STORE`.

Two corollaries, both load-bearing:

1. **Answers are data, not markup.** `AnswerBlock` is a typed union — text,
   list, table, code — rendered through ordinary interpolation. Model output is
   untrusted until it passes the validation gate, so there is no
   `bypassSecurityTrustHtml` anywhere and there must never be one.
2. **Bootstrap carries layout, not identity.** Grid, utilities, offcanvas and
   form controls come from Bootstrap; bubbles, avatars, gradients and the
   streaming cursor stay in component SCSS. Don't express a message bubble as a
   stack of utility classes.

## Run it

```bash
npm install
npm start          # http://localhost:4200
```

Node must be 20.19+, 22.12+ or 24+. `engines` in `package.json` fails the
install otherwise, rather than letting it surface as a confusing build error
later. If `ng` reports "not recognised" after switching Node versions, that is
nvm — global packages are installed per Node version.

| Do | Command |
|---|---|
| Dev server | `npm start` |
| Production build | `npm run build` → `dist/chat-app/browser` |
| Unit tests | `npm test` |
| Guard rail (below) | see next section |

The build output folder is `chat-app`, the **Angular project** name — not
`web`, the folder name. Anything pointing at the build (the Pages workflow)
must use `web/dist/chat-app/browser`.

## Guard rail

This must print nothing. It is the check that the one rule above still holds,
and it failed before the seam existed:

```bash
grep -rniE "graphql|bypassSecurityTrustHtml|innerHTML|example\.com" src/ | grep -vE ':[0-9]+:\s*(\*|//|/\*)'
```

## Layout

```
src/app/
├── core/                      the seams and app-wide services
│   ├── chat-service.ts          interface + CHAT_SERVICE token
│   ├── mock-chat-service.ts     canned answers, two-speed streaming
│   ├── http-chat-service.ts     stub, no providedIn — stays tree-shaken
│   ├── chat-store.ts            interface + CHAT_STORE token
│   ├── local-chat-store.ts      projects/chats in localStorage
│   ├── theme.service.ts         writes data-bs-theme onto <html>
│   └── layout.service.ts        sidebar drawer state
├── models/
│   ├── chat.models.ts         AnswerBlock · AnswerChunk · appendChunk
│   └── workspace.models.ts    Project · Chat
├── shell/
│   ├── sidebar/               projects, chats, new/rename
│   └── rename-field/          inline rename, shared by both
└── chat/
    ├── chat.component.*       thread, composer, streaming state
    ├── answer-block/          renders one AnswerBlock
    └── theme-toggle/          light/dark switch
```

## Theming

`data-bs-theme` on `<html>` drives **both** this app's tokens (`src/styles.scss`)
and Bootstrap 5.3's own — one attribute, one source of truth. Components never
hardcode a colour, so adding a theme means redefining that token block and
nothing else.

The choice follows the operating system until the toggle is used, then sticks.
An inline script in `index.html` resolves it before first paint; without that,
a light-theme user gets a dark flash on every load.

## Routing

A chat is the only addressable thing — `/chat/:chatId`. A chat's project is a
property of the chat, so the URL does not repeat it (`/project/x/chat/y` would
permit states where `y` is not in `x`). `/` resolves to the most recent chat, or
creates one.

`npm run build` copies `index.html` to `404.html` so deep links survive a
refresh on GitHub Pages, which has no SPA rewrite.

Type `fail` as a question to exercise the error path — nothing else throws
now that the mock is the only implementation.

## Not built yet

Conversation memory and follow-ups · deleting projects and chats · moving a
chat between projects · search · zoneless change detection (deferred: the
scroll logic is being rewritten anyway) · auth · markdown · charts.

Full plan and open questions: [`docs/web-plan.md`](docs/web-plan.md).
