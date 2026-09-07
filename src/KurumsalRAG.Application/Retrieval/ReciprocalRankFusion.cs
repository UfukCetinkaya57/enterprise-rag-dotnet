using KurumsalRAG.Domain.Entities;
using KurumsalRAG.Domain.ValueObjects;

namespace KurumsalRAG.Application.Retrieval;

/// <summary>
/// Reciprocal Rank Fusion (RRF): birden çok sıralı sonuç listesini (ör. vektör + keyword araması)
/// tek listede birleştirir. SKORLARI değil SIRALAMALARI kullanır → farklı ölçekleri (cosine 0-1 vs
/// ts_rank) ve keyfi ağırlıkları ortadan kaldırır. Bir chunk her listede ne kadar üst sıradaysa
/// o kadar yüksek puan alır. Endüstri standardı hybrid search füzyonu.
///
///   fused_score(chunk) = Σ_listeler  1 / (k + rank)      (rank 1-tabanlı, k tipik 60)
/// </summary>
public static class ReciprocalRankFusion
{
    public const int DefaultK = 60;

    /// <param name="rankedLists">Her biri kendi içinde sıralı (en iyi başta) aday listeleri.</param>
    /// <param name="take">Döndürülecek füzyon sonrası aday sayısı.</param>
    /// <param name="k">RRF sabiti (büyük k → sıra farklarını yumuşatır).</param>
    public static IReadOnlyList<ScoredChunk> Fuse(
        IReadOnlyList<IReadOnlyList<ScoredChunk>> rankedLists, int take, int k = DefaultK)
    {
        var fused = new Dictionary<Guid, double>();
        var chunkById = new Dictionary<Guid, DocumentChunk>();

        foreach (var list in rankedLists)
        {
            for (var rank0 = 0; rank0 < list.Count; rank0++)
            {
                var scored = list[rank0];
                var id = scored.Chunk.Id;
                var rank = rank0 + 1; // 1-tabanlı sıra
                fused[id] = fused.GetValueOrDefault(id) + 1.0 / (k + rank);
                chunkById[id] = scored.Chunk;
            }
        }

        return fused
            .OrderByDescending(kv => kv.Value)
            .Take(take)
            .Select(kv => new ScoredChunk(chunkById[kv.Key], kv.Value))
            .ToArray();
    }
}
