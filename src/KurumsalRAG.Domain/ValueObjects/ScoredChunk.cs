using KurumsalRAG.Domain.Entities;

namespace KurumsalRAG.Domain.ValueObjects;

/// <summary>
/// Bir chunk + onun bir sorguya göre alaka skoru.
/// Retrieval (cosine benzerliği) ve rerank aşamalarında taşınır.
/// </summary>
public sealed record ScoredChunk(DocumentChunk Chunk, double Score);
