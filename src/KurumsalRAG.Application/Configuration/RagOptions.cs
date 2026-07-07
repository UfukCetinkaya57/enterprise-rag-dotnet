namespace KurumsalRAG.Application.Configuration;

/// <summary>
/// Tüm RAG davranışının konfigürasyonu. appsettings.json'daki "Rag" bölümünden bağlanır.
/// Hard-code yok: model isimleri, chunk boyutu, top-k/n, eşikler hep buradan.
/// </summary>
public sealed class RagOptions
{
    public const string SectionName = "Rag";

    public ChunkingOptions Chunking { get; init; } = new();
    public RetrievalOptions Retrieval { get; init; } = new();
    public FaithfulnessOptions Faithfulness { get; init; } = new();
}

public sealed class ChunkingOptions
{
    /// <summary>Hedef chunk boyutu (token). Spec: ~500-800.</summary>
    public int MaxTokens { get; init; } = 600;

    /// <summary>Chunk'lar arası örtüşme oranı (0-1). Spec: ~%10-15.</summary>
    public double OverlapRatio { get; init; } = 0.12;
}

public sealed class RetrievalOptions
{
    /// <summary>Vektör aramadan çekilecek aday sayısı (rerank'ten önce).</summary>
    public int TopK { get; init; } = 20;

    /// <summary>Rerank sonrası context'e girecek chunk sayısı.</summary>
    public int TopN { get; init; } = 5;
}

public sealed class FaithfulnessOptions
{
    /// <summary>Bu skorun altındaki cevaplar "düşük groundedness" işaretlenir.</summary>
    public double Threshold { get; init; } = 0.6;

    /// <summary>Faithfulness kontrolünü açar/kapar (maliyet kontrolü).</summary>
    public bool Enabled { get; init; } = true;
}
