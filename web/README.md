# ERP GPT — web

Angular chat UI for [erp-gpt](../README.md). Ask the ERP a question in plain
language; the answer comes back from live data.

Originally generated with [Angular CLI](https://github.com/angular/angular-cli)
20.2.0; now on Angular 21.2.x. Implementation plan: [`doc/web-plan.md`](doc/web-plan.md).

## How answers get here

The UI never composes a query. It asks a `ChatService` and renders whatever
structured blocks come back — only the API knows about data
([`docs/architecture.md`](../docs/architecture.md)).

```
ChatComponent → CHAT_SERVICE ─┬─ MockChatService   canned ERP answers (today)
                              └─ HttpChatService   throws until the Phase 4
                                                   agent exists (agent/README.md)
```

Swapping implementations is one line in [`src/app/app.config.ts`](src/app/app.config.ts).
Answers are typed `AnswerBlock`s (text · list · table · code), not HTML strings,
so nothing needs a sanitizer bypass.

Try `fail` as a question to exercise the error path.

### Guard rail

This must print nothing — it is the check that the rule above still holds:

```bash
grep -rniE "graphql|bypassSecurityTrustHtml|innerHTML|example\.com" src/ | grep -vE ':[0-9]+:\s*(\*|//|/\*)'
```

## Development server

To start a local development server, run:

```bash
ng serve
```

Once the server is running, open your browser and navigate to `http://localhost:4200/`. The application will automatically reload whenever you modify any of the source files.

## Code scaffolding

Angular CLI includes powerful code scaffolding tools. To generate a new component, run:

```bash
ng generate component component-name
```

For a complete list of available schematics (such as `components`, `directives`, or `pipes`), run:

```bash
ng generate --help
```

## Building

To build the project run:

```bash
ng build
```

This will compile your project and store the build artifacts in the `dist/` directory. By default, the production build optimizes your application for performance and speed.

## Running unit tests

To execute unit tests with the [Karma](https://karma-runner.github.io) test runner, use the following command:

```bash
ng test
```

## Running end-to-end tests

For end-to-end (e2e) testing, run:

```bash
ng e2e
```

Angular CLI does not come with an end-to-end testing framework by default. You can choose one that suits your needs.

## Additional Resources

For more information on using the Angular CLI, including detailed command references, visit the [Angular CLI Overview and Command Reference](https://angular.dev/tools/cli) page.
