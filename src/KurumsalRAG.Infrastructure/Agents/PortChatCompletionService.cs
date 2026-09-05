using System.Runtime.CompilerServices;
using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Domain.ValueObjects;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Services;

namespace KurumsalRAG.Infrastructure.Agents;

/// <summary>
/// Semantic Kernel'in IChatCompletionService'ini, uygulamanın kendi ILlmProvider portuna
/// köprüleyen adapter. Böylece SK agentic katmanı (AnswerAgent, FaithfulnessCheckerAgent)
/// da hangi sağlayıcı seçiliyse (OpenAI/Gemini) ONU kullanır — provider-agnostik mimari
/// SK dahil uçtan uca korunur. SK'ya OpenAI-özel connector bağlamak yerine kendi portumuzu veririz.
/// </summary>
public sealed class PortChatCompletionService : IChatCompletionService
{
    private readonly ILlmProvider _llm;

    public PortChatCompletionService(ILlmProvider llm) => _llm = llm;

    public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var messages = Map(chatHistory);
        var completion = await _llm.CompleteAsync(messages, cancellationToken);
        return [new ChatMessageContent(AuthorRole.Assistant, completion.Content)];
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messages = Map(chatHistory);
        await foreach (var token in _llm.StreamAsync(messages, cancellationToken))
            yield return new StreamingChatMessageContent(AuthorRole.Assistant, token);
    }

    /// <summary>SK ChatHistory'yi uygulamanın ChatMessage listesine çevirir.</summary>
    private static IReadOnlyList<ChatMessage> Map(ChatHistory chatHistory)
    {
        var result = new List<ChatMessage>(chatHistory.Count);
        foreach (var m in chatHistory)
        {
            var role = m.Role.Label switch
            {
                "system" => ChatRole.System,
                "assistant" => ChatRole.Assistant,
                _ => ChatRole.User
            };
            result.Add(new ChatMessage(role, m.Content ?? string.Empty));
        }
        return result;
    }
}
