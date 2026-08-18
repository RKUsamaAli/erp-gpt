# Web - Angular Chat UI

**Owner: unassigned** | Stack: Angular 21, Bootstrap 5.3, TypeScript 5.9, Node 22 | Icons are inline SVG, not an icon font.

The human interface to the ERP. Someone types "who were our biggest customers last quarter?" and the answer appears here: no SQL, no schema knowledge, no clicking through screens.

Live web app: [https://erpgpt-web.vercel.app/](https://erpgpt-web.vercel.app/)

## The One Rule

**Chat components never compose GraphQL.** They hand a question to `ChatService` and render structured answer blocks. For deployed API testing, `HttpChatService` maps known prompts to static GraphQL documents and sends user values through `variables`, never string concatenation.

```text
ChatComponent -> CHAT_SERVICE -> HttpChatService  deployed GraphQL answers
                            -> MockChatService  local canned answers for tests

SidebarComponent -> CHAT_STORE -> LocalChatStore  localStorage today
                             -> ApiChatStore    when ChatThread / ChatTurn land
```

Swapping either implementation is one line in [`src/app/app.config.ts`](src/app/app.config.ts).

> Projects and chats are defined client-side for now. `agent/README.md` plans `ChatThread` / `ChatTurn` entities server-side, so the two shapes will need reconciling. Nothing reaches storage except through `CHAT_STORE`.

Two corollaries:

1. **Answers are data, not markup.** `AnswerBlock` is a typed union: text, list, table, code. Model output is untrusted until it passes the validation gate, so there is no `bypassSecurityTrustHtml` anywhere.
2. **Bootstrap carries layout, not identity.** Grid, utilities, offcanvas and form controls come from Bootstrap; bubbles, avatars, gradients and streaming behavior stay in component SCSS.

## Run It

```bash
npm install
npm start          # http://localhost:4200
```

Node must be 20.19+, 22.12+ or 24+. `engines` in `package.json` fails the install otherwise.

| Do | Command |
|---|---|
| Dev server | `npm start` |
| Production build | `npm run build` -> `dist/chat-app/browser` |
| Unit tests | `npm test` |

The build output folder is `chat-app`, the Angular project name, not `web`, the folder name.

## API URL

Local environment config lives in [`src/environments/environment.ts`](src/environments/environment.ts):

```ts
apiUrl: 'https://erpgpt-api-xh5w.onrender.com'
```

Production config lives in [`src/environments/environment.prod.ts`](src/environments/environment.prod.ts). It uses the same deployed API URL.

`HttpChatService` uses the configured base URL for the deployed GraphQL endpoint:

```text
${environment.apiUrl}/graphql
```

For local API testing, change `apiUrl` to `http://localhost:5000`; the frontend will call `${environment.apiUrl}/graphql`.

## Guard Rail

This should only show intentional GraphQL usage inside `HttpChatService` and comments/docs. It should not show raw HTML rendering or component-level query construction:

```bash
grep -rniE "bypassSecurityTrustHtml|innerHTML|example\.com" src/
```

## Layout

```text
src/app/
  core/
    chat-service.ts       interface + CHAT_SERVICE token
    mock-chat-service.ts  canned answers and simulated streaming
    http-chat-service.ts  deployed GraphQL implementation
    chat-store.ts         interface + CHAT_STORE token
    local-chat-store.ts   projects/chats in localStorage
    theme.service.ts      data-bs-theme on html
    layout.service.ts     mobile drawer + desktop collapse state
  models/
    chat.models.ts        AnswerBlock, AnswerChunk, appendChunk
    workspace.models.ts   Project, Chat
  shell/
    sidebar/              projects, chats, new, rename, archive, remove
    rename-field/         inline rename shared by projects and chats
  chat/
    chat.component.*      thread, composer, streaming state
    answer-block/         renders one AnswerBlock
    theme-toggle/         light/dark switch
```

## Sidebar And Chat UX

- Desktop sidebar supports expanded and collapsed rail states.
- Mobile sidebar behaves as an overlay drawer with a close button.
- Projects and chats can be renamed, archived, and removed.
- Long project/chat names truncate before action buttons.
- The chat page uses a full-screen shell with a centered conversation lane, matching the ChatGPT-style reading layout.

## Theming

`data-bs-theme` on `<html>` drives both this app's tokens (`src/styles.scss`) and Bootstrap 5.3's own tokens. Components should use theme variables instead of hardcoded colors wherever practical.

The choice follows the operating system until the toggle is used, then sticks. An inline script in `index.html` resolves it before first paint.

## Routing

A chat is the only addressable thing: `/chat/:chatId`. A chat's project is a property of the chat, so the URL does not repeat it. `/` resolves to the most recent chat, or creates one.

`npm run build` copies `index.html` to `404.html` so deep links survive a refresh on GitHub Pages.

The deployed API can sleep after idle on the free Render plan, so the first real request may take around a minute.

## Not Built Yet

Conversation memory and follow-ups | moving a chat between projects | search | zoneless change detection | auth | markdown | charts.
