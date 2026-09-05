-- Container ilk açılışında bir kez çalışır (docker-entrypoint-initdb.d).
-- pgvector extension + şema + HNSW index + demo zırhı tabloları.
-- NOT: init.sql yalnızca BOŞ volume'de çalışır. Şema değiştiyse: docker compose down -v.

CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS documents (
    id          UUID PRIMARY KEY,
    session_id  TEXT NOT NULL,               -- anonim session izolasyonu ('seed' = örnek doküman)
    file_name   TEXT NOT NULL,
    file_bytes  BIGINT NOT NULL DEFAULT 0,   -- session boyut kotası için
    uploaded_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    chunk_count INT NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_documents_session_id ON documents (session_id);

CREATE TABLE IF NOT EXISTS chunks (
    id           UUID PRIMARY KEY,
    document_id  UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    session_id   TEXT NOT NULL,              -- denormalize: hızlı session filtreli retrieval için
    content      TEXT NOT NULL,
    chunk_index  INT NOT NULL,
    token_count  INT NOT NULL,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),  -- TTL temizliği için
    -- text-embedding-3-small -> 1536 boyut. Model değişirse bu boyut da değişir.
    embedding    vector(1536) NOT NULL
);

-- Cosine benzerliği için HNSW index (yaklaşık en-yakın-komşu, hızlı retrieval).
CREATE INDEX IF NOT EXISTS idx_chunks_embedding_hnsw
    ON chunks USING hnsw (embedding vector_cosine_ops);

CREATE INDEX IF NOT EXISTS idx_chunks_document_id ON chunks (document_id);
CREATE INDEX IF NOT EXISTS idx_chunks_session_id  ON chunks (session_id);
CREATE INDEX IF NOT EXISTS idx_chunks_created_at  ON chunks (created_at);

-- Yanıt cache'i: (session_id + normalize edilmiş soru hash) -> cevap. TTL created_at ile.
CREATE TABLE IF NOT EXISTS response_cache (
    session_id    TEXT NOT NULL,
    question_hash TEXT NOT NULL,
    question      TEXT NOT NULL,
    answer_json   JSONB NOT NULL,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (session_id, question_hash)
);

CREATE INDEX IF NOT EXISTS idx_response_cache_created_at ON response_cache (created_at);

-- Günlük global token bütçesi sayacı (restart'a dayanıklı — DB'de tutulur).
CREATE TABLE IF NOT EXISTS daily_usage (
    usage_date  DATE PRIMARY KEY,
    tokens_used BIGINT NOT NULL DEFAULT 0
);

-- IP başına günlük istek sayaçları (restart'a dayanıklı rate limiting).
-- scope: 'query' | 'upload'. Uygulama süreç yeniden başlasa da pencere korunur.
CREATE TABLE IF NOT EXISTS ip_usage (
    usage_date DATE NOT NULL,
    ip         TEXT NOT NULL,
    scope      TEXT NOT NULL,
    hits       INT  NOT NULL DEFAULT 0,
    PRIMARY KEY (usage_date, ip, scope)
);

CREATE INDEX IF NOT EXISTS idx_ip_usage_date ON ip_usage (usage_date);
