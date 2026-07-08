using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Application.Configuration;
using KurumsalRAG.Domain.ValueObjects;
using KurumsalRAG.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KurumsalRAG.Infrastructure.Agents;

/// <summary>
/// Ajanları sırayla koşturan orchestrator: retrieve → answer → check.
/// Groundedness skoru eşiğin altındaysa TEK bir reflection denemesi yapar
/// (AnswerAgent'ı strict modda tekrar çağırır); yine düşükse cevaba uyarı ekler.
/// Yeniden üretim 1 ile sınırlıdır (sonsuz döngü olmaz).
/// </summary>
public sealed class RagOrchestrator
{
    private readonly RetrievalAgent _retrieval;
    private readonly AnswerAgent _answer;
    private readonly IFaithfulnessEvaluator _faithfulness;
    private readonly FaithfulnessOptions _options;
    private readonly ILogger<RagOrchestrator> _logger;

    private const string LowGroundednessWarning =
        "\n\n⚠️ Not: Bu cevabın bir kısmı sağlanan dokümanlarca tam olarak desteklenmiyor olabilir.";

    /// <summary>Cevap, standart "dokümanlarda yok" reti mi? (grounded davranış, iddia değil).</summary>
    private static bool IsRefusal(string answer)
        => answer.Contains(RagPromptBuilder.RefusalText, StringComparison.OrdinalIgnoreCase);

    public RagOrchestrator(
        RetrievalAgent retrieval,
        AnswerAgent answer,
        IFaithfulnessEvaluator faithfulness,
        IOptions<RagOptions> options,
        ILogger<RagOrchestrator> logger)
    {
        _retrieval = retrieval;
        _answer = answer;
        _faithfulness = faithfulness;
        _options = options.Value.Faithfulness;
        _logger = logger;
    }

    public async Task<OrchestratedAnswer> RunAsync(string question, CancellationToken cancellationToken = default)
    {
        // 1) RETRIEVE
        var context = await _retrieval.RetrieveAsync(question, cancellationToken);

        // 2) ANSWER (taslak)
        var answer = await _answer.AnswerAsync(question, context, strict: false, cancellationToken);

        // Standart ret cevabı ("dokümanlarda yok") zaten grounded davranıştır — bir iddia
        // değildir. Denetime sokup düşük skorla cezalandırma; reflection'ı da atla.
        if (IsRefusal(answer) || context.IsEmpty)
        {
            var groundedRefusal = new FaithfulnessResult(1.0, [], Passed: true);
            _logger.LogInformation("Ret/boş context cevabı grounded sayıldı; faithlessness atlandı.");
            return new OrchestratedAnswer(answer, context, groundedRefusal, ReflectionApplied: false, FirstScore: 1.0);
        }

        // 3) CHECK
        var eval = await _faithfulness.EvaluateAsync(answer, context, cancellationToken);
        _logger.LogInformation("Groundedness (ilk): {Score:F2}", eval.Score);

        var reflectionApplied = false;
        var firstScore = eval.Score;

        // 4) REFLECTION (en fazla 1 kez) — faithfulness aktif ve eşiğin altındaysa
        if (_options.Enabled && !eval.Passed && !context.IsEmpty)
        {
            _logger.LogInformation("Eşik altı skor; strict modda yeniden üretiliyor (reflection).");
            var retried = await _answer.AnswerAsync(question, context, strict: true, cancellationToken);
            var retriedEval = await _faithfulness.EvaluateAsync(retried, context, cancellationToken);
            _logger.LogInformation("Groundedness (reflection sonrası): {Score:F2}", retriedEval.Score);

            reflectionApplied = true;

            // Reflection daha iyi (ya da eşit) ise onu al.
            if (retriedEval.Score >= eval.Score)
            {
                answer = retried;
                eval = retriedEval;
            }
        }

        // 5) Hâlâ eşik altındaysa cevaba şeffaf bir uyarı ekle.
        var finalAnswer = eval.Passed ? answer : answer + LowGroundednessWarning;

        return new OrchestratedAnswer(
            finalAnswer, context, eval, reflectionApplied, firstScore);
    }
}

/// <summary>Orchestrator çıktısı: nihai cevap + context + değerlendirme + reflection izleri.</summary>
public sealed record OrchestratedAnswer(
    string Answer,
    RetrievedContext Context,
    FaithfulnessResult Faithfulness,
    bool ReflectionApplied,
    double FirstScore);
