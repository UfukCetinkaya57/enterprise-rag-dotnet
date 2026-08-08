using System.Text.Json;
using System.Threading;
using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Application.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;

namespace KurumsalRAG.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL tabanlı yanıt cache'i (IResponseCache). Anahtar: (session_id + normalize soru hash).
/// TTL created_at ile uygulanır. Cache hit'te LLM'e gidilmez. Redis'e geçişte sadece bu sınıf değişir.
/// </summary>
public sealed class PgResponseCache : IResponseCache
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly CacheOptions _options;
    private long _hitCount;

    public PgResponseCache(NpgsqlDataSource dataSource, IOptions<DemoOptions> options)
    {
        _dataSource = dataSource;
        _options = options.Value.Cache;
    }

    public long HitCount => Interlocked.Read(ref _hitCount);

    public async Task<CachedAnswer?> GetAsync(
        string sessionId, string question, CancellationToken cancellationToken = default)
    {
        var hash = QuestionNormalizer.Hash(question);
        var cutoff = DateTimeOffset.UtcNow.AddHours(-_options.TtlHours);

        const string sql = """
            SELECT answer_json FROM response_cache
            WHERE session_id = @session AND question_hash = @hash AND created_at >= @cutoff
            """;

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("session", sessionId);
        cmd.Parameters.AddWithValue("hash", hash);
        cmd.Parameters.AddWithValue("cutoff", cutoff);

        var json = await cmd.ExecuteScalarAsync(cancellationToken) as string;
        if (json is null)
            return null;

        var cached = JsonSerializer.Deserialize<CachedAnswer>(json);
        if (cached is not null)
            Interlocked.Increment(ref _hitCount);
        return cached;
    }

    public async Task SetAsync(
        string sessionId, string question, CachedAnswer answer, CancellationToken cancellationToken = default)
    {
        var hash = QuestionNormalizer.Hash(question);
        var json = JsonSerializer.Serialize(answer);

        // Aynı anahtar tekrar sorulursa cevabı ve zamanı tazele (TTL yeniden başlar).
        const string sql = """
            INSERT INTO response_cache (session_id, question_hash, question, answer_json, created_at)
            VALUES (@session, @hash, @question, @json::jsonb, now())
            ON CONFLICT (session_id, question_hash) DO UPDATE
                SET answer_json = EXCLUDED.answer_json, question = EXCLUDED.question, created_at = now()
            """;

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("session", sessionId);
        cmd.Parameters.AddWithValue("hash", hash);
        cmd.Parameters.AddWithValue("question", question);
        cmd.Parameters.AddWithValue("json", json);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
