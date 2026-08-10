# Evaluation

`questions.csv` — currently 20 seeded rows. **Target per the team roadmap
(step 16): 100–500**, covering: basic queries, filters, dates, aggregations,
relationships, follow-ups, ambiguous questions, and invalid/unsupported
requests. The seeded rows already include ambiguous (row 20) and
gap-detection (row 19) traps; keep adding categories as columns of the same
file.

Two measurements, in order:

1. **Retrieval hit-rate** (`score_retrieval.py`) — was the right context
   even retrieved? Measured in Phase 3, before any LLM. If this is low,
   every downstream metric fails together and you won't know which layer
   broke. Fix KB example_questions first.
2. **End-to-end accuracy** (Phase 4) — per the roadmap: intent, entity,
   field, filter, date interpretation, aggregation, plan validity,
   clarification correctness, execution success. Compare
   base → base+RAG → (only if gated) base+RAG+LoRA.

Follow-up questions ("only active ones", "make it 20") need conversation
state and can only be tested once chat threads exist — mark them with a
`requires_context` note column so Phase 3 scoring skips them.
