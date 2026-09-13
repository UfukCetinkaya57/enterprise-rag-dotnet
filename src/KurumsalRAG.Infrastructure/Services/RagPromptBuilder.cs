using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Domain.ValueObjects;

namespace KurumsalRAG.Infrastructure.Services;

/// <summary>
/// RAG system prompt'unu kurar. Context'i net delimiter'larla kullanıcı sorusundan
/// ayırır (prompt injection yüzeyini daraltır) ve kaynak-atıf ([chunk:N]) talep eder.
/// Multi-turn: geçmiş turlar system'den SONRA, context'li son sorudan ÖNCE User/Assistant
/// mesajları olarak eklenir — model önceki bağlamı görür ama context yine yalıtılmış kalır.
/// </summary>
public static class RagPromptBuilder
{
    public const string RefusalText = "Sağlanan dokümanlarda bu bilgi bulunmuyor.";

    /// <summary>
    /// Cevap standart "dokümanlarda yok" reti mi? Ret grounded bir davranıştır (iddia değil),
    /// bu yüzden faithfulness denetiminden ve reflection'dan muaftır. Tek kaynak — RagQueryService
    /// ve RagOrchestrator ortak kullanır (kural tek yerde kalsın).
    /// </summary>
    public static bool IsRefusal(string answer)
        => answer.Contains(RefusalText, StringComparison.OrdinalIgnoreCase);

    /// <summary>Geçmişsiz (tek-tur) kısayol — mevcut çağıranlarla uyumluluk için.</summary>
    /// <summary>
    /// Reflection/self-correction retry'ında eklenen katı talimat: ilk cevap desteklenmeyen iddia
    /// içerdi → bu kez yalnızca context'te açıkça yazana sadık kal.
    /// </summary>
    public const string StrictInstruction =
        "UYARI: Önceki cevap context'te tam desteklenmeyen iddialar içeriyordu. Bu kez SADECE " +
        "aşağıdaki CONTEXT'te açıkça yazan bilgiyi kullan; emin olmadığın hiçbir şeyi ekleme.";

    public static IReadOnlyList<ChatMessage> Build(string question, RetrievedContext context)
        => Build(question, context, []);

    /// <summary>Strict (self-correction) modda: context'e sadakat için ek bir katı system talimatı ekler.</summary>
    public static IReadOnlyList<ChatMessage> BuildStrict(
        string question, RetrievedContext context, IReadOnlyList<ConversationTurn>? history = null)
    {
        var messages = new List<ChatMessage>(Build(question, context, history ?? []))
        {
            ChatMessage.System(StrictInstruction)
        };
        return messages;
    }

    public static IReadOnlyList<ChatMessage> Build(
        string question, RetrievedContext context, IReadOnlyList<ConversationTurn> history)
    {
        var system = $"""
            Sen bir kurumsal doküman asistanısın. SADECE aşağıdaki CONTEXT'e dayanarak cevap ver.
            Context'te cevap yoksa "{RefusalText}" de. Uydurma.
            Her iddiadan sonra hangi chunk'tan geldiğini [chunk:N] şeklinde belirt.
            Konuşma geçmişi varsa yalnızca soruyu anlamlandırmak için kullan; cevabın kaynağı CONTEXT'tir.

            CONTEXT:
            <<<
            {context.ToPromptBlock()}
            >>>
            """;

        var messages = new List<ChatMessage>(history.Count * 2 + 2) { ChatMessage.System(system) };

        // Önceki turlar — modelin takip sorusunu bağlamıyla anlaması için.
        foreach (var turn in history)
        {
            messages.Add(ChatMessage.User(turn.Question));
            messages.Add(ChatMessage.Assistant(turn.Answer));
        }

        // Güncel soru — context'e karşı cevaplanacak olan.
        messages.Add(ChatMessage.User($"SORU: {question}"));
        return messages;
    }
}
