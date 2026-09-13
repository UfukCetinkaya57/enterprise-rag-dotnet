using KurumsalRAG.Application.Configuration;
using KurumsalRAG.Infrastructure.Ingestion;
using Xunit;

namespace KurumsalRAG.Tests;

/// <summary>
/// Parent-document ("small-to-big") chunk'lama: küçük child'lar üretilir ama her child ait olduğu
/// BÜYÜK parent bloğun tam metnini taşır. Embedding child ile; LLM'e parent verilir.
/// </summary>
public sealed class TextChunkerParentTests
{
    private static TextChunker Build(int childTokens, int parentTokens) =>
        new(new ChunkingOptions
        {
            MaxTokens = childTokens,
            ParentMaxTokens = parentTokens,
            OverlapRatio = 0.1,
            Strategy = "ParentDocument"
        });

    // ~60 kelimelik metin (token ≈ kelime*1.25).
    private static string LongText() =>
        string.Join(' ', Enumerable.Range(1, 60).Select(i => $"kelime{i}"));

    [Fact]
    public void Bos_metin_bos_doner()
    {
        var chunker = Build(childTokens: 20, parentTokens: 60);
        Assert.Empty(chunker.ChunkWithParents(""));
        Assert.Empty(chunker.ChunkWithParents("   "));
    }

    [Fact]
    public void Child_kendi_parentinin_alt_kumesidir()
    {
        // Küçük child (~10 kelime), büyük parent (~40 kelime).
        var chunker = Build(childTokens: 12, parentTokens: 50);

        var pairs = chunker.ChunkWithParents(LongText());

        Assert.NotEmpty(pairs);
        foreach (var (child, parent) in pairs)
        {
            Assert.False(string.IsNullOrWhiteSpace(child));
            Assert.False(string.IsNullOrWhiteSpace(parent));
            // Her child kelimesi kendi parent'ında geçmeli (child ⊆ parent).
            foreach (var w in child.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                Assert.Contains(w, parent);
        }
    }

    [Fact]
    public void Parent_childden_belirgin_sekilde_buyuktur()
    {
        var chunker = Build(childTokens: 12, parentTokens: 60);

        var pairs = chunker.ChunkWithParents(LongText());

        // En az bir parent, child'ından daha çok kelime içermeli (aksi halde "büyük bağlam" anlamsız).
        Assert.Contains(pairs, p =>
            p.Parent.Split(' ').Length > p.Child.Split(' ').Length);
    }

    [Fact]
    public void Ayni_parenta_birden_fazla_child_dusebilir()
    {
        // Parent child'ın birkaç katı → tek parent birden çok child üretmeli (dedup senaryosunu kanıtlar).
        var chunker = Build(childTokens: 10, parentTokens: 60);

        var pairs = chunker.ChunkWithParents(LongText());

        var distinctParents = pairs.Select(p => p.Parent).Distinct().Count();
        Assert.True(pairs.Count > distinctParents,
            "Bir parent birden fazla child üretmeli (small-to-big).");
    }
}
