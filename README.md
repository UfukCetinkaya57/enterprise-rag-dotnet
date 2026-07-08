# Enterprise RAG + Agentic — Kurumsal Doküman Asistanı

Kurumsal PDF dokümanları üzerinde kaynak-atıflı soru-cevap sunan, **provider-agnostik** bir
RAG (Retrieval Augmented Generation) sistemi ve **Semantic Kernel** tabanlı agentic doğrulama
katmanı. .NET 8 / ASP.NET Core, Clean Architecture, PostgreSQL + pgvector.

Kullanıcı bir PDF yükler → sistem metni chunk'lar, embed eder, vektör deposuna yazar. Kullanıcı
soru sorar → ilgili chunk'lar getirilir, yeniden sıralanır (rerank), context'e dayalı cevap
üretilir ve **stream** edilir. Bir **faithfulness (groundedness) checker** ajanı cevabın gerçekten
getirilen context'e dayandığını denetler; bir **prompt injection guard** kullanıcı girdisini
işlemeden önce süzer.

---

## Mimari

**Clean Architecture — 4 katman.** Bağımlılık yönü içe doğru: `Api → Infrastructure → Application → Domain`.
Domain hiçbir şeye bağlı değildir; Application yalnızca **port** (interface) tanımlar, somut
**adapter**'lar Infrastructure'dadır. Bu, sağlayıcı değişimini tek bir katmanla sınırlar.

```
┌────────────────────────────────────────────────────────────────────┐
│  Api            Controller'lar · SSE · /health · DI kompozisyon kökü │
├────────────────────────────────────────────────────────────────────┤
│  Infrastructure OpenAI · pgvector · SK ajanları · PDF · guard        │  (adapter'lar)
├────────────────────────────────────────────────────────────────────┤
│  Application    ILlmProvider · IVectorStore · IReranker · ...        │  (portlar)
├────────────────────────────────────────────────────────────────────┤
│  Domain         DocumentChunk · RetrievedContext · ScoredChunk       │  (bağımlılıksız)
└────────────────────────────────────────────────────────────────────┘
```

### Ingestion pipeline (`POST /api/documents`)

```
PDF ──PdfPig──▶ metin ──TextChunker──▶ chunk'lar ──OpenAI embed──▶ pgvector
                          (~180 token,                (1536-boyut)   (HNSW, cosine)
                           %15 overlap)
```

### Query pipeline (`POST /api/chat` · `GET /api/chat/stream`)

```
soru ─▶ PromptGuard ─▶ embed ─▶ pgvector top-k ─▶ LlmReranker top-n ─▶ prompt ─▶ LLM ─▶ cevap
        (injection)             (k=20, cosine)     (n=4)                          │
                                                                                   ▼
                                                        FaithfulnessChecker ── groundedness skoru
                                                        (eşik altıysa → reflection: 1x strict retry)
```

Agentic akış **RagOrchestrator** tarafından yürütülür: `retrieve → answer → check → (reflection)`.

---

## Teknoloji yığını

- **.NET 8**, ASP.NET Core Web API
- **Microsoft.SemanticKernel** — agentic orkestrasyon (`IChatCompletionService`, plugin fonksiyonları)
- **PostgreSQL + pgvector** — vektör deposu, **HNSW** index + cosine benzerliği (Npgsql + Pgvector)
- **OpenAI** — `text-embedding-3-small` (1536), `gpt-4o-mini`
- **UglyToad.PdfPig** — PDF metin çıkarımı
- **Polly** — 429/geçici hata için üstel geri çekilmeli retry
- **SSE** (Server-Sent Events) — token-token cevap akışı

> Not: Depo `net10.0` hedefiyle geliştirildi (mevcut SDK); tasarım ve API'ler .NET 8 ile birebir
> uyumludur — `TargetFramework`'ü `net8.0` yapmak yeterlidir.

---

## Öne çıkan tasarım kararları

| Karar | Gerekçe |
|---|---|
| **Provider soyutlaması** (`ILlmProvider`, `IEmbeddingProvider`, `IVectorStore`) | LLM/embedding/vektör deposu port arkasında. OpenAI → Azure OpenAI (KVKK/TR bölgesi) veya on-prem Ollama geçişi **iş mantığına dokunmadan**, tek adapter değişimiyle. |
| **İki aşamalı retrieval** (top-k + rerank) | Cosine benzerliği tek başına yakın skorları ayıramıyor. `LlmReranker`, top-k=20 adayı **tek** LLM çağrısında 0–1 alaka puanlayıp top-n=4'e daraltır. İki dokümanlı çakışan senaryoda (çalışan "3 gün" vs stajyer "hak yok") doğru dokümanı öne çıkarır. |
| **Faithfulness checker + reflection loop** | Ayrı bir critic ajan, cevaptaki her iddianın context'te desteklenip desteklenmediğini 0–1 skorlar ve desteklenmeyen iddiaları listeler. Skor eşiğin altındaysa AnswerAgent bir kez "strict" modda yeniden üretir (generator-critic); yine düşükse cevaba şeffaf uyarı eklenir. Döngü 1 ile sınırlı. |
| **Prompt injection guard + yapısal ayrım** | `RuleBasedPromptGuard` üç kategoriyi (talimat ezme, rol/system sızdırma, delimiter kaçışı) TR+EN yakalar. Asıl güvence ise kullanıcı sorusunun system talimatından net delimiter'larla **ayrı bir `user` mesajında** tutulmasıdır — context'e gömülü komutlar talimat olarak yorumlanmaz. |
| **Graceful degradation** | Reranker bir *iyileştirme* katmanıdır; LLM hatası/bozuk JSON durumunda sessizce cosine sırasına düşer, retrieval'ı bloklamaz. Guard `Block`/`SanitizeAndWarn` modu, faithfulness `Enabled` bayrağı — hepsi config'ten. |
| **Konfigürasyon disiplini** | Model isimleri, chunk boyutu, top-k/n, faithfulness eşiği, guard davranışı, fiyatlandırma — hepsi `appsettings.json`'dan. Hard-code yok. Secret yalnızca `.env`'de. |

