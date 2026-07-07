namespace KurumsalRAG.Domain.ValueObjects;

/// <summary>
/// Bir cevap üretmek için LLM'e sunulan, seçilmiş chunk'lardan oluşan context.
/// Prompt'ta [chunk:N] atıfları bu <see cref="RetrievedChunk.Reference"/> ile eşleşir.
/// </summary>
public sealed record RetrievedContext(IReadOnlyList<RetrievedChunk> Chunks)
{
    public bool IsEmpty => Chunks.Count == 0;

    /// <summary>Prompt'a gömülecek [chunk:N] etiketli, birleştirilmiş context metni.</summary>
    public string ToPromptBlock()
        => string.Join(
            "\n\n",
            Chunks.Select(c => $"[chunk:{c.Reference}] {c.Content}"));
}

/// <summary>Context içindeki tek bir chunk; N atıf numarası ve kaynak izlenebilirliği taşır.</summary>
public sealed record RetrievedChunk(int Reference, Guid ChunkId, string Content, double Score);
