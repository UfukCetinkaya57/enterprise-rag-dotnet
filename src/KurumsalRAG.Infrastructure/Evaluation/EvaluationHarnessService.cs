using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Application.Configuration;
using KurumsalRAG.Application.Evaluation;
using KurumsalRAG.Infrastructure.Agents;
using KurumsalRAG.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KurumsalRAG.Infrastructure.Evaluation;

/// <summary>
/// Altın soru setini gerçek retrieval + generation hattından geçirip metrikleri hesaplar.
/// Cache/bütçe by-pass edilir (her seferinde gerçek hattı ölçmek için). Diagnostics-gated
/// (prod'da kapalı) — çok LLM çağrısı yapar.
/// </summary>
public sealed class EvaluationHarnessService : IEvaluationHarness
{
    private readonly RetrievalAgent _retrieval;
    private readonly AnswerAgent _answer;
    private readonly IFaithfulnessEvaluator _faithfulness;
    private readonly bool _faithfulnessEnabled;
    private readonly ILogger<EvaluationHarnessService> _logger;

    public EvaluationHarnessService(
        RetrievalAgent retrieval,
        AnswerAgent answer,
        IFaithfulnessEvaluator faithfulness,
        IOptions<RagOptions> options,
        ILogger<EvaluationHarnessService> logger)
    {
        _retrieval = retrieval;
        _answer = answer;
        _faithfulness = faithfulness;
        _faithfulnessEnabled = options.Value.Faithfulness.Enabled;
        _logger = logger;
    }

    public async Task<EvalReport> RunAsync(int interQuestionDelayMs = 0, CancellationToken cancellationToken = default)
    {
        var cases = new List<EvalCase>();
        var first = true;
        foreach (var q in GoldenSet.Questions)
        {
            // Free-tier RPM'i aşmamak için sorular arası gecikme (ilk sorudan önce beklemeyiz).
            if (!first && interQuestionDelayMs > 0)
                await Task.Delay(interQuestionDelayMs, cancellationToken);
            first = false;

            cases.Add(await EvaluateOneAsync(q, cancellationToken));
        }

        var inCtx = cases.Where(c => !c.ShouldRefuse).ToArray();
        var outCtx = cases.Where(c => c.ShouldRefuse).ToArray();
        var faithScores = cases.Where(c => c.Faithfulness is not null).Select(c => c.Faithfulness!.Value).ToArray();

        var report = new EvalReport(
            TotalQuestions: cases.Count,
            InContextQuestions: inCtx.Length,
            OutOfContextQuestions: outCtx.Length,
            RetrievalRecall: Ratio(inCtx.Count(c => c.RetrievalHit), inCtx.Length),
            Mrr: inCtx.Length == 0 ? 0 : inCtx.Average(c => c.FactRank > 0 ? 1.0 / c.FactRank : 0.0),
            AnswerAccuracy: Ratio(inCtx.Count(c => c.AnswerCorrect), inCtx.Length),
            RefusalAccuracy: Ratio(outCtx.Count(c => c.AnswerCorrect), outCtx.Length),
            AvgFaithfulness: faithScores.Length == 0 ? null : faithScores.Average(),
            Cases: cases);

        _logger.LogInformation(
            "EVAL-SUITE recall={Recall:P0} mrr={Mrr:F2} answerAcc={Acc:P0} refusalAcc={Ref:P0} faith={Faith}",
            report.RetrievalRecall, report.Mrr, report.AnswerAccuracy, report.RefusalAccuracy,
            report.AvgFaithfulness);
        return report;
    }

    private async Task<EvalCase> EvaluateOneAsync(GoldenQuestion q, CancellationToken cancellationToken)
    {
        // 1) Retrieval — beklenen gerçek getirilen parçalarda var mı, kaçıncı sırada?
        var context = await _retrieval.RetrieveAsync(q.Question, cancellationToken);
        var retrievalHit = false;
        var factRank = 0;
        if (!q.ShouldRefuse)
        {
            for (var i = 0; i < context.Chunks.Count; i++)
            {
                if (FactMatcher.ContainsAny(context.Chunks[i].Content, q.ExpectedFacts))
                {
                    retrievalHit = true;
                    factRank = i + 1; // 1-tabanlı sıra (MRR için)
                    break;
                }
            }
        }

        // 2) Generation — cevabı üret. LLM hatası (ör. 429) tüm suite'i çökertmesin.
        string answer;
        try
        {
            answer = await _answer.AnswerAsync(q.Question, context, strict: false, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Eval: cevap üretilemedi (ör. rate limit): {Q}", q.Question);
            return new EvalCase(q.Question, q.ShouldRefuse, retrievalHit, factRank,
                AnswerCorrect: false, Faithfulness: null, AnswerPreview: "[LLM hatası]");
        }

        // 3) Doğruluk — context-içi: gerçek var mı; context-dışı: red etti mi.
        bool answerCorrect;
        if (q.ShouldRefuse)
            answerCorrect = FactMatcher.Normalize(answer)
                .Contains(FactMatcher.Normalize(RagPromptBuilder.RefusalText), StringComparison.Ordinal);
        else
            answerCorrect = FactMatcher.ContainsAny(answer, q.ExpectedFacts);

        // 4) Faithfulness (varsa) — yalnızca context boş değilse anlamlı.
        double? faithfulness = null;
        if (_faithfulnessEnabled && !context.IsEmpty)
        {
            try
            {
                var eval = await _faithfulness.EvaluateAsync(answer, context, cancellationToken);
                faithfulness = eval.Score;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Eval: faithfulness ölçülemedi: {Q}", q.Question);
            }
        }

        var preview = answer.Length > 90 ? answer[..90].Replace('\n', ' ') + "…" : answer;
        return new EvalCase(q.Question, q.ShouldRefuse, retrievalHit, factRank, answerCorrect, faithfulness, preview);
    }

    private static double Ratio(int hit, int total) => total == 0 ? 0 : (double)hit / total;
}
