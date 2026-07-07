using KurumsalRAG.Domain.ValueObjects;

namespace KurumsalRAG.Infrastructure.Services;

/// <summary>
/// RAG system prompt'unu kurar. Context'i net delimiter'larla kullanıcı sorusundan
/// ayırır (prompt injection yüzeyini daraltır) ve kaynak-atıf ([chunk:N]) talep eder.
/// </summary>
public static class RagPromptBuilder
{
    public const string RefusalText = "Sağlanan dokümanlarda bu bilgi bulunmuyor.";

    public static IReadOnlyList<ChatMessage> Build(string question, RetrievedContext context)
    {
        var system = $"""
            Sen bir kurumsal doküman asistanısın. SADECE aşağıdaki CONTEXT'e dayanarak cevap ver.
            Context'te cevap yoksa "{RefusalText}" de. Uydurma.
            Her iddiadan sonra hangi chunk'tan geldiğini [chunk:N] şeklinde belirt.

            CONTEXT:
            <<<
            {context.ToPromptBlock()}
            >>>
            """;

        // Kullanıcı sorusu ayrı bir user mesajında — system talimatlarıyla karışmaz.
        return [ChatMessage.System(system), ChatMessage.User($"SORU: {question}")];
    }
}
