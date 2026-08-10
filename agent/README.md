# Agent — the run-time loop

**Phase 4.** Prompt assembly, model call, validation, execution, answer,
logging. Language: C# (one runtime with the API), per the team roadmap.

## Architecture: Query Plan, not direct GraphQL

Per Mudassir's roadmap (step 6): the model does NOT write GraphQL. It writes
a structured Query Plan; deterministic code converts the plan to GraphQL.

    User question
        ↓
    Context Resolver  (RAG docs + prefs + active-query context)
        ↓
    Llama 3.1 8B  →  Query Plan JSON
        ↓
    Validator / Decision Engine  →  EXECUTE | CLARIFICATION | UNSUPPORTED
        ↓ (EXECUTE)
    GraphQL Request Builder  →  GraphQL API  →  rows
        ↓
    Answer rendering  +  full audit log

Why: flat JSON with a fixed schema is a far easier target for an 8B model
than GraphQL syntax, is field-by-field validatable, and suits constrained
decoding. The model expresses INTENT; code constructs the query.

## Query Plan shape (v1)

    {
      "entity": "Customer",
      "filters": [ {"field": "City", "operator": "EQ", "value": "Riyadh"} ],
      "dateRange": "CURRENT_MONTH",
      "aggregation": null,
      "sort": {"field": "Sales", "direction": "DESC"},
      "limit": 10
    }

This schema is a CONTRACT (like kb/_schema.json). Version it. Changing it
invalidates prompts, validator, builder, and eval expectations together.

## Components, in build order

1. **ContextResolver** — retrieval (top-3 KB docs via vector DB) + user
   prefs + active-query context. Similarity floor: best match < 0.5 →
   route to clarification, don't call the model.
2. **LlamaService** — Ollama client. One interface so hosted-model fallback
   is a config flag. Constrained decoding (GBNF grammar for the plan
   schema) as soon as raw output proves unreliable.
3. **Validator / Decision Engine** — deterministic, NOT an LLM. Checks
   entity, fields, operators, params, date ranges, aggregations, limits
   against supported capabilities. Outputs EXECUTE, CLARIFICATION, or
   UNSUPPORTED. Never execute an unvalidated plan. On malformed plan:
   readable error back to the model, one retry, then fail cleanly.
4. **ClarificationEngine** — app decides IF clarification is needed
   (validator said so, or similarity floor); LLM only phrases the question
   and options.
5. **GraphQLRequestBuilder** — plan → GraphQL string. Deterministic,
   unit-tested, boring.
6. **Executor** — POST with the user's auth token. Fresh data every time.
7. **AnswerRenderer** — rows → sentence/table. Summarises ONLY what the
   API returned.
8. **AuditLog** — per turn: prompt, retrieved context, memory used, model
   output, plan, validation result, GraphQL, execution result, timing,
   errors. This log feeds eval, KB fixes, and (Phase 5, gated) LoRA data.

## Later (roadmap steps 9–12): chat threads & memory

- ChatThread / ChatTurn / QueryExecution / QueryResult entities;
  ActiveQueryId + ParentQueryExecutionId for follow-up lineage
  ("only active ones", "make it 20").
- User preferences: model PROPOSES {intent: SAVE_PREFERENCE, ...}; a
  validator checks against an ALLOW-LIST before saving. Forbidden keys
  (bypass_authorization, change_user_role, ...) rejected outright.

## Open decision — flagged, not resolved

MCP server (from the call) vs Query Plan builder (from the roadmap) are two
mechanisms for the same job: constraining how the model invokes the API.
Pick one, or nest the plan inside a single MCP tool. Do not build both
independently. See docs/decisions.md.
