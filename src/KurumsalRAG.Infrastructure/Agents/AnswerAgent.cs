using KurumsalRAG.Domain.ValueObjects;
using KurumsalRAG.Infrastructure.Services;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace KurumsalRAG.Infrastructure.Agents;

/// <summary>
/// AnswerAgent — context + soruyla taslak cevabı üretir. Üretimi Semantic Kernel'in
/// IChatCompletionService'i üzerinden yapar (SK'nın native provider soyutlaması).
/// Reflection döngüsü için "strict" modu vardır: yalnızca desteklenen iddialarda kalmayı
/// zorlayan ek talimatla yeniden üretir.
/// </summary>
public sealed class AnswerAgent
{
    private readonly IChatCompletionService _chat;

    public AnswerAgent(Kernel kernel)
        => _chat = kernel.GetRequiredService<IChatCompletionService>();

    public async Task<string> AnswerAsync(
        string question,
        RetrievedContext context,
        bool strict = false,
        CancellationToken cancellationToken = default)
    {
        var messages = RagPromptBuilder.Build(question, context);

        var history = new ChatHistory();
        foreach (var m in messages)
        {
            switch (m.Role)
            {
                case ChatRole.System: history.AddSystemMessage(m.Content); break;
                case ChatRole.User: history.AddUserMessage(m.Content); break;
                case ChatRole.Assistant: history.AddAssistantMessage(m.Content); break;
            }
        }

        if (strict)
        {
            // Reflection retry: critic düşük skor verdiğinde uygulanır.
            history.AddSystemMessage(
                "UYARI: Önceki cevap context'te tam desteklenmeyen iddialar içeriyordu. " +
                "Bu kez SADECE yukarıdaki CONTEXT'te açıkça yazan bilgiyi kullan. " +
                "Context'te olmayan hiçbir şeyi ekleme; emin olmadığın kısmı yazma.");
        }

        var result = await _chat.GetChatMessageContentAsync(history, cancellationToken: cancellationToken);
        return result.Content ?? string.Empty;
    }
}
