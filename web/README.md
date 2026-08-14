# Web — Angular chat UI

**Owner: unassigned** · Stack: Angular 21, Bootstrap 5.3, TypeScript 5.9,
Node 22

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
```

Swapping implementations is one line in [`src/app/app.config.ts`](src/app/app.config.ts).

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
├── core/                    the seam
│   ├── chat-service.ts        interface + CHAT_SERVICE token
│   ├── mock-chat-service.ts   canned answers, two-speed streaming
│   └── http-chat-service.ts   stub, no providedIn — stays tree-shaken
├── models/chat.models.ts    AnswerBlock · AnswerChunk · ChatMessage
└── chat/
    ├── chat.component.*     thread, composer, streaming state
    └── answer-block/        renders one AnswerBlock
```

Type `fail` as a question to exercise the error path — nothing else throws
now that the mock is the only implementation.

## Not built yet

Sidebar and thread list · conversation memory and follow-ups · responsive
breakpoints below `md` · zoneless change detection (deferred: the scroll logic
is being rewritten anyway) · auth · markdown · charts.

Full plan and open questions: [`docs/web-plan.md`](docs/web-plan.md).
