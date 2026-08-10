# Training (LoRA / QLoRA)

**This folder is empty on purpose.**

## Why

LoRA training data is real user questions paired with the correct GraphQL
query. We do not have any yet. They are produced by step 13 of the run-time
loop — the logging step — once Phases 1–4 are live and real people are asking
real questions.

Training before that means inventing fake examples and teaching the model our
guesses. That is worse than not training.

## Division of labour

| Concern | Lives in | Update cost |
|---|---|---|
| **What** endpoints exist, their params | RAG knowledge base (`kb/`) | Edit JSON, re-embed — minutes |
| **How** to behave — output GraphQL only, no prose | Prompting first, LoRA if needed | Retrain — hours + GPU |

**Facts go in RAG. Format goes in LoRA.** If the model picks the wrong
endpoint, that is a KB fix, never a training fix.

## The gate for opening this folder

Phase 5 starts only when **all three** are true:

1. Phase 4 is live and step 13 has logged a meaningful volume of real
   question → query → outcome triples.
2. End-to-end accuracy on `eval/questions.csv` is measured, and the failures
   show a **format** pattern (malformed GraphQL, prose wrappers, invented
   syntax) — not an endpoint-selection pattern.
3. Prompt engineering and constrained decoding have been tried against that
   pattern and have plateaued.

Expected: with 20 endpoints, retrieval + good prompting reaches ~80%+ and
this folder stays empty for a long time. That is success, not failure.

## When the gate opens

Format: JSONL, one `{"question": ..., "context": [retrieved docs],
"query": ...}` per line, drawn from step 13 logs with human-verified correct
queries. QLoRA on Llama 3.1 8B, r=16 to start. But that is a bridge for
later.
