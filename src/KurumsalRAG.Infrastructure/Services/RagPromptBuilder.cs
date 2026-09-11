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

    /// <summary>Geçmişsiz (tek-tur) kısayol — mevcut çağıranlarla uyumluluk için.</summary>
    public static IReadOnlyList<ChatMessage> Build(string question, RetrievedContext context)
        => Build(question, context, []);

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
