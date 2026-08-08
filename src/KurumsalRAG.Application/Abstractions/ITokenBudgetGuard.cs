namespace KurumsalRAG.Application.Abstractions;

/// <summary>
/// Günlük global token bütçesi bekçisi. Sayaç restart'a dayanıklı biçimde (DB'de) tutulur.
/// Bütçe aşımında LLM çağrısı yapılmamalıdır.
/// </summary>
public interface ITokenBudgetGuard
{
    /// <summary>Bugünkü kullanım bütçenin altında mı? (LLM çağrısından önce kontrol edilir.)</summary>
    Task<BudgetStatus> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>Harcanan token'ı bugünün sayacına ekler (LLM çağrısından sonra).</summary>
    Task RecordUsageAsync(int tokens, CancellationToken cancellationToken = default);
}

/// <param name="WithinBudget">Bugünkü kullanım bütçenin altında mı.</param>
/// <param name="UsedToday">Bugüne kadar harcanan token.</param>
/// <param name="DailyBudget">Günlük bütçe.</param>
public sealed record BudgetStatus(bool WithinBudget, long UsedToday, long DailyBudget)
{
    /// <summary>Saf karar mantığı: kullanım bütçenin altındaysa izin ver (test edilebilir).</summary>
    public static BudgetStatus From(long usedToday, long dailyBudget)
        => new(usedToday < dailyBudget, usedToday, dailyBudget);
}
