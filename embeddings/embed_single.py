import sys
import json
from sentence_transformers import SentenceTransformer

def main():
    if len(sys.argv) < 2:
        print(json.dumps([]))
        sys.exit(1)

    text = sys.argv[1]
    model = SentenceTransformer("sentence-transformers/all-MiniLM-L6-v2")
    vector = model.encode(text, normalize_embeddings=True)
    print(json.dumps(vector.tolist()))

if __name__ == "__main__":
    main()
