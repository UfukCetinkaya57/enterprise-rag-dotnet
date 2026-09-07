namespace KurumsalRAG.Application.Abstractions;

/// <summary>
/// Değerlendirme koşucusu: bir "altın soru seti"ni tüm RAG hattından geçirip retrieval ve
/// generation kalitesini ÖLÇER (Recall, MRR, answer accuracy, refusal, faithfulness).
/// "RAG'imi ölçebiliyorum" — regresyon ve iyileştirme kararlarının temeli. Dev/CI aracı
/// (çok LLM çağrısı yapar; prod'da diagnostics kapalıyken erişilemez).
/// </summary>
public interface IEvaluationHarness
{
    /// <param name="interQuestionDelayMs">
    /// Sorular arası gecikme (ms). Free-tier rate limit'ini (RPM) aşmamak için kullanılır;
    /// 0 = gecikme yok (ücretli/yerel model).
    /// </param>
    Task<EvalReport> RunAsync(int interQuestionDelayMs = 0, CancellationToken cancellationToken = default);
}

/// <summary>Bir altın soru: beklenen gerçekler + context dışında mı (red beklenir mi).</summary>
/// <param name="Question">Sorulacak soru.</param>
/// <param name="ExpectedFacts">Doğru cevabın/parçanın içermesi beklenen metin parçaları (OR).</param>
/// <param name="ShouldRefuse">Context dışı ise true — sistem "dokümanlarda yok" demeli.</param>
public sealed record GoldenQuestion(string Question, string[] ExpectedFacts, bool ShouldRefuse);

/// <summary>Tek bir sorunun sonucu (tanılama için).</summary>
public sealed record EvalCase(
    string Question,
    bool ShouldRefuse,
    bool RetrievalHit,      // beklenen gerçek getirilen parçalarda var mı (context recall)
    int FactRank,           // ilk gerçek-taşıyan parçanın sırası (1-tabanlı; 0 = bulunamadı) → MRR
    bool AnswerCorrect,     // cevap beklenen gerçeği/red'i içeriyor mu
    double? Faithfulness,   // groundedness (varsa)
    string AnswerPreview);

/// <summary>Toplu değerlendirme raporu — mülakatta "RAG'imi böyle ölçüyorum".</summary>
public sealed record EvalReport(
    int TotalQuestions,
    int InContextQuestions,
    int OutOfContextQuestions,
    double RetrievalRecall,     // context-içi sorularda beklenen parça getirilme oranı
    double Mrr,                 // ilk doğru parçanın ortalama tersinir sırası
    double AnswerAccuracy,      // context-içi cevap doğruluk oranı
    double RefusalAccuracy,     // context-dışı doğru red oranı
    double? AvgFaithfulness,    // ortalama groundedness (ölçüldüyse)
    IReadOnlyList<EvalCase> Cases);
