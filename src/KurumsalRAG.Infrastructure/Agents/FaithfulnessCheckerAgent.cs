using System.Text.Json;
using System.Text.Json.Serialization;
using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Application.Configuration;
using KurumsalRAG.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace KurumsalRAG.Infrastructure.Agents;

/// <summary>
/// FaithfulnessCheckerAgent (critic/evaluator) — üretilen cevabı ve kullanılan context'i
/// alır; cevaptaki her iddianın context'te desteklenip desteklenmediğini denetler.
/// 0.0–1.0 groundedness skoru + desteklenmeyen iddiaların listesini döndürür.
///
/// IFaithfulnessEvaluator port'unu implemente eder — NoOpFaithfulnessEvaluator'ın yerine
/// kayıtlanır. Değerlendirme Semantic Kernel üzerinden yapılır.
/// </summary>
public sealed class FaithfulnessCheckerAgent : IFaithfulnessEvaluator
{
    private readonly IChatCompletionService _chat;
    private readonly FaithfulnessOptions _options;
    private readonly ILogger<FaithfulnessCheckerAgent> _logger;

    public FaithfulnessCheckerAgent(
        Kernel kernel,
        IOptions<RagOptions> options,
        ILogger<FaithfulnessCheckerAgent> logger)
    {
        _chat = kernel.GetRequiredService<IChatCompletionService>();
        _options = options.Value.Faithfulness;
        _logger = logger;
    }

    public async Task<FaithfulnessResult> EvaluateAsync(
        string answer,
        RetrievedContext context,
        CancellationToken cancellationToken = default)
    {
        var history = new ChatHistory();
        history.AddSystemMessage(
            "Sen bir groundedness (faithfulness) denetçisisin. Sana bir CONTEXT ve bir CEVAP verilecek. " +
            "Görevin: CEVAP'taki her iddianın SADECE CONTEXT'e dayanıp dayanmadığını denetlemek.\n" +
            "- CONTEXT'te açıkça desteklenmeyen her iddiayı 'desteklenmeyen' say.\n" +
            "- Genel bilgiden gelen ama context'te olmayan iddialar da desteklenmezdir.\n" +
            "- 'score': desteklenen iddia oranını yansıtan 0.0–1.0 arası bir sayı " +
            "(tüm iddialar destekliyse 1.0; hiçbiri desteklenmiyorsa 0.0).\n" +
            "SADECE şu JSON'u dön, başka metin yazma:\n" +
            "{\"score\": <0.0-1.0>, \"unsupported_claims\": [\"...\"]}");

        history.AddUserMessage(
            $"CONTEXT:\n<<<\n{context.ToPromptBlock()}\n>>>\n\nCEVAP:\n<<<\n{answer}\n>>>");

        var result = await _chat.GetChatMessageContentAsync(history, cancellationToken: cancellationToken);
        var parsed = ParseVerdict(result.Content ?? string.Empty);

        var passed = parsed.Score >= _options.Threshold;
        if (!passed)
        {
            _logger.LogWarning(
                "Faithfulness eşiğin altında: {Score:F2} < {Threshold:F2}. Desteklenmeyen: {Claims}",
                parsed.Score, _options.Threshold, string.Join(" | ", parsed.UnsupportedClaims));
        }

        return new FaithfulnessResult(parsed.Score, parsed.UnsupportedClaims, passed);
    }

    private (double Score, IReadOnlyList<string> UnsupportedClaims) ParseVerdict(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            _logger.LogWarning("Faithfulness denetçisi geçerli JSON döndürmedi; skor 0 varsayılıyor.");
            return (0.0, ["Denetçi çıktısı ayrıştırılamadı."]);
        }

        try
        {
            var verdict = JsonSerializer.Deserialize<Verdict>(content[start..(end + 1)]);
            if (verdict is null)
                return (0.0, ["Denetçi çıktısı boş."]);

            var score = Math.Clamp(verdict.Score, 0.0, 1.0);
            var claims = verdict.UnsupportedClaims ?? [];
            return (score, claims);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Faithfulness JSON ayrıştırma hatası.");
            return (0.0, ["Denetçi çıktısı ayrıştırılamadı."]);
        }
    }

    private sealed record Verdict(
        [property: JsonPropertyName("score")] double Score,
        [property: JsonPropertyName("unsupported_claims")] IReadOnlyList<string>? UnsupportedClaims);
}
