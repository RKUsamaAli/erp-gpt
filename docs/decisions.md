# Decisions

Settled questions stay settled; open ones are named so they get answered
once, on purpose, instead of repeatedly by accident.

## Settled

**D1 — RAG embeds metadata, not business data.**
Source: Kashif (chat, twice) + Mudassir roadmap steps 2–4 + principle 7.
The knowledge base contains schema metadata, endpoint capabilities,
terminology, and example questions. Business data lives only behind the
API and is fetched live per query. The call summary's "convert existing
data into embeddings" meant system metadata.

**D2 — The model outputs a Query Plan, not GraphQL.**
Source: roadmap step 6. Flat JSON plan → deterministic
GraphQLRequestBuilder → API. Easier target for an 8B model, field-by-field
validatable, suits constrained decoding. Supersedes the original
direct-GraphQL design in early repo drafts.

**D3 — Validator is deterministic code, never an LLM.**
Source: roadmap step 7, principle 2. Outputs EXECUTE / CLARIFICATION /
UNSUPPORTED. No unvalidated model output is ever executed.

**D4 — LoRA is last, gated on measured RAG failures.**
Source: roadmap steps 17–19, principle 10; training/README.md. Compare
base → base+RAG → base+RAG+LoRA. Training data comes from validated audit
logs, never invented examples.

**D5 — Aggregations are API capabilities, not model compositions.**
The plan may *request* an aggregation the API supports; the computation
lives in tested C#.

**D6 — App decides whether clarification is needed; LLM only phrases it.**
Source: roadmap step 8. Triggers: validator says CLARIFICATION, or
retrieval similarity below floor (~0.5).

**D7 — User memory is allow-listed.**
Source: roadmap step 12. Model proposes SAVE_PREFERENCE; validator checks
type and value against an allow-list. Keys touching auth, roles, company
scope are forbidden by construction.

## Open — needs a named owner and a date

**O1 — MCP vs Query Plan → RESOLVED (10 Aug call): no MCP this phase.**
MCP's role was clarified as external actions (emails, notifications) and
explicitly excluded from the current phase for simplicity. The orchestrator
follows the roadmap's Query Plan → GraphQL builder design. Revisit MCP when
action-taking features are scoped.

**O2 — Who owns the Metadata Generator (roadmap step 2)?**
Auto-extracts tables, FKs, types, GraphQL operations from the live system
into KB foundation files. Naturally sits with the API owners (Hammad,
Usama, Nazim). Note: it generates only the technical half of the KB —
example_questions must still be written by humans (see kb/README.md).

**O3 — Query Plan schema v1.**
The exact fields, operators, dateRange enums, and aggregation vocabulary.
This is a three-way contract (prompts ↔ validator ↔ builder), so it needs
one owner and a version number. Draft shape in agent/README.md.

**O4 — Postgres vs SQL Server → RESOLVED (10 Aug call): PostgreSQL + pgvector.**
Decisive: pgvector puts the RAG vector store inside the same instance as
the ERP data — one service, one backup. Also free and Docker-identical on
every machine. Implemented in api/ (pgvector/pgvector:pg16 compose image).
Revisit only if existing SQL Server licenses/DBA expertise outweigh this.

**O5 — Demo schema size: 10 entities today, call wants 15–20.**
Close by extending the demo (candidates: Employees, Payments,
PurchaseOrders, Warehouses, Returns, Shipments) or loading a real ERP
export if sourced. Needs an owner and must settle BEFORE schema lock.

**D8 — Retrieval favours recall over precision** (10 Aug call).
Missing relevant KB entries is unacceptable; retrieve generously (top 3–5)
and let the validator/model narrow. Affects eval: measure hit-rate at the
chosen k.
