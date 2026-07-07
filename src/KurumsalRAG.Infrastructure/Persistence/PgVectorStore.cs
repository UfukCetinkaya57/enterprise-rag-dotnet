using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Domain.Entities;
using KurumsalRAG.Domain.ValueObjects;
using KurumsalRAG.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector;

namespace KurumsalRAG.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL + pgvector adapter'ı (IVectorStore). Cosine benzerliği (&lt;=&gt; operatörü)
/// ile top-k arama. Qdrant/Milvus/Azure AI Search'e geçişte sadece bu sınıf değişir.
/// </summary>
public sealed class PgVectorStore : IVectorStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PgVectorStore(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    public async Task SaveDocumentAsync(DocumentEntity document, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO documents (id, file_name, uploaded_at, chunk_count)
            VALUES (@id, @file_name, @uploaded_at, @chunk_count)
            ON CONFLICT (id) DO UPDATE SET chunk_count = EXCLUDED.chunk_count
            """;

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", document.Id);
        cmd.Parameters.AddWithValue("file_name", document.FileName);
        cmd.Parameters.AddWithValue("uploaded_at", document.UploadedAt);
        cmd.Parameters.AddWithValue("chunk_count", document.ChunkCount);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertChunksAsync(
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
            return;

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);

        // Toplu yazım: binary COPY yerine basit ve okunur bir batch INSERT.
        const string sql = """
            INSERT INTO chunks (id, document_id, content, chunk_index, token_count, embedding)
            VALUES (@id, @document_id, @content, @chunk_index, @token_count, @embedding)
            ON CONFLICT (id) DO UPDATE
                SET content = EXCLUDED.content, embedding = EXCLUDED.embedding
            """;

        await using var tx = await conn.BeginTransactionAsync(cancellationToken);
        foreach (var chunk in chunks)
        {
            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("id", chunk.Id);
            cmd.Parameters.AddWithValue("document_id", chunk.DocumentId);
            cmd.Parameters.AddWithValue("content", chunk.Content);
            cmd.Parameters.AddWithValue("chunk_index", chunk.ChunkIndex);
            cmd.Parameters.AddWithValue("token_count", chunk.TokenCount);
            cmd.Parameters.AddWithValue("embedding", new Vector(chunk.Embedding));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ScoredChunk>> SearchAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken = default)
    {
        // <=> = cosine distance (0=aynı yön). Skor = 1 - distance => yüksek = daha alakalı.
        const string sql = """
            SELECT id, document_id, content, chunk_index, token_count,
                   1 - (embedding <=> @query) AS score
            FROM chunks
            ORDER BY embedding <=> @query
            LIMIT @topk
            """;

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("query", new Vector(queryEmbedding));
        cmd.Parameters.AddWithValue("topk", topK);

        var results = new List<ScoredChunk>(topK);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var chunk = new DocumentChunk
            {
                Id = reader.GetGuid(0),
                DocumentId = reader.GetGuid(1),
                Content = reader.GetString(2),
                ChunkIndex = reader.GetInt32(3),
                TokenCount = reader.GetInt32(4)
                // Embedding retrieval'da geri okunmuyor (gereksiz ağırlık).
            };
            results.Add(new ScoredChunk(chunk, reader.GetDouble(5)));
        }
        return results;
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var cmd = _dataSource.CreateCommand("SELECT 1");
            await cmd.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
