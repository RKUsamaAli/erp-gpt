-- Enable pgvector extension
CREATE EXTENSION IF NOT EXISTS vector;

-- Table to store endpoint example questions and metadata payloads
CREATE TABLE IF NOT EXISTS endpoint_embeddings (
    id SERIAL PRIMARY KEY,
    endpoint_name VARCHAR(100) NOT NULL,
    question TEXT NOT NULL,
    payload JSONB NOT NULL,
    embedding vector(384)
);

-- Index for fast Cosine similarity search using HNSW
CREATE INDEX IF NOT EXISTS idx_endpoint_embeddings_hnsw 
ON endpoint_embeddings USING hnsw (embedding vector_cosine_ops);
