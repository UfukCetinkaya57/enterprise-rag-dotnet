using KurumsalRAG.Application.Retrieval;
using KurumsalRAG.Domain.Entities;
using KurumsalRAG.Domain.ValueObjects;

namespace KurumsalRAG.Tests;

public sealed class ReciprocalRankFusionTests
{
    private static ScoredChunk Chunk(string id, double score)
        => new(new DocumentChunk { Id = Guid.Parse(id.PadLeft(8, '0') + "-0000-0000-0000-000000000000"), Content = id }, score);

    // Sabit id'ler
    private static readonly string A = "a", B = "b", C = "c", D = "d";

    [Fact]
    public void Her_iki_listede_ust_sirada_olan_kazanir()
    {
        // vektör: A,B,C   keyword: A,D,B  → A her ikisinde 1. → en yüksek RRF
        var vector = new[] { Chunk(A, 0.9), Chunk(B, 0.8), Chunk(C, 0.7) };
        var keyword = new[] { Chunk(A, 5.0), Chunk(D, 4.0), Chunk(B, 3.0) };

        var fused = ReciprocalRankFusion.Fuse([vector, keyword], take: 4);

        Assert.Equal("a", fused[0].Chunk.Content); // A ilk sırada
        Assert.Equal(4, fused.Count);              // A,B,C,D birleşti (tekilleştirildi)
    }

    [Fact]
    public void Skor_olcegi_degil_sira_onemli()
    {
        // keyword skorları (0-5) vektörden (0-1) çok büyük ama RRF SIRAYA bakar:
        // B keyword'de 1., vektörde 3. → C'den (keyword'de yok) üstte olmalı.
        var vector = new[] { Chunk(A, 0.99), Chunk(C, 0.98), Chunk(B, 0.97) };
        var keyword = new[] { Chunk(B, 5.0) };

        var fused = ReciprocalRankFusion.Fuse([vector, keyword], take: 3);
        var order = fused.Select(f => f.Chunk.Content).ToArray();

        Assert.True(Array.IndexOf(order, "b") < Array.IndexOf(order, "c"));
    }

    [Fact]
    public void Bos_liste_digerini_bozmaz()
    {
        var vector = new[] { Chunk(A, 0.9), Chunk(B, 0.8) };
        var keyword = System.Array.Empty<ScoredChunk>();

        var fused = ReciprocalRankFusion.Fuse([vector, keyword], take: 5);

        Assert.Equal(2, fused.Count);
        Assert.Equal("a", fused[0].Chunk.Content);
    }

    [Fact]
    public void Take_siniri_uygulanir()
    {
        var vector = new[] { Chunk(A, 0.9), Chunk(B, 0.8), Chunk(C, 0.7) };
        var fused = ReciprocalRankFusion.Fuse([vector], take: 2);
        Assert.Equal(2, fused.Count);
    }
}
