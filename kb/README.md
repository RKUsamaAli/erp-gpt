# Endpoint Knowledge Base

**Owners: Zahid, Kashif**

One JSON file per GraphQL endpoint, named exactly after it
(`topCustomers.json` documents `topCustomers`). Every file must validate
against [`_schema.json`](_schema.json). Copy
[`topCustomers.json`](topCustomers.json) as your template.

## The three rules

1. **No business data in these files. Ever.** Endpoint documentation only.
   The KB describes *how to ask*, never *what the answer is*.
2. **Change an endpoint → change its KB file in the same PR.** CI will fail
   the build if a schema endpoint has no matching KB file.
3. **`example_questions` is where the quality lives.** These are what get
   embedded — retrieval matches real user questions against them. Everything
   else in the file is reference material for the model *after* retrieval
   already succeeded.

## Writing good example questions

- Write how sales and finance people actually talk, not developer English.
  "which accounts bring in the most money" — not "retrieve customer entities
  ordered by aggregate revenue".
- 5–8 per endpoint. Vary the vocabulary deliberately: clients / customers /
  accounts; sales / revenue / money.
- Include Roman-Urdu phrasings where natural — users will mix languages and
  the embedding model handles it.
- Best source: ask five people around the office how *they* would ask for
  this, and write down their exact words.

## Disambiguation

When two endpoints could be confused (`salesByRegion` vs `revenueByRegion`),
the `do_not_use_when` field is the fix — name the situation and the endpoint
to use instead. Sharpening these two fields is how most "model picked the
wrong endpoint" bugs get fixed. It is a five-minute KB edit, not a model
problem.
