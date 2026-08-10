"""
Retrieval hit-rate scorer — the health metric for the whole project.

For each question in questions.csv, embeds it, queries the vector DB for the
top-k endpoint docs, and checks whether the expected endpoint appears.

    hit-rate = questions where expected endpoint is in top-k / total

At 95%: remaining failures are prompting problems — proceed to Phase 4.
At 60%: no model will save us — fix the KB example_questions first.

Run in Phase 3, BEFORE any LLM is wired in. Retrieval is testable on its own.

Usage:
    python score_retrieval.py --k 3
    python score_retrieval.py --k 3 --verbose   # show every miss

Requires the vector DB to be populated first:  cd ../embeddings && python ingest.py
"""

import argparse
import csv
import sys
from pathlib import Path

QUESTIONS = Path(__file__).parent / "questions.csv"


def load_questions():
    rows = []
    with open(QUESTIONS, newline="", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            if row["expected_endpoint"] != "AMBIGUOUS":
                rows.append(row)
    return rows


def retrieve(question: str, k: int) -> list[str]:
    """Return the top-k endpoint names for a question.

    TODO (Phase 3): wire to the real stack —
      1. embed `question` with the SAME model used in embeddings/ingest.py
         (e.g. sentence-transformers all-MiniLM-L6-v2 or bge-small-en-v1.5)
      2. query Qdrant / pgvector for k nearest example-question vectors
      3. map hits back to their parent endpoint, dedupe preserving order
    """
    raise NotImplementedError(
        "Wire retrieve() to the embedding model + vector DB (see embeddings/ingest.py)"
    )


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--k", type=int, default=3, help="top-k endpoints to retrieve")
    ap.add_argument("--verbose", action="store_true", help="print every miss")
    args = ap.parse_args()

    questions = load_questions()
    hits, misses = 0, []

    for row in questions:
        try:
            top = retrieve(row["question"], args.k)
        except NotImplementedError as e:
            sys.exit(f"Not wired up yet: {e}")
        if row["expected_endpoint"] in top:
            hits += 1
        else:
            misses.append((row["question"], row["expected_endpoint"], top))

    total = len(questions)
    rate = hits / total * 100
    print(f"\nRetrieval hit-rate @ k={args.k}:  {hits}/{total}  ({rate:.0f}%)\n")

    if rate >= 95:
        print("PASS — remaining failures are prompting problems. Proceed to Phase 4.")
    elif rate >= 80:
        print("OK — usable, but review misses below and sharpen kb/ example_questions.")
    else:
        print("FIX RETRIEVAL FIRST — no model will save this. The answer isn't in the room.")

    if misses and (args.verbose or rate < 95):
        print("\nMisses:")
        for q, expected, got in misses:
            print(f"  Q: {q}\n     expected {expected}, got {got}\n")


if __name__ == "__main__":
    main()
