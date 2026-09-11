using System.Text;
using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace KurumsalRAG.Infrastructure.Services;

/// <summary>
/// IQueryRewriter'ın LLM tabanlı implementasyonu. Takip sorusunu, konuşma geçmişi ışığında
/// bağımsız (standalone) tam soruya çevirir → retrieval doğru chunk'ı bulur. Geçmiş boşsa
/// (ilk soru) LLM'e HİÇ gitmez, soruyu aynen döndürür (maliyet/latency yok).
/// </summary>
public sealed class LlmQueryRewriter : IQueryRewriter
{
    private readonly ILlmProvider _llm;
    private readonly ILogger<LlmQueryRewriter> _logger;

    public LlmQueryRewriter(ILlmProvider llm, ILogger<LlmQueryRewriter> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<string> RewriteAsync(
        string question,
        IReadOnlyList<ConversationTurn> history,
        CancellationToken cancellationToken = default)
    {
        // Geçmiş yoksa yeniden yazmaya gerek yok (soru zaten bağımsız).
        if (history.Count == 0)
            return question;

        var historyBlock = new StringBuilder();
        foreach (var t in history)
            historyBlock.Append("Kullanıcı: ").Append(t.Question).Append('\n')
                        .Append("Asistan: ").Append(t.Answer).Append('\n');

        var system = """
            Görevin: Kullanıcının SON sorusunu, konuşma geçmişini kullanarak tek başına anlaşılır,
            BAĞIMSIZ bir soruya dönüştürmek. Zamiri/eksik bağlamı geçmişten tamamla
            (ör. "peki ya 5 yıl?" → "5 yılını dolduran çalışanlar için yıllık izin kaç gündür?").
            SADECE yeniden yazılmış soruyu döndür — açıklama, tırnak veya ek metin YOK.
            Soru zaten bağımsızsa aynen bırak.
            """;

        var user = $"""
            KONUŞMA GEÇMİŞİ:
            {historyBlock}
            SON SORU: {question}

            BAĞIMSIZ SORU:
            """;

        try
        {
            var completion = await _llm.CompleteAsync(
                [ChatMessage.System(system), ChatMessage.User(user)], cancellationToken);

            var rewritten = completion.Content.Trim();
            // Boş/anlamsız dönerse orijinale düş (retrieval yine de çalışsın).
            if (string.IsNullOrWhiteSpace(rewritten))
                return question;

            _logger.LogInformation("Query rewrite: \"{Orig}\" → \"{Rewritten}\"", question, rewritten);
            return rewritten;
        }
        catch (Exception ex)
        {
            // Rewrite başarısız olursa (ör. 429) sessizce orijinal soruyla devam et.
            _logger.LogWarning(ex, "Query rewrite başarısız; orijinal soruyla devam ediliyor.");
            return question;
        }
    }
}
