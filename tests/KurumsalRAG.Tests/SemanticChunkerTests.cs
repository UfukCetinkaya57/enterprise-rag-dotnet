using KurumsalRAG.Application.Configuration;
using KurumsalRAG.Infrastructure.Ingestion;
using Xunit;

namespace KurumsalRAG.Tests;

/// <summary>
/// Semantic chunking: cümlelere böler, ardışık embedding benzerliği düşünce (konu değişimi) veya
/// boyut sınırı aşılınca yeni chunk başlar. Saf/deterministik — embedding'ler elle verilir.
/// </summary>
public sealed class SemanticChunkerTests
{
    private static SemanticChunker Build(double threshold = 0.5, int maxTokens = 800) =>
        new(new ChunkingOptions { SemanticBreakThreshold = threshold, SemanticMaxTokens = maxTokens });

    [Fact]
    public void Cumlelere_boler()
    {
        var chunker = Build();
        var sentences = chunker.SplitSentences("Birinci cümle. İkinci cümle! Üçüncü cümle?");
        Assert.Equal(3, sentences.Count);
    }

    [Fact]
    public void Bos_metin_bos_doner()
    {
        var chunker = Build();
        Assert.Empty(chunker.SplitSentences(""));
        Assert.Empty(chunker.GroupBySimilarity([], []));
    }

    [Fact]
    public void Benzerlik_dusunce_yeni_chunk_baslar()
    {
        var chunker = Build(threshold: 0.5);
        var sentences = new[] { "A konusu.", "A devam.", "B konusu.", "B devam." };
        // A cümleleri birbirine benzer (dik yön 1), B'ye geçişte benzerlik düşük (dik vektör).
        var embeddings = new[]
        {
            new[] { 1f, 0f },  // A
            new[] { 1f, 0f },  // A (öncekiyle benzerlik=1 → aynı chunk)
            new[] { 0f, 1f },  // B (öncekiyle benzerlik=0 → BÖL)
            new[] { 0f, 1f },  // B (benzerlik=1 → aynı chunk)
        };

        var chunks = chunker.GroupBySimilarity(sentences, embeddings);

        Assert.Equal(2, chunks.Count);                 // A bloğu + B bloğu
        Assert.Contains("A konusu. A devam.", chunks[0]);
        Assert.Contains("B konusu. B devam.", chunks[1]);
    }

    [Fact]
    public void Yuksek_benzerlik_tek_chunkta_toplar()
    {
        var chunker = Build(threshold: 0.5);
        var sentences = new[] { "Cümle bir.", "Cümle iki.", "Cümle üç." };
        var embeddings = new[] { new[] { 1f, 0f }, new[] { 1f, 0f }, new[] { 1f, 0f } };

        var chunks = chunker.GroupBySimilarity(sentences, embeddings);

        Assert.Single(chunks);   // hepsi benzer → tek chunk
    }

    [Fact]
    public void Boyut_siniri_asilinca_bolunur()
    {
        // Çok küçük SemanticMaxTokens → benzerlik yüksek olsa bile boyuttan böler.
        var chunker = Build(threshold: 0.0, maxTokens: 3); // eşik 0 = benzerlikten asla bölme
        var sentences = new[] { "kelime kelime kelime.", "kelime kelime kelime." };
        var embeddings = new[] { new[] { 1f, 0f }, new[] { 1f, 0f } }; // benzerlik=1

        var chunks = chunker.GroupBySimilarity(sentences, embeddings);

        Assert.Equal(2, chunks.Count);   // boyut sınırı benzerliği ezip böldü
    }

    [Fact]
    public void Noktalamasiz_dev_paragraf_boyuta_gore_alt_bolunur()
    {
        // Noktalama içermeyen çok uzun metin → tek "cümle" olmamalı, SemanticMaxTokens'a göre bölünmeli.
        var chunker = Build(maxTokens: 10);
        var dev = string.Join(' ', Enumerable.Range(1, 100).Select(i => $"kelime{i}")); // nokta yok
        var sentences = chunker.SplitSentences(dev);
        Assert.True(sentences.Count > 1, "Dev paragraf alt-bölünmeliydi.");
    }

    [Fact]
    public void Ondalik_sayidaki_nokta_cumle_sonu_sayilmaz()
    {
        var chunker = Build();
        // "3.14" ortasındaki nokta bölmemeli → tek cümle.
        var sentences = chunker.SplitSentences("Oran 3.14 olarak belirlendi");
        Assert.Single(sentences);
    }

    [Fact]
    public void Cumle_embedding_sayisi_uyusmazsa_hata()
    {
        var chunker = Build();
        Assert.Throws<ArgumentException>(() =>
            chunker.GroupBySimilarity(["a", "b"], [new[] { 1f }]));
    }
}
