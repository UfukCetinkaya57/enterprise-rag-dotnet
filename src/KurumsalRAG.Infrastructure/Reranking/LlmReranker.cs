using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KurumsalRAG.Application.Abstractions;
using KurumsalRAG.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace KurumsalRAG.Infrastructure.Reranking;

/// <summary>
/// LLM tabanlı reranker (IReranker). Retrieval'dan gelen top-k adayı, TEK bir LLM
/// çağrısında soruya alaka açısından 0.0–1.0 puanlatır, yeniden sıralar ve top-n seçer.
/// 20 aday için 20 çağrı yapmaz — hepsi tek istekte, yapılandırılmış JSON skor listesiyle.
///
/// Soyutlama bilinçli olarak IReranker arkasında: ileride bir cross-encoder
/// (ör. bge-reranker) veya Cohere Rerank API'si bu sınıfın yerine kayıtlanabilir;
/// RagQueryService değişmez.
/// </summary>
public sealed class LlmReranker : IReranker
{
    private readonly ILlmProvider _llm;
    private readonly ILogger<LlmReranker> _logger;

    public LlmReranker(ILlmProvider llm, ILogger<LlmReranker> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ScoredChunk>> RerankAsync(
        string query,
        IReadOnlyList<ScoredChunk> candidates,
        int topN,
        CancellationToken cancellationToken = default)
    {
        if (candidates.Count == 0)
            return [];

        // Aday sayısı zaten <= topN ise LLM'e gitmeye gerek yok (maliyet kontrolü).
        if (candidates.Count <= topN)
            return candidates.OrderByDescending(c => c.Score).ToArray();

        var prompt = BuildScoringPrompt(query, candidates);
        var messages = new[]
        {
            ChatMessage.System(
                "Sen bir alaka (relevance) puanlayıcısısın. Her ADAY pasajı, SORUYU ne kadar " +
                "yanıtladığına göre 0.0 (soruyla ilgisiz) ile 1.0 (soruyu doğrudan yanıtlıyor) " +
                "arasında puanla.\n" +
                "ÖNEMLİ: Bir pasaj soruyu OLUMSUZ da yanıtlasa (ör. 'X hakkı yoktur', 'X yasaktır') " +
                "bu YÜKSEK alakadır (1.0'a yakın) — çünkü sorunun cevabını içerir. Puanı, pasajın " +
                "soruyla AYNI KONUDA ve o kişiler/durum hakkında olmasına göre ver; sadece ortak " +
                "kelime geçmesine göre değil.\n" +
                "Her adayı BAĞIMSIZ puanla; pasajın listedeki sırası puanı ETKİLEMEZ.\n" +
                "SADECE şu JSON formatında dön, başka metin yazma: " +
                "{\"scores\":[{\"id\":<int>,\"score\":<0.0-1.0>}, ...]}"),
            ChatMessage.User(prompt)
        };

        try
        {
            var completion = await _llm.CompleteAsync(messages, cancellationToken);
            var scores = ParseScores(completion.Content, candidates.Count);

            if (scores.Count == 0)
            {
                _logger.LogWarning("Reranker skor döndürmedi; cosine sırasına düşülüyor.");
                return Fallback(candidates, topN);
            }

            // LLM skorunu adaylarla eşle; skoru olmayan aday cosine skorunu korur.
            // Eşit LLM skorlarında cosine skoru ikincil sıralama anahtarı (kararlı davranış).
            var reranked = candidates
                .Select((c, i) => (
                    Chunk: c.Chunk,
                    LlmScore: scores.TryGetValue(i, out var s) ? s : c.Score,
                    CosineScore: c.Score))
                .OrderByDescending(x => x.LlmScore)
                .ThenByDescending(x => x.CosineScore)
                .Take(topN)
                .Select(x => new ScoredChunk(x.Chunk, x.LlmScore))
                .ToArray();

            return reranked;
        }
        catch (Exception ex)
        {
            // Reranker bir "iyileştirme" katmanı; hata halinde retrieval'ı bloklamaz.
            _logger.LogWarning(ex, "Reranker çağrısı başarısız; cosine sırasına düşülüyor.");
            return Fallback(candidates, topN);
        }
    }

    private static IReadOnlyList<ScoredChunk> Fallback(IReadOnlyList<ScoredChunk> candidates, int topN)
        => candidates.OrderByDescending(c => c.Score).Take(topN).ToArray();

    private static string BuildScoringPrompt(string query, IReadOnlyList<ScoredChunk> candidates)
    {
        var sb = new StringBuilder();
        sb.Append("SORU: ").Append(query).Append("\n\nADAYLAR:\n");
        for (var i = 0; i < candidates.Count; i++)
        {
            // Puanlama için içeriği kısalt — token tasarrufu, sıralama kalitesini bozmaz.
            // Chunk'lar zaten ~250 token (~1000 char); kararı belirleyen cümlenin
            // kesilmemesi için geniş bir üst sınır. Aşırı uzun chunk'larda token korumak
            // için yine de sınır var.
            var content = candidates[i].Chunk.Content;
            var snippet = content.Length > 1200 ? content[..1200] : content;
            sb.Append("id=").Append(i).Append(": ").Append(snippet.Replace('\n', ' ')).Append('\n');
        }
        return sb.ToString();
    }

    private static IReadOnlyDictionary<int, double> ParseScores(string json, int candidateCount)
    {
        var result = new Dictionary<int, double>();

        // Model bazen JSON'ı ```json ... ``` içine sarar; çıplak gövdeyi ayıkla.
        var start = json.IndexOf('{');
        var end = json.LastIndexOf('}');
        if (start < 0 || end <= start)
            return result;

        var body = json[start..(end + 1)];
        try
        {
            var parsed = JsonSerializer.Deserialize<ScoreEnvelope>(body);
            if (parsed?.Scores is null)
                return result;

            foreach (var s in parsed.Scores)
            {
                if (s.Id >= 0 && s.Id < candidateCount)
                    result[s.Id] = Math.Clamp(s.Score, 0.0, 1.0);
            }
        }
        catch (JsonException)
        {
            // Bozuk JSON -> boş sözlük -> çağıran fallback'e düşer.
        }
        return result;
    }

    private sealed record ScoreEnvelope(
        [property: JsonPropertyName("scores")] IReadOnlyList<ScoreItem>? Scores);

    private sealed record ScoreItem(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("score")] double Score);
}
