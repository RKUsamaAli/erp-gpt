"""
KB ingestion: kb/*.json  →  embeddings  →  vector DB.

Run at build time, and re-run whenever a KB file changes. Idempotent —
wipes and rebuilds the collection (it's ~100 tiny vectors; rebuilding is
cheaper than diffing).

THE DESIGN DECISION THAT MATTERS:
  We embed each example_question as its OWN vector, all pointing back to the
  parent endpoint. We do NOT embed the whole JSON blob — type annotations and
  param metadata are noise that dilutes meaning. Matching a user question
  against example questions beats matching it against a technical description.

Stack (Phase 2 decision, current default):
  - embedding model: sentence-transformers/all-MiniLM-L6-v2 (384-dim, fast, local)
    or BAAI/bge-small-en-v1.5 — pick ONE and use the same in eval/score_retrieval.py
  - vector DB: Qdrant (docker run -p 6333:6333 qdrant/qdrant)
    pgvector also fine if we'd rather stay inside Postgres

Usage:
    pip install sentence-transformers qdrant-client
    python ingest.py
"""

import json
from pathlib import Path

KB_DIR = Path(__file__).parent.parent / "kb"
COLLECTION = "erp_gpt_endpoints"


def load_kb() -> list[dict]:
    """Load every endpoint doc, skipping _schema.json."""
    docs = []
    for f in sorted(KB_DIR.glob("*.json")):
        if f.name.startswith("_"):
            continue
        doc = json.loads(f.read_text(encoding="utf-8"))
        assert doc["endpoint"] == f.stem, f"{f.name}: filename must match endpoint name"
        docs.append(doc)
    return docs


def build_points(docs: list[dict]) -> list[dict]:
    """One point per example_question, payload carries the full parent doc."""
    points = []
    for doc in docs:
        for q in doc["example_questions"]:
            points.append({"text": q, "endpoint": doc["endpoint"], "doc": doc})
    return points


def main():
    docs = load_kb()
    points = build_points(docs)
    print(f"{len(docs)} endpoints → {len(points)} vectors to embed")

    # TODO (Phase 2): wire up —
    # from sentence_transformers import SentenceTransformer
    # from qdrant_client import QdrantClient
    # from qdrant_client.models import Distance, VectorParams, PointStruct
    #
    # model = SentenceTransformer("sentence-transformers/all-MiniLM-L6-v2")
    # client = QdrantClient("localhost", port=6333)
    # client.recreate_collection(
    #     COLLECTION,
    #     vectors_config=VectorParams(size=384, distance=Distance.COSINE),
    # )
    # vectors = model.encode([p["text"] for p in points], normalize_embeddings=True)
    # client.upsert(COLLECTION, points=[
    #     PointStruct(id=i, vector=v.tolist(),
    #                 payload={"endpoint": p["endpoint"], "question": p["text"], "doc": p["doc"]})
    #     for i, (p, v) in enumerate(zip(points, vectors))
    # ])
    print("TODO: wire embedding model + Qdrant (see comments)")


if __name__ == "__main__":
    main()
