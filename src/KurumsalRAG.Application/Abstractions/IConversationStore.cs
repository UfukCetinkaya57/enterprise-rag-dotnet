namespace KurumsalRAG.Application.Abstractions;

/// <summary>
/// Oturum bazlı konuşma geçmişi deposu (multi-turn). Her tur bir soru-cevap çiftidir;
/// takip sorularında ("peki ya X?") bağlam bu geçmişten gelir. Session-scoped ve TTL'li
/// (yanıt cache'i gibi). Redis/başka store'a geçişte yalnızca implementasyon değişir.
/// </summary>
public interface IConversationStore
{
    /// <summary>Bir soru-cevap turunu geçmişe ekler.</summary>
    Task AppendAsync(string sessionId, string question, string answer, CancellationToken cancellationToken = default);

    /// <summary>Son <paramref name="count"/> turu kronolojik sırayla (eski → yeni) döndürür.</summary>
    Task<IReadOnlyList<ConversationTurn>> GetRecentAsync(
        string sessionId, int count, CancellationToken cancellationToken = default);
}

/// <summary>Geçmişteki tek bir tur: kullanıcının sorusu + asistanın cevabı.</summary>
public sealed record ConversationTurn(string Question, string Answer);
