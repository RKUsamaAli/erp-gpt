# ERP GPT — Architecture

## What this is

A translator between a human question and the ERP database. Someone types
*"which supplier did we spend the most with last quarter?"* and gets an answer —
without SQL, without knowing the schema, without clicking through screens.

## The core design rule

**Only the API knows about data.**

| Layer | Knows | Never knows |
|---|---|---|
| Database | All business data | That an AI exists |
| GraphQL API | How to fetch and aggregate safely | Who is asking or why |
| Knowledge base | What endpoints exist and how to call them | A single row of business data |
| Vector DB | Which docs are similar to which questions | What any of it means |
| Language model | How to turn a question into an API call | The schema, the data, the totals |

The model is deliberately kept ignorant of the database. It cannot leak what it
cannot see, and it cannot corrupt what it cannot reach. Its entire job is: pick
one endpoint, fill in its parameters.

## Why GraphQL endpoints instead of AI-written SQL

Raw SQL means the model can write `DROP TABLE`, or join nine tables wrong and
return garbage that *looks* correct. A fixed set of endpoints is a cage: the
model can only ask for things we've already decided are safe and correct. The
hard queries are written once, in C#, where they can be unit-tested.

**Corollary: aggregations are endpoints, not model output.** "Average order
value by region excluding cancelled orders, month over month" is four things to
get right. That logic lives in `averageOrderValue(groupBy, excludeStatus,
interval, from, to)` — tested C# — not in the model's head.

## Why RAG (and what it is here)

The local model (Llama 3.1 8B) has never seen our API. Unaided, it will invent
endpoint names and parameters — fluently and wrongly.

RAG fixes this by looking up the relevant endpoint docs *at question time* and
pasting them into the prompt. The model copies from a reference we just put in
front of it; it recalls nothing.

**RAG here is not about our data.** The knowledge base contains endpoint
documentation only. Embedding business data would give us a chatbot reciting
stale snapshots; embedding endpoint docs gives us a system that fetches live
data every time.

### Why retrieval at all, when 20 endpoints fit in context

1. It won't stay at 20. A real ERP surface grows to 150+. Retrieval is easy to
   build at 20 and painful to retrofit at 150.
2. More context makes small models *worse*. Twenty endpoints in the prompt is
   twenty chances to pick wrong; three relevant ones is three. Long-context
   "lost in the middle" degradation is real.
3. Latency. An 8B model on local hardware chews through 6k tokens of context
   noticeably slower than 800.

### The retrieval design decision that matters most

**Embed the `example_questions`, one vector per question — not the whole JSON
blob.** A user's question is a question; matching questions against questions
beats matching a question against a technical description. Each endpoint
produces ~5 vectors, all pointing back to the same doc.

This means writing good example questions is the real work of the KB — written
the way sales/finance people actually talk, including Roman-Urdu phrasings,
not developer English.

## Update: Query Plan layer (per team roadmap)

The model does **not** write GraphQL directly. It outputs a structured
Query Plan (flat JSON: entity, filters, dateRange, sort, limit);
deterministic C# converts the plan into the GraphQL request. Easier target
for an 8B model, field-by-field validatable, ideal for constrained
decoding. See `agent/README.md` and decision D2 in `docs/decisions.md`.

The KB gains a generated half: a **Metadata Generator** (roadmap step 2)
auto-extracts schema/relationship/operation metadata from the live system.
Hand-written `example_questions` remain the retrieval driver — they cannot
be generated, because they encode how humans talk, not what the schema
says.

## The validation gate (step 10)

Everything the model produces is untrusted text until it passes:

- [ ] Query parses as valid GraphQL
- [ ] All fields exist in the real schema
- [ ] The requesting user is authorised for these fields
- [ ] Parameters are within safe limits (dates sane, `limit` capped)

On failure: the parse error goes back to the model for **one** retry, then the
system gives up cleanly and tells the user. Errors must be readable —
`"Field 'customerName' does not exist. Did you mean 'name'?"` lets the model
self-correct; a stack trace doesn't.

## Why LoRA is Phase 5, not Phase 1

| | RAG | LoRA/QLoRA |
|---|---|---|
| Teaches | **What** endpoints exist | **How** to behave |
| Changes when | An endpoint is added/edited | Almost never |
| Update cost | Edit JSON, re-embed (minutes) | Retrain (hours + GPU) |
| Fixes | "Picked the wrong endpoint" | "Writes prose instead of GraphQL" |

Rule of thumb: **facts go in RAG, format goes in LoRA.**

The dependency: LoRA training data is real user questions paired with correct
GraphQL. None exist until the Phase 4 loop is logging them (step 13). Training
earlier means inventing fake examples and teaching the model our guesses.

Expected outcome: with 20 endpoints, good retrieval + prompting likely reaches
80%+ accuracy. Phase 5 triggers only if Phase 4 logs show a *format* failure
pattern that prompting and constrained decoding can't fix.

## Known risks

1. **Small local models fumble structured output.** Llama 3.1 8B and Qwen 7B
   have both been observed failing on tool-calling / clean JSON (trailing
   commas, prose wrappers). Mitigations, in order: readable-error retry loop;
   constrained/grammar-based decoding; hosted model fallback for the demo while
   the local path is hardened.
2. **Multi-endpoint questions.** "Compare this quarter to last and show which
   products drove the difference" needs composed calls. v1 answer: make the
   common ones dedicated endpoints. Don't pretend single-shot covers it.
3. **Near-identical endpoints.** `salesByRegion` vs `revenueByRegion` will make
   the model coin-flip. Fix in the KB via `do_not_use_when`, not in the model.
4. **Vague questions.** Below a similarity floor (~0.5), don't call the model —
   ask a clarifying question instead. "Did you mean sales, stock, or cashflow?"
   reads smart; a confident wrong report reads broken.
5. **KB drift.** Someone renames a param in C# and forgets the JSON. CI checks
   that every schema endpoint has a matching KB file (`.github/workflows/`).

## The health metric

`eval/questions.csv` — 50 real questions with the correct endpoint next to
each. One number decides everything:

> For what % of questions does the correct endpoint appear in the top 3
> retrieved?

At 95%, remaining failures are prompting problems. At 60%, no model will save
us — the right answer isn't in the room. This is measured in Phase 3, **before
any LLM is wired in**.


## Source documents

- Group chat (Kashif): "Only API knows about data" — origin of D1.
- Call summary: MCP server, chat caching, token validation.
- Mudassir's implementation roadmap: Query Plan architecture, validator
  outputs, clarification engine, user-memory allow-list, 100–500 question
  eval, A/B/C fine-tuning comparison, dev order. This repo tracks that
  roadmap; conflicts and gaps are logged in docs/decisions.md.
