using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Domain.ValueObjects;

namespace KurumsalRAG.Infrastructure.Agents;

/// <summary>
/// Faithfulness tanılama: tam agentic akışı çalıştırır (InspectAsync) ve denetçiyi izole
/// test etmek için elle kurgulanmış bir cevabı denetime sokar (CheckClaimAsync).
/// </summary>
public sealed class FaithfulnessDiagnosticsService : IFaithfulnessDiagnostics
{
    private readonly RagOrchestrator _orchestrator;
    private readonly RetrievalAgent _retrieval;
    private readonly IFaithfulnessEvaluator _faithfulness;

    public FaithfulnessDiagnosticsService(
        RagOrchestrator orchestrator,
        RetrievalAgent retrieval,
        IFaithfulnessEvaluator faithfulness)
    {
        _orchestrator = orchestrator;
        _retrieval = retrieval;
        _faithfulness = faithfulness;
    }

    public async Task<FaithfulnessReport> InspectAsync(string question, CancellationToken cancellationToken = default)
    {
        var result = await _orchestrator.RunAsync(question, cancellationToken);
        return ToReport(question, result.Answer, result.Faithfulness, result.ReflectionApplied,
            result.FirstScore, result.Context);
    }

    public async Task<FaithfulnessReport> CheckClaimAsync(
        string question, string candidateAnswer, CancellationToken cancellationToken = default)
    {
        // Aynı retrieval'ı kullan ama cevabı ÜRETME — dışarıdan verilen cevabı denetle.
        var context = await _retrieval.RetrieveAsync(question, cancellationToken);
        var eval = await _faithfulness.EvaluateAsync(candidateAnswer, context, cancellationToken);
        return ToReport(question, candidateAnswer, eval, reflectionApplied: false,
            firstScore: eval.Score, context);
    }

    private static FaithfulnessReport ToReport(
        string question, string answer, FaithfulnessResult eval,
        bool reflectionApplied, double firstScore, RetrievedContext context)
        => new(
            question,
            answer,
            eval.Score,
            eval.Passed,
            eval.UnsupportedClaims,
            reflectionApplied,
            firstScore,
            context.Chunks.Select(c => new ContextRef(
                c.Reference, c.ChunkId, c.Score,
                c.Content.Length > 90 ? c.Content[..90].Replace('\n', ' ') + "…" : c.Content)).ToArray());
}
