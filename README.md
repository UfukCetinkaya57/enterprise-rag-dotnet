# Kurumsal Doküman Asistanı — Enterprise RAG + Agentic

Kurumsal PDF dokümanları üzerinde soru-cevap sunan, **provider-agnostik** bir RAG
(Retrieval Augmented Generation) sistemi + **Semantic Kernel** tabanlı agentic doğrulama
katmanı. .NET 10, Clean Architecture, PostgreSQL + pgvector.

> **Tasarım ilkesi:** LLM ve embedding sağlayıcısı bir interface'in arkasında. Bugün OpenAI;
> yarın Azure OpenAI (KVKK/TR bölgesi) veya on-prem Ollama'ya iş mantığına dokunmadan geçilebilir.

---

## Mimari (Clean Architecture)

```
KurumsalRAG.sln
 └─ src/
     ├─ KurumsalRAG.Domain/         # Entity + value object'ler, hiçbir dış bağımlılık YOK
     ├─ KurumsalRAG.Application/    # Port interface'leri (soyutlamalar) + DTO + config
     ├─ KurumsalRAG.Infrastructure/ # Adapter'lar: OpenAI, pgvector, SK ajanları, PDF, guard
     └─ KurumsalRAG.Api/            # Controller'lar, SSE, health, DI kompozisyon kökü
```

**Bağımlılık yönü:** `Api → Infrastructure → Application → Domain`. Domain hiçbir şeye bağlı
değil. Application yalnızca **port** tanımlar; somut **adapter**'lar Infrastructure'da.

### Portlar (Application) → Adapter'lar (Infrastructure)

| Port (soyutlama) | Bugünkü adapter | Yarın takılabilecek |
|---|---|---|
| `ILlmProvider` | `OpenAiLlmProvider` | Azure OpenAI, Ollama |
| `IEmbeddingProvider` | `OpenAiEmbeddingProvider` | Azure, on-prem embedding |
| `IVectorStore` | `PgVectorStore` (pgvector) | Qdrant, Milvus, Azure AI Search |
| `IReranker` | `LlmReranker` | cross-encoder, Cohere Rerank |
| `IPromptGuard` | `RuleBasedPromptGuard` | LLM-based classifier, içerik filtreleme servisi |
| `IFaithfulnessEvaluator` | `FaithfulnessCheckerAgent` (SK) | başka critic modeli |

Her port'un bir de "no-op / passthrough" varsayılanı vardır (`Services/Defaults/`) — soyutlamanın
gerçekten değiştirilebilir olduğunun kanıtı.

### Veri akışı

```
Yükleme:  PDF ─(PdfPig)→ metin ─(TextChunker)→ chunk'lar ─(embed)→ pgvector (HNSW, cosine)
Sorgu:    soru ─(PromptGuard)→ ─(embed)→ ─(pgvector top-k)→ ─(LlmReranker top-n)→
          ─(RagPromptBuilder)→ ─(LLM)→ cevap ─(FaithfulnessChecker)→ groundedness skoru
Agentic:  RagOrchestrator: retrieve → answer → check → (eşik altıysa 1x reflection)
```

---

## Fazlar — ne yapıldı

| Faz | İçerik |
|---|---|
| **1 — Çekirdek RAG (MVP)** | docker-compose pgvector + HNSW index; PDF ingestion (chunk+embed+store); non-stream sorgu; kaynak-atıflı (`[chunk:N]`) cevap; "dokümanlarda yok" davranışı |
| **2 — Rerank + SSE** | `LlmReranker` (top-k=20 → top-n=4, tek LLM çağrısında puanlama); SSE token-token streaming (`GET /api/chat/stream`) |
| **3 — Semantic Kernel agentic** | SK `Kernel` + `IChatCompletionService`; RetrievalAgent (SK plugin), AnswerAgent, FaithfulnessCheckerAgent (gerçek groundedness), RagOrchestrator (generator-critic reflection loop, max 1) |
| **4 — Güvenlik + Evaluation** | `RuleBasedPromptGuard` (prompt injection tespiti, block/sanitize); system/user yapısal ayrımı; tek yapılandırılmış eval log (guard + chunks + faithfulness + token + maliyet) |

---

## Çalıştırma

### Gereksinimler
- .NET 10 SDK
- Docker (pgvector için)
- Bir OpenAI API anahtarı

### Adımlar

```bash
# 1) Secret'ları hazırla
cp .env.example .env
# .env içine gerçek OPENAI_API_KEY değerini yaz (.env .gitignore'dadır, commit'lenmez)

# 2) pgvector'ı ayağa kaldır (extension + şema + HNSW index otomatik kurulur)
docker compose up -d

# 3) API'yi çalıştır
dotnet run --project src/KurumsalRAG.Api
```

### Uçlar (endpoints)

| Metot & yol | İş |
|---|---|
| `POST /api/documents` (multipart `file`) | PDF yükle → chunk + embed + store |
| `POST /api/chat` `{ "question": "..." }` | Non-stream, kaynak-atıflı cevap + observability |
| `GET  /api/chat/stream?question=...` | SSE, token-token cevap |
| `GET  /health` | DB + provider erişilebilirlik |
| `GET  /api/diagnostics/rerank?question=...` | Rerank öncesi/sonrası sıralama (yan yana) |
| `GET  /api/diagnostics/faithfulness?question=...` | Tam agentic akış + groundedness |
| `POST /api/diagnostics/faithfulness/check` | İzole checker (kurgulanan cevabı denetle) |
| `GET  /api/diagnostics/eval?question=...` | Tek yapılandırılmış eval kaydı |

