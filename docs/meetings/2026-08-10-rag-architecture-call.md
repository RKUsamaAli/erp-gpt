# Meeting notes — RAG architecture call (10 Aug 2026)

Filed from the call summary. Decisions extracted into `docs/decisions.md`.

## What was decided

1. **Direct GPT/Claude API integration rejected** — too simplistic; users could
   bypass the interface. The system is a specialised pipeline, not a chatbot
   wrapper.
2. **Scope: ERP data only**, focused on the reporting module. The AI agent is
   strictly limited to ERP queries.
3. **Pre-trained reasoning models, no retraining on ERP data** — retraining on
   frequently changing datasets is impractical. (Consistent with the roadmap's
   RAG-first, LoRA-last ordering.)
4. **PostgreSQL + pgvector confirmed** as the database and vector store —
   "suitable for the current use case due to its integration with PostgreSQL."
   → Already implemented: `api/docker-compose.yml` ships `pgvector/pgvector:pg16`.
5. **MCP excluded from the current phase** — MCP's role (external actions the
   model can't do natively, e.g. sending emails/notifications) is out of scope
   for now, kept for simplicity. → Resolves open decision O1.
6. **Retrieval tuned for recall over precision** — missing relevant information
   is unacceptable; the model narrows down from a wider retrieved set. Practical
   consequence: retrieve top-k generously (k=3–5), let validation/model filter.
7. **Phase 2 (not now): clarification questions and follow-up handling.**

## Architecture as described

Angular UI → .NET API (chat history/context) → RAG (embed → vector retrieve →
augment) → AI model reasoning → GraphQL execution → formatted response
(tables/bullets) → UI.

## Data requirement raised

- Demo/seed data deemed insufficient for comprehensive testing; a realistic
  **multi-entity dataset with 15–20 entities** is wanted.
- Current demo schema has **10 entities** — gap of 5–10 entities to close,
  either by extending the demo (e.g. Employees, Payments, PurchaseOrders,
  Warehouses, Returns, Shipments) or by loading a real ERP export if one can
  be sourced.

## Knowledge base guidance (matches existing kb/ design)

- KB defines GraphQL endpoints, entities, fields **and synonyms** so the AI
  maps natural phrasings to data correctly.
- Training data covers query variations and expected behaviour — supplements
  the KB, does not replace it.

## Open follow-ups

- [ ] Source a real ERP dataset, or approve extending the demo schema to
      15–20 entities (owner needed).
- [ ] Confirm entity list before schema lock/migrations.
- [ ] Unassigned work from the call needs owners: Angular UI, .NET AI
      middleware, RAG pipeline, response formatting, clarification engine.
