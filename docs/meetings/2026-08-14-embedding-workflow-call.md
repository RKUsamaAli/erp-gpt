# Meeting notes — embedding & workflow call (14 Aug 2026)

Filed from the call notes. Timestamps are from the recording. Decisions that
change `docs/decisions.md` are listed at the bottom under *Decisions to
ratify* — they are **not** yet recorded there.

## Knowledge base embedding

- **Embedding created from sample top customers, products, max-value orders** (00:10)
  - JSON file format
  - Embedding string mechanism discussed
- **Semantic match on embedding strings only** (03:40)
  - Avoid full-object semantic matching
  - → Confirms the existing design. `embeddings/ingest.py` already embeds each
    `example_questions` entry as its own vector and never embeds the whole JSON.
    Consistent with D1 and `docs/architecture.md`.
- **Vector DB fetch returns keys; full objects fetched separately** (04:27)
  - Index columns used for matching
  - Include columns fetched but not matched
  - → Partly already true, partly a change. `endpoint_embeddings` stores the
    full doc inline in a `payload JSONB` column and returns it in the *same*
    query — only `embedding` participates in matching. "Fetched separately"
    would be a different shape (see **O6**).
- **Separate DB for metadata if the vector DB cannot include it** (05:14)
  - Admin UI for CRUD on metadata
  - Sync process to update vector DB embeddings
  - → New scope. An admin UI for KB metadata is not in the roadmap; it also
    changes who owns KB authorship (currently `kb/*.json` in git, reviewed by PR).

## System architecture and workflow

- **UI sends string for semantic search, returns scored matches** (09:10)
  - Select top 2–3 matches based on score
  - → Needs the dedupe decision in **O7**: the current query returns the top-k
    *questions*, which can all belong to one endpoint.
- **AI model extracts structured preload from matched metadata** (09:23)
  - Preload formatted as GraphQL object
- **Local AI model setup tested for inference** (09:51)
  - Free APIs considered for inference to reduce resource load
- **Vector DB lightweight model runs in app memory** (11:18)
  - Docker images submitted for rendering
- **Tasks divided among team for UI, DB, endpoints** (07:19)
  - Collaboration to avoid duplicate effort

## AI inference and validation

- **Validation engine checks request parameters before AI inference** (12:54)
  - Required fields must be present
- **Response validation deferred to a later advanced phase** (12:54)
  - Initially rely on semantic match confidence (>90%)
  - → **Conflicts with D3 and principle 2.** See *Conflicts* below.
- **Validation engine to catch missing params and errors pre-execution** (13:51)
  - Helps with development error troubleshooting
- **Response types: single object, array, clarification questions** (14:57)
  - Follow-up questions included in metadata
  - UI renders buttons for these options
  - → Reverses the 10 Aug call, which deferred clarification handling to a
    later phase. Also means `kb/_schema.json` needs a follow-up-questions field.

## Response formatting and UI rendering

- **Responses returned as JSON** (17:05)
  - UI renders formatted with labels and spacing
- **Single object rendered as a bullet list with bold columns** (17:05)
  - Multi-object rendered as a table
- **Follow-up questions clickable; add to prompt on click** (17:05)
- **Future enhancement: summary values rendered as large tiles** (19:42)
  - Start with simple single-object and array rendering
  - → Matches roadmap step 14 (tables and summaries first, richer output later).

## Deployment and documentation

- **Use free cloud AI APIs to avoid local model resource constraints** (10:57)
  - Local models require GPU and RAM that are not always available
  - → **Reverses the 10 Aug decision and affects roadmap steps 18–19.**
    See *Conflicts* below.
- **Deployment steps for the inference model to be finalised** (21:21)
- **Meeting notes to be saved as HTML files shared via URL** (25:20)
  - Easy public access without login
  - Includes summary and detailed notes
  - → Note: the GitHub Pages site is already public (`"public": true`) even
    though the repo is private. "Public access without login" is achievable
    today, but it is genuinely public, not team-only.
- **Team reminded to communicate task ownership and progress** (07:19)
- **Code conflicts and branch merges discussed briefly at end** (26:03)

## Conflicts with previously settled decisions

These need an explicit call, because code and docs currently assume the
earlier position.

**1. Cloud inference vs local Llama (10:57 vs 10 Aug call, D4).**
The 10 Aug call rejected direct GPT/Claude API integration as "too
simplistic", and the roadmap specifies Llama 3.1 8B via Ollama. Moving to a
hosted API is defensible on resource grounds — and low-risk for data, since
the model only ever sees endpoint documentation and the user's question, never
business rows. But it removes the LoRA/QLoRA path entirely (roadmap steps
18–19, D4), because you cannot fine-tune someone else's hosted model. If we
go hosted, steps 18–19 should be struck rather than left as "phase 5".

**2. Response validation deferred (12:54 vs D3, principle 2).**
D3 states the validator is deterministic code and that no unvalidated model
output is ever executed. Deferring response validation means generated
GraphQL reaches the API unchecked. This is not theoretical: on
`feat/rag-implementation`, `GraphQLValidatorAndBuilder` currently only parses
JSON and concatenates strings, and the query it produces for aggregation
endpoints fails against the live schema with five errors (missing required
`from`, non-existent `take`/`where`/`totalCount`/`items`).

**3. Retrieval confidence is not query validity.**
"Rely on semantic match confidence (>90%)" measures whether the *right
endpoint was retrieved*. It says nothing about whether the model then built a
valid query for it. The two failure modes are independent and need separate
gates. Note also that no similarity floor is implemented today — distances are
computed and printed, never compared against a threshold (D6).

## Decisions to ratify in `docs/decisions.md`

| Ref | Decision needed |
|---|---|
| **D9** | Inference target: local Ollama or hosted API. If hosted, strike steps 18–19. |
| **D10** | Validation scope for v1: request-params only, or request + response. |
| **O6** | Metadata storage: inline `payload JSONB` (current), normalised two-table + join, or a separate metadata DB with a sync job. |
| **O7** | Retrieval returns top-k *endpoints* (dedupe by `endpoint_name`) or top-k *questions* (current behaviour). |
| **O8** | Owner and timeline for the admin CRUD UI, and whether `kb/*.json` in git remains the source of truth. |
| **O3** | Still open from 10 Aug — Query Plan schema v1. Two incompatible shapes now exist in the repo. |

## Open follow-ups

- [ ] Ratify D9 (inference target) — blocks deployment planning and steps 18–19.
- [ ] Ratify O3 (Query Plan schema) — blocks all further agent work.
- [ ] Decide O7 (dedupe) — small change now, hard-to-find bug later.
- [ ] Name an owner for the 15 missing `kb/*.json` files. This is the critical
      path: with one endpoint documented, retrieval has nothing to choose between.
- [ ] Add a follow-up-questions field to `kb/_schema.json` if clarification
      buttons are in scope for v1.
- [ ] Confirm whether "public access without login" for notes is acceptable
      given the repo is private.