### Port → adapter eşlemesi

| Port | Bugünkü adapter | Yarın takılabilecek |
|---|---|---|
| `ILlmProvider` | `OpenAiLlmProvider` | Azure OpenAI, Ollama |
| `IEmbeddingProvider` | `OpenAiEmbeddingProvider` | Azure, on-prem |
| `IVectorStore` | `PgVectorStore` | Qdrant, Milvus, Azure AI Search |
| `IReranker` | `LlmReranker` | cross-encoder, Cohere Rerank |
| `IPromptGuard` | `RuleBasedPromptGuard` | LLM-based classifier |
| `IFaithfulnessEvaluator` | `FaithfulnessCheckerAgent` (SK) | başka critic modeli |

---

## Nasıl çalıştırılır

**Gereksinimler:** .NET SDK, Docker, bir OpenAI API anahtarı.

```bash
# 1) Secret'ları hazırla — .env.example placeholder içerir, gerçek key ASLA commit'lenmez
cp .env.example .env
#    .env içindeki OPENAI_API_KEY değerini kendi anahtarınla değiştir
#    (.env .gitignore'dadır)

# 2) pgvector'ı ayağa kaldır — extension + şema + HNSW index otomatik kurulur (db/init.sql)
docker compose up -d

# 3) API'yi çalıştır
dotnet run --project src/KurumsalRAG.Api
```

### Örnek istekler

```bash
# Doküman yükle
curl -X POST http://localhost:5264/api/documents \
     -F "file=@politika.pdf;type=application/pdf"

# Context-içi soru → kaynak-atıflı cevap
curl -X POST http://localhost:5264/api/chat \
     -H "Content-Type: application/json" \
     -d '{"question":"Çalışanlar haftada kaç gün uzaktan çalışabilir?"}'
# → "Çalışanlar haftada en fazla 3 (üç) gün uzaktan çalışabilir. [chunk:1]"

# Context-dışı soru → uydurmaz
curl -X POST http://localhost:5264/api/chat \
     -H "Content-Type: application/json" \
     -d '{"question":"Şirket araç tahsisi var mı?"}'
# → "Sağlanan dokümanlarda bu bilgi bulunmuyor."

# SSE ile token-token akış
curl -N "http://localhost:5264/api/chat/stream?question=Ev%20ofisi%20ekipman%20deste%C4%9Fi%20ne%20kadar%3F"
```

### Endpoint'ler

| Metot & yol | İş |
|---|---|
| `POST /api/documents` | PDF yükle → chunk + embed + store |
| `POST /api/chat` | Non-stream, kaynak-atıflı cevap + observability |
| `GET  /api/chat/stream` | SSE, token-token cevap |
| `GET  /health` | DB + provider erişilebilirlik |
| `GET  /api/diagnostics/rerank` | Rerank öncesi/sonrası sıralama (yan yana) |
| `GET  /api/diagnostics/faithfulness` | Tam agentic akış + groundedness |
| `POST /api/diagnostics/faithfulness/check` | İzole checker (kurgulanan cevabı denetle) |
| `GET  /api/diagnostics/eval` | Tek yapılandırılmış eval kaydı (guard + chunk + faithfulness + token + maliyet) |

---

## Provider'ı değiştirme

**→ Azure OpenAI (KVKK / TR bölgesi):** `KernelFactory`'de `AddOpenAIChatCompletion` →
`AddAzureOpenAIChatCompletion`; OpenAI adapter'larının `BaseUrl`'ünü Azure endpoint'ine çevir.
İş mantığı değişmez.

**→ On-prem Ollama:** `ILlmProvider` / `IEmbeddingProvider` için Ollama adapter'ları yaz,
`DependencyInjection`'da kaydı değiştir. Embedding boyutu değişirse `db/init.sql`'deki
`vector(1536)` ve `EmbeddingDimensions`'ı güncelle.

**→ Farklı vektör deposu:** `IVectorStore` arkasına yeni adapter, DI'da tek satır.

---

## Güvenlik yaklaşımı

- **Secret yönetimi.** API anahtarı yalnızca `.env` / ortam değişkeninde (`OPENAI_API_KEY`).
  `appsettings`'te anahtar yok; `.env` `.gitignore`'da. Gerçek anahtar hiçbir zaman commit'lenmez.
- **Prompt injection guard.** Kural tabanlı ilk katman (block / sanitize, config'ten). Asıl
  güvence: system/user içeriğinin yapısal ayrımı (context'e gömülü komut talimat sayılmaz).
- **On-prem / KVKK.** Provider soyutlaması sayesinde tüm LLM/embedding trafiği Azure'un TR
  bölgesine veya tamamen on-prem Ollama'ya taşınabilir — veri-ikameti senaryosu için hazır.
- **Groundedness denetimi.** Faithfulness checker hallucination'ı yakalar; düşük skorlu
  cevaplar reflection'a girer ya da uyarıyla işaretlenir.

---

## Geliştirici notu — OneDrive

Depo `OneDrive\Desktop\RAG` altında. OneDrive senkronizasyonu ile git bazen `bin/`, `obj/`
gibi dizinlerde dosya kilidi çakışması yaşayabilir (bu dizinler zaten `.gitignore`'da). Bir
sorun görülürse OneDrive senkronunu kısa süreliğine duraklatmak yeterlidir; depoyu taşımak
zorunlu değildir.
