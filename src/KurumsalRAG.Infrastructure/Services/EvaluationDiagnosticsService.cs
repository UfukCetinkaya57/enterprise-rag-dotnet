using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Infrastructure.Agents;
using KurumsalRAG.Infrastructure.Configuration;
using KurumsalRAG.Infrastructure.Ingestion;
using KurumsalRAG.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KurumsalRAG.Infrastructure.Services;

/// <summary>
/// Tek bir soru için uçtan uca değerlendirme kaydı üretir: guard sonucu, kullanılan
/// chunk'lar + retrieval skorları, faithfulness (groundedness), reflection izi ve
/// token/maliyet tahmini. Kurumsal gözlem/observability için tek yapılandırılmış kayıt.
/// </summary>
public sealed class EvaluationDiagnosticsService : IEvaluationDiagnostics
{
    private readonly IPromptGuard _guard;
    private readonly RagOrchestrator _orchestrator;
    private readonly PricingOptions _pricing;
    private readonly ILogger<EvaluationDiagnosticsService> _logger;

    public EvaluationDiagnosticsService(
        IPromptGuard guard,
        RagOrchestrator orchestrator,
        IOptions<OpenAiOptions> openAi,
        ILogger<EvaluationDiagnosticsService> logger)
    {
        _guard = guard;
        _orchestrator = orchestrator;
        _pricing = openAi.Value.Pricing;
        _logger = logger;
    }

    public async Task<EvaluationRecord> EvaluateAsync(string question, CancellationToken cancellationToken = default)
    {
        // 1) GUARD
        var guard = _guard.Inspect(question);
        var guardInfo = new GuardInfo(
            guard.IsSuspicious, guard.MatchedRule, guard.Action.ToString(), guard.Reasons);

        if (guard.ShouldBlock)
        {
            _logger.LogWarning("Eval: istek bloklandı. Kural: {Rule}", guard.MatchedRule);
            return new EvaluationRecord(
                question, "[BLOCKED]", guardInfo, [], null, false, [], false,
                new TokenCost(0, 0, 0, 0m));
        }

        // 2) Tam agentic akış (retrieve -> answer -> check -> reflection)
        var result = await _orchestrator.RunAsync(guard.SanitizedInput, cancellationToken);

        var retrieved = result.Context.Chunks
            .Select(c => new RetrievalScore(c.Reference, c.ChunkId, c.Score))
            .ToArray();

        // 3) Token/maliyet tahmini (SK usage döndürmediği için metin uzunluğundan kestirilir).
        var promptTokens = TokenEstimator.Estimate(result.Context.ToPromptBlock() + guard.SanitizedInput);
        var completionTokens = TokenEstimator.Estimate(result.Answer);
        var embeddingTokens = TokenEstimator.Estimate(guard.SanitizedInput);
        // Reflection tetiklendiyse answer+check ~2 kat çağrı; kabaca yansıt.
        var factor = result.ReflectionApplied ? 2 : 1;
        var cost = EstimateCost(promptTokens * factor, completionTokens * factor, embeddingTokens);

        var record = new EvaluationRecord(
            question,
            result.Answer,
            guardInfo,
            retrieved,
            result.Faithfulness.Score,
            result.Faithfulness.Passed,
            result.Faithfulness.UnsupportedClaims,
            result.ReflectionApplied,
            new TokenCost(promptTokens * factor, completionTokens * factor, embeddingTokens, cost));

        // Tek satır yapılandırılmış eval log'u.
        _logger.LogInformation(
            "EVAL q='{Q}' guard={Guard} chunks={Chunks} faithfulness={Faith:F2} passed={Passed} " +
            "reflection={Refl} tokens={Tokens} costUsd={Cost}",
            question, guard.IsSuspicious, retrieved.Length, result.Faithfulness.Score,
            result.Faithfulness.Passed, result.ReflectionApplied, record.Cost.TotalTokens, cost);

        return record;
    }

    private decimal EstimateCost(int promptTokens, int completionTokens, int embeddingTokens)
        => promptTokens / 1_000_000m * _pricing.ChatInputPerMillion
         + completionTokens / 1_000_000m * _pricing.ChatOutputPerMillion
         + embeddingTokens / 1_000_000m * _pricing.EmbeddingPerMillion;
}
