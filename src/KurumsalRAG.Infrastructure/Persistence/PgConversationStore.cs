using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Application.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;

namespace KurumsalRAG.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL tabanlı konuşma geçmişi (IConversationStore). Anahtar: session_id + artan turn_index.
/// TTL created_at ile (yanıt cache'iyle aynı pencere). Redis'e geçişte yalnızca bu sınıf değişir.
/// </summary>
public sealed class PgConversationStore : IConversationStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly CacheOptions _options;

    public PgConversationStore(NpgsqlDataSource dataSource, IOptions<DemoOptions> options)
    {
        _dataSource = dataSource;
        _options = options.Value.Cache;
    }

    public async Task AppendAsync(
        string sessionId, string question, string answer, CancellationToken cancellationToken = default)
    {
        // turn_index = bu session'daki mevcut max + 1. Aynı session'da eşzamanlı iki istek aynı
        // index'i hesaplayabilir; ON CONFLICT DO NOTHING ile ikinci INSERT sessizce atlanır
        // (exception fırlatmaz). Tek kullanıcı tek session senaryosunda çakışma nadir; bir turun
        // düşmesi kabul edilebilir (cevap zaten kullanıcıya döndü).
        const string sql = """
            INSERT INTO conversation_turns (session_id, turn_index, question, answer, created_at)
            SELECT @session, COALESCE(MAX(turn_index), 0) + 1, @question, @answer, now()
            FROM conversation_turns WHERE session_id = @session
            ON CONFLICT (session_id, turn_index) DO NOTHING
            """;

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("session", sessionId);
        cmd.Parameters.AddWithValue("question", question);
        cmd.Parameters.AddWithValue("answer", answer);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ConversationTurn>> GetRecentAsync(
        string sessionId, int count, CancellationToken cancellationToken = default)
    {
        if (count <= 0)
            return [];

        var cutoff = DateTimeOffset.UtcNow.AddHours(-_options.TtlHours);

        // Son N turu al (turn_index DESC), sonra kronolojik (eski→yeni) sıraya çevir.
        const string sql = """
            SELECT question, answer FROM (
                SELECT question, answer, turn_index FROM conversation_turns
                WHERE session_id = @session AND created_at >= @cutoff
                ORDER BY turn_index DESC LIMIT @count
            ) t ORDER BY turn_index ASC
            """;

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("session", sessionId);
        cmd.Parameters.AddWithValue("cutoff", cutoff);
        cmd.Parameters.AddWithValue("count", count);

        var turns = new List<ConversationTurn>(count);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            turns.Add(new ConversationTurn(reader.GetString(0), reader.GetString(1)));

        return turns;
    }
}
