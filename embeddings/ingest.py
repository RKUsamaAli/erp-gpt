"""
KB ingestion: kb/*.json  →  all-MiniLM-L6-v2 embeddings  →  pgvector (PostgreSQL).

Run at build time, and re-run whenever a KB file changes. Idempotent —
wipes and rebuilds the endpoint_embeddings table.

Stack:
  - embedding model: sentence-transformers/all-MiniLM-L6-v2 (384-dim, fast, local)
  - vector DB: pgvector extension in Postgres (erpgpt database)

Usage:
    pip install sentence-transformers psycopg2-binary pgvector
    python ingest.py
"""

import json
import os
from pathlib import Path
# pyrefly: ignore [missing-import]
from sentence_transformers import SentenceTransformer
import psycopg2
# pyrefly: ignore [missing-import]
from pgvector.psycopg2 import register_vector

KB_DIR = Path(__file__).parent.parent / "kb"
DB_HOST = os.getenv("DB_HOST", "localhost")
DB_PORT = os.getenv("DB_PORT", "5432")
DB_NAME = os.getenv("DB_NAME", "erpgpt")
DB_USER = os.getenv("DB_USER", "erpgpt")
DB_PASS = os.getenv("DB_PASS", "devonly")


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
    print(f"Loaded {len(docs)} endpoint docs → {len(points)} question vectors to embed.")

    print("Loading embedding model 'sentence-transformers/all-MiniLM-L6-v2'...")
    model = SentenceTransformer("sentence-transformers/all-MiniLM-L6-v2")

    texts = [p["text"] for p in points]
    vectors = model.encode(texts, normalize_embeddings=True)

    print(f"Connecting to PostgreSQL pgvector database ({DB_HOST}:{DB_PORT}/{DB_NAME})...")
    conn = psycopg2.connect(
        host=DB_HOST,
        port=DB_PORT,
        dbname=DB_NAME,
        user=DB_USER,
        password=DB_PASS
    )
    conn.autocommit = True
    cur = conn.cursor()

    cur.execute("CREATE EXTENSION IF NOT EXISTS vector;")
    register_vector(conn)

    cur.execute("""
        CREATE TABLE IF NOT EXISTS endpoint_embeddings (
            id SERIAL PRIMARY KEY,
            endpoint_name VARCHAR(100) NOT NULL,
            question TEXT NOT NULL,
            payload JSONB NOT NULL,
            embedding vector(384)
        );
    """)

    print("Trimming existing vectors for clean ingestion...")
    cur.execute("TRUNCATE TABLE endpoint_embeddings;")

    print("Inserting vector embeddings into pgvector...")
    for p, vec in zip(points, vectors):
        cur.execute(
            """
            INSERT INTO endpoint_embeddings (endpoint_name, question, payload, embedding)
            VALUES (%s, %s, %s, %s);
            """,
            (p["endpoint"], p["text"], json.dumps(p["doc"]), vec.tolist())
        )

    print("Creating HNSW vector index...")
    cur.execute("""
        CREATE INDEX IF NOT EXISTS idx_endpoint_embeddings_hnsw 
        ON endpoint_embeddings USING hnsw (embedding vector_cosine_ops);
    """)

    cur.close()
    conn.close()
    print("✅ Ingestion complete! Successfully stored vectors in pgvector.")


if __name__ == "__main__":
    main()
