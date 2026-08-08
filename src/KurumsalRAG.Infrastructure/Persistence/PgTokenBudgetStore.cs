using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Application.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;

namespace KurumsalRAG.Infrastructure.Persistence;

/// <summary>
/// Günlük global token bütçesi bekçisi (ITokenBudgetGuard). Sayaç daily_usage tablosunda
/// tutulur — süreç yeniden başlasa da bugünkü kullanım korunur (restart-dayanıklı).
/// </summary>
public sealed class PgTokenBudgetStore : ITokenBudgetGuard
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly DemoOptions _options;

    public PgTokenBudgetStore(NpgsqlDataSource dataSource, IOptions<DemoOptions> options)
    {
        _dataSource = dataSource;
        _options = options.Value;
    }

    public async Task<BudgetStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT tokens_used FROM daily_usage WHERE usage_date = @date";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("date", DateTime.UtcNow.Date);
        var used = await cmd.ExecuteScalarAsync(cancellationToken) as long? ?? 0L;

        return BudgetStatus.From(used, _options.DailyTokenBudget);
    }

    public async Task RecordUsageAsync(int tokens, CancellationToken cancellationToken = default)
    {
        if (tokens <= 0)
            return;

        // Atomik artış: bugünün satırını upsert et.
        const string sql = """
            INSERT INTO daily_usage (usage_date, tokens_used)
            VALUES (@date, @tokens)
            ON CONFLICT (usage_date) DO UPDATE
                SET tokens_used = daily_usage.tokens_used + EXCLUDED.tokens_used
            """;

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("date", DateTime.UtcNow.Date);
        cmd.Parameters.AddWithValue("tokens", (long)tokens);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
