-- Container ilk açılışında bir kez çalışır (docker-entrypoint-initdb.d).
-- pgvector extension + şema + HNSW index'i kurar.

CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS documents (
    id          UUID PRIMARY KEY,
    file_name   TEXT NOT NULL,
    uploaded_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    chunk_count INT NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS chunks (
    id           UUID PRIMARY KEY,
    document_id  UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    content      TEXT NOT NULL,
    chunk_index  INT NOT NULL,
    token_count  INT NOT NULL,
    -- text-embedding-3-small -> 1536 boyut. Model değişirse bu boyut da değişir.
    embedding    vector(1536) NOT NULL
);

-- Cosine benzerliği için HNSW index (yaklaşık en-yakın-komşu, hızlı retrieval).
CREATE INDEX IF NOT EXISTS idx_chunks_embedding_hnsw
    ON chunks USING hnsw (embedding vector_cosine_ops);

CREATE INDEX IF NOT EXISTS idx_chunks_document_id ON chunks (document_id);
