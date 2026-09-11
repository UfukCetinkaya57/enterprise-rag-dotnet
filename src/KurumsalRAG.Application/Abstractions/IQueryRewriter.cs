namespace KurumsalRAG.Application.Abstractions;

/// <summary>
/// Takip sorularını, konuşma geçmişini kullanarak "bağımsız (standalone) tam soru"ya çevirir.
/// Örn. geçmiş "Yıllık izin kaç gün? → 20 gün" iken "peki ya 5 yılını dolduranlar?" sorusu
/// "5 yılını dolduran çalışanlar için yıllık izin kaç gündür?" olur — böylece retrieval doğru
/// chunk'ı bulur. Geçmiş boşsa (ilk soru) soru DEĞİŞMEZ ve LLM çağrısı YAPILMAZ (maliyet/latency).
/// </summary>
public interface IQueryRewriter
{
    /// <summary>
    /// Soruyu, verilen geçmiş ışığında bağımsız hale getirir. Geçmiş boşsa soruyu aynen döndürür.
    /// </summary>
    Task<string> RewriteAsync(
        string question,
        IReadOnlyList<ConversationTurn> history,
        CancellationToken cancellationToken = default);
}
