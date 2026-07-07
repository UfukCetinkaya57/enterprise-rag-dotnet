using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Application.Configuration;
using KurumsalRAG.Domain.ValueObjects;
using Microsoft.Extensions.Options;
using Npgsql;

namespace KurumsalRAG.Infrastructure.Services;

/// <summary>
/// Rerank öncesi/sonrası sıralamayı yan yana üretir. Doküman adını da getirmek için
/// pgvector'a doğrudan (source-aware) bir sorgu atar — böylece hangi adayın hangi
/// dokümandan geldiği görünür olur.
/// </summary>
public sealed class RerankDiagnosticsService : IRerankDiagnostics
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IEmbeddingProvider _embeddings;
    private readonly IReranker _reranker;
    private readonly RagOptions _options;

    public RerankDiagnosticsService(
        NpgsqlDataSource dataSource,
        IEmbeddingProvider embeddings,
        IReranker reranker,
        IOptions<RagOptions> options)
    {
        _dataSource = dataSource;
        _embeddings = embeddings;
        _reranker = reranker;
        _options = options.Value;
    }

    public async Task<RerankComparison> CompareAsync(string question, CancellationToken cancellationToken = default)
    {
        var queryEmbedding = await _embeddings.EmbedAsync(question, cancellationToken);

        // Source-aware retrieval (documents JOIN) — cosine sırasında.
        var candidates = await SearchWithSourceAsync(queryEmbedding, _options.Retrieval.TopK, cancellationToken);

        var before = candidates
            .Select((c, i) => ToRankedItem(i + 1, c))
            .ToArray();

        // ScoredChunk'a çevirip rerank et.
        var scored = candidates.Select(c => new ScoredChunk(
            new Domain.Entities.DocumentChunk
            {
                Id = c.ChunkId,
                Content = c.Content
            }, c.Score)).ToArray();

        var reranked = await _reranker.RerankAsync(question, scored, _options.Retrieval.TopN, cancellationToken);

        // Rerank sonrası her chunk'ın kaynağını eşlemek için id->source haritası.
        var sourceById = candidates.ToDictionary(c => c.ChunkId, c => (c.Source, c.Content));
        var after = reranked
            .Select((c, i) =>
            {
                var (source, content) = sourceById.TryGetValue(c.Chunk.Id, out var v) ? v : ("?", c.Chunk.Content);
                return new RankedItem(i + 1, c.Chunk.Id, c.Score, source, Snippet(content));
            })
            .ToArray();

        return new RerankComparison(question, before, after);
    }

    private static RankedItem ToRankedItem(int rank, SourcedCandidate c)
        => new(rank, c.ChunkId, c.Score, c.Source, Snippet(c.Content));

    private static string Snippet(string content)
    {
        var normalized = content.Replace('\n', ' ').Trim();
        return normalized.Length > 90 ? normalized[..90] + "…" : normalized;
    }

    private async Task<IReadOnlyList<SourcedCandidate>> SearchWithSourceAsync(
        float[] queryEmbedding, int topK, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT c.id, c.content, d.file_name, 1 - (c.embedding <=> @query) AS score
            FROM chunks c
            JOIN documents d ON d.id = c.document_id
            ORDER BY c.embedding <=> @query
            LIMIT @topk
            """;

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("query", new Pgvector.Vector(queryEmbedding));
        cmd.Parameters.AddWithValue("topk", topK);

        var list = new List<SourcedCandidate>(topK);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new SourcedCandidate(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetDouble(3)));
        }
        return list;
    }

    private sealed record SourcedCandidate(Guid ChunkId, string Content, string Source, double Score);
}