### Örnek

```bash
curl -X POST http://localhost:5264/api/documents -F "file=@politika.pdf;type=application/pdf"
curl -X POST http://localhost:5264/api/chat -H "Content-Type: application/json" \
     -d '{"question":"Çalışanlar kaç gün uzaktan çalışır?"}'
```

---

## Konfigürasyon (`appsettings.json`)

Hiçbir davranış hard-code değil — hepsi konfigüre edilebilir:

```jsonc
"OpenAI": {
  "EmbeddingModel": "text-embedding-3-small",   // 1536 boyut
  "EmbeddingDimensions": 1536,
  "ChatModel": "gpt-4o-mini",
  "Pricing": { "ChatInputPerMillion": 0.15, "ChatOutputPerMillion": 0.60, "EmbeddingPerMillion": 0.02 }
},
"Rag": {
  "Chunking":     { "MaxTokens": 180, "OverlapRatio": 0.15 },
  "Retrieval":    { "TopK": 20, "TopN": 4 },
  "Faithfulness": { "Threshold": 0.7, "Enabled": true },
  "Security":     { "PromptGuardAction": "SanitizeAndWarn" }  // veya "Block"
}
```

**Secret yönetimi:** API anahtarı yalnızca `.env` / ortam değişkeninde (`OPENAI_API_KEY`).
`appsettings`'te anahtar yok. `.env` `.gitignore`'da.

---

## Provider'ı değiştirme

### → Azure OpenAI (KVKK / Türkiye bölgesi)
1. `KurumsalRAG.Infrastructure/Agents/KernelFactory.cs`: `AddOpenAIChatCompletion` →
   `AddAzureOpenAIChatCompletion(deployment, endpoint, apiKey)`.
2. `OpenAiEmbeddingProvider` / `OpenAiLlmProvider`'daki `BaseUrl`'ü Azure endpoint'ine çevir
   (ya da Azure için ayrı adapter yaz ve `DependencyInjection`'da o adapter'ı kaydet).
3. İş mantığı (`RagQueryService`, ajanlar, controller'lar) **hiç değişmez** — port'lar aynı.

### → On-prem Ollama
1. `ILlmProvider` ve `IEmbeddingProvider` için `OllamaLlmProvider` / `OllamaEmbeddingProvider`
   yaz (Ollama'nın `/api/chat` ve `/api/embeddings` uçlarına HTTP).
2. `DependencyInjection.AddInfrastructure` içinde OpenAI adapter kaydını Ollama ile değiştir.
3. Embedding boyutu değişirse `db/init.sql`'deki `vector(1536)` ve `EmbeddingDimensions`'ı güncelle.

### → Farklı vektör deposu (Qdrant/Milvus/Azure AI Search)
`IVectorStore` arkasına yeni adapter yaz, `DependencyInjection`'da tek satır değiştir.

---

## Güvenlik yaklaşımı (savunma derinliği)

1. **Prompt injection guard (`IPromptGuard`).** Kural tabanlı, TR + EN. Üç kategori:
   talimat ezme ("önceki talimatları unut" / "ignore previous instructions"), rol/system
   sızdırma ("act as" / "system prompt'unu göster"), delimiter/çıkış kaçışı (sahte
   `CONTEXT:` / `SORU:` / `###` blokları). Sonuç: `{ IsSuspicious, MatchedRule, Action }`.
   Davranış `appsettings`'ten seçilir: **`Block`** (isteği reddet) veya **`SanitizeAndWarn`**
   (delimiter'ları nötrleştir, işaretle, akışa devam et).
2. **System/User yapısal ayrımı.** Asıl güvence guard değil, `RagPromptBuilder`'ın kullanıcı
   sorusunu system talimatından **net delimiter'larla** (`<<< >>>`) ayrı bir `user` mesajında
   tutmasıdır. Böylece context'e gömülü bir "SİSTEM: şunu yap" enjeksiyonu talimat seviyesinde
   yorumlanmaz — model onu veri olarak görür. (Test edildi: gömülü injection uygulanmadı.)
3. **Groundedness denetimi (`IFaithfulnessEvaluator`).** Üretilen cevabın context'e dayanıp
   dayanmadığını (hallucination) denetler; eşik altında reflection ya da uyarı.
4. **Secret yönetimi.** Anahtar sadece `.env`'de; koda/appsettings'e gömülmez.
5. **Dayanıklılık.** OpenAI 429/geçici hatalar için Polly ile üstel geri çekilmeli retry.

> **Not:** Kural tabanlı guard ilk katmandır; tek başına yeterli sayılmaz. `IPromptGuard`
> soyutlaması sayesinde ileride LLM tabanlı bir injection-classifier veya harici içerik
> filtreleme servisi aynı portun arkasına takılabilir.

---

## Gözlemlenebilirlik (observability)

Her cevap için `RagObservability` / eval kaydı: kullanılan chunk id'leri + retrieval skorları,
faithfulness skoru + geçti/kaldı + desteklenmeyen iddialar, guard sonucu, prompt/completion/
embedding token ve **tahmini USD maliyet**. `GET /api/diagnostics/eval` tek yapılandırılmış
kayıt döndürür; ayrıca her istek tek satır yapılandırılmış log üretir (`EVAL q=... costUsd=...`).

---

## Teknoloji yığını

.NET 10 · ASP.NET Core · Microsoft.SemanticKernel · PostgreSQL + pgvector (Npgsql + Pgvector) ·
UglyToad.PdfPig · Polly · OpenAI (`text-embedding-3-small`, `gpt-4o-mini`)
