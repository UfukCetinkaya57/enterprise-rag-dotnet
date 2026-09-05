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

# 3) API'yi çalıştır (startup'ta örnek seed doküman otomatik ingest edilir)
dotnet run --project src/KurumsalRAG.Api

# 4) Tarayıcıda demo arayüzü: http://localhost:5264/
#    (seed doküman yüklü — hemen soru sorabilirsiniz)
```

> Şema güncellendiyse (session_id kolonları, cache/usage tabloları) mevcut volume'ü sıfırlayın:
> `docker compose down -v && docker compose up -d`.

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

## Production considerations (live demo)

Depo herkese açık, anonim bir canlı demo olarak yayımlanabilir. Aşağıdaki zırh, kötüye
kullanımı ve maliyeti sınırlarken Clean Architecture'ı bozmaz — her biri bir **port**
arkasında, davranış `appsettings` "Demo" bölümünden ayarlanır.

| Karar | Ne yapar | Gerekçe |
|---|---|---|
| **Session izolasyonu** | httpOnly cookie ile anonim session; her chunk `session_id` ile etiketlenir. Retrieval yalnızca kullanıcının kendi + `seed` chunk'larını görür. | Bir kullanıcının yüklediği doküman başka kullanıcıya sızmaz. Cookie httpOnly (XSS'e karşı JS erişemez). |
| **Seed doküman** | Startup'ta kurgusal bir şirket politikası PDF'i `session_id='seed'` ile ingest edilir. | Demo kullanıcısı hiçbir şey yüklemeden hemen soru sorabilir. |
| **Doküman TTL** | Saatlik `BackgroundService`, seed hariç 24 saatten eski dokümanları siler. | Public demoda veri birikmesini ve kalıcı depolama yükünü önler. |
| **Günlük token bütçesi** | `daily_usage` tablosunda restart-dayanıklı sayaç. Aşımda LLM çağrılmaz; nazik "limited" cevabı. | OpenAI maliyet tavanı — kötüye kullanım faturayı patlatamaz. |
| **Kill switch** | `Demo:Enabled=false` → tüm chat uçları "limited" döner. | Sorun anında tek konfigürasyonla üretimi durdurma. |
| **IP kotaları** | .NET yerleşik rate limiting: IP başına günlük sorgu (30) ve upload (3). `ForwardedHeaders` ile Nginx arkasında gerçek IP. | Tek bir IP'nin servisi tüketmesini engeller. |
| **Session kotaları** | Session başına en fazla 2 doküman, toplam 20 MB. | Depolama ve embedding maliyetini kullanıcı başına sınırlar. |
| **Yanıt cache'i** | `(session + normalize soru)` anahtarı, DB tablosu, TTL 24s. Hit'te LLM'e gidilmez; cevap `cached` işaretlenir. | Tekrarlı sorularda gecikmeyi ve maliyeti düşürür. |
| **Upload sertleştirme** | `application/pdf` + magic-byte (`%PDF-`), 10 MB, 50 sayfa sınırı. | Sahte/zararlı dosya ve kaynak tüketimini engeller. |
| **Global hata yönetimi** | `IExceptionHandler` + ProblemDetails; stack trace kullanıcıya sızmaz. | Bilgi sızıntısını önler, kullanıcı-dostu mesaj verir. |

**Yeni portlar (Application) → adapter (Infrastructure):** `ISessionAccessor` → `HttpSessionAccessor`
(Api), `IResponseCache` → `PgResponseCache`, `ITokenBudgetGuard` → `PgTokenBudgetStore`,
`ISessionQuota` → `PgSessionQuota`. Cache'i Redis'e taşımak = tek adapter değişikliği.

**Yapılandırma (`appsettings.json` → `Demo`):**

```jsonc
"Demo": {
  "Enabled": true,              // kill switch
  "SeedSessionId": "seed",
  "DailyTokenBudget": 200000,   // günlük global token tavanı
  "DocumentTtlHours": 24,
  "Ip":      { "QueriesPerDay": 30, "UploadsPerDay": 3 },
  "Session": { "MaxDocuments": 2, "MaxTotalBytes": 20971520 },
  "Cache":   { "TtlHours": 24 },
  "Upload":  { "MaxBytes": 10485760, "MaxPages": 50 }
}
```

**Bilinen sınırlamalar:** Rate limiter penceresi süreç-içi (in-memory) tutulur; süreç
yeniden başlarsa IP pencereleri sıfırlanır (token bütçesi ise DB'de kalıcıdır). Token/maliyet
tahmini stream yolunda karakter-tabanlı kestirimdir (SK usage döndürmez); non-stream yolda
gerçek OpenAI usage kullanılır. Session anonim cookie tabanlıdır; cookie silinirse yeni session
başlar (kimlik doğrulama yoktur — demo amaçlıdır).

---

## Deployment (rag.ufukcetinkaya.com)

Hedef: Ubuntu + Nginx (üzerinde **başka siteler de barınıyor** — bu kurulum onlara
dokunmaz; `rag.ufukcetinkaya.com` için **ayrı** bir Nginx server bloğu ekler).

**Mimari:** İnternet → Nginx (443, TLS) → `127.0.0.1:8092` (Docker: API) → `postgres` (Docker, dışarı kapalı).

### Ön koşullar
- Sunucuda Docker + Docker Compose, Nginx, certbot (`sudo apt install certbot`).
- **DNS:** `rag.ufukcetinkaya.com` A kaydı sunucunun public IP'sine (bunu sen giriyorsun).
- Port **8092** sunucuda boş olmalı (aşağıda kontrol var). Doluysa: `.env.prod`'daki
  `API_PORT` + `deploy/nginx/rag.ufukcetinkaya.com.conf`'daki upstream portunu birlikte değiştir.

### Sunucuda çalıştırılacak komutlar (sırayla)

```bash
# 0) DNS'in yayıldığını doğrula (sunucu IP'sini göstermeli)
dig +short rag.ufukcetinkaya.com

# 1) Portun boş olduğunu kontrol et (çıktı BOŞ olmalı)
sudo ss -ltnp | grep ':8092' || echo "8092 boş"

# 2) Repoyu al
git clone https://github.com/UfukCetinkaya57/enterprise-rag-dotnet.git
cd enterprise-rag-dotnet

# 3) Prod secret'ları hazırla
cp .env.prod.example .env.prod
nano .env.prod   # OPENAI_API_KEY ve güçlü POSTGRES_PASSWORD gir (.env.prod git-ignored)

# 4) API + pgvector'ı ayağa kaldır (ilk kurulumda ŞEMA yeni → temiz volume otomatik oluşur)
docker compose -f docker-compose.prod.yml --env-file .env.prod up -d --build
docker compose -f docker-compose.prod.yml --env-file .env.prod ps
curl -s http://127.0.0.1:8092/health          # → Healthy

# 5) Nginx: certbot webroot dizini + config'i kopyala
sudo mkdir -p /var/www/certbot
sudo cp deploy/nginx/rag.ufukcetinkaya.com.conf /etc/nginx/sites-available/rag.ufukcetinkaya.com
#    ÖNEMLİ: Sertifika henüz YOK. Dosyayı aç, "server { listen 443 ... }" bloğunun TAMAMINI
#    geçici olarak yorum satırı yap (her satır başına #). Sadece 80 bloğu aktif kalsın:
sudo nano /etc/nginx/sites-available/rag.ufukcetinkaya.com
sudo ln -s /etc/nginx/sites-available/rag.ufukcetinkaya.com /etc/nginx/sites-enabled/
sudo nginx -t && sudo systemctl reload nginx     # yalnızca 80 bloğu → geçerli

# 6) Let's Encrypt sertifikası (webroot; Nginx'i durdurmaz, diğer siteleri etkilemez)
sudo certbot certonly --webroot -w /var/www/certbot -d rag.ufukcetinkaya.com \
     --agree-tos -m sunssquad988@gmail.com --no-eff-email

# 7) 443 bloğunun yorumlarını KALDIR (5. adımda yorumladığın satırların başındaki #'leri sil)
sudo nano /etc/nginx/sites-available/rag.ufukcetinkaya.com
sudo nginx -t && sudo systemctl reload nginx     # artık TLS aktif

# 8) Smoke test (dışarıdan, TLS ile)
./deploy/smoke-test.sh                          # BASE=https://rag.ufukcetinkaya.com (varsayılan)
```

> **Not (5–7. adım):** `deploy/nginx/rag.ufukcetinkaya.com.conf` 443 bloğunu elle içerir ve
> certbot'u yalnızca **sertifika almak** için (`certonly --webroot`) kullanır — böylece certbot
> config'e dokunmaz, location/header'lar bizim kontrolümüzde kalır. Sertifika ilk kez alınmadan
> önce 443 bloğu `nginx -t`'yi bozar; o yüzden sırayla önce 80, sonra sertifika, sonra 443.

### Sertifika yenileme
certbot paketi `certbot.timer`'ı otomatik kurar (günde 2 kez dener, süresi %30 kalınca yeniler).
Yenileme sonrası Nginx'in yeni sertifikayı alması için deploy hook:
```bash
echo 'systemctl reload nginx' | sudo tee /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh
sudo chmod +x /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh
sudo certbot renew --dry-run     # yenileme provasını doğrula
```

### Güncelleme (yeni sürüm çıktığında)
```bash
cd enterprise-rag-dotnet && git pull
docker compose -f docker-compose.prod.yml --env-file .env.prod up -d --build
# Şema DEĞİŞMEDİYSE volume korunur; değiştiyse "Bilinen riskler"e bak.
```

### Bilinen riskler ve rollback

| Risk | Etki / belirti | Önlem / geri alma |
|---|---|---|
| **Port 8092 dolu** | API başlamaz / başka servis kırılır | 1. adımdaki `ss` kontrolü; doluysa `.env.prod` + Nginx conf'ta portu değiştir |
| **DNS henüz yayılmadı** | certbot HTTP-01 başarısız | `dig +short` ile doğrula, yayılmayı bekle, certbot'u tekrarla |
| **Şema değişikliği** | Yeni sürümde tablo/kolon eklenirse eski volume uyumsuz olabilir | `init.sql` yalnızca boş volume'de çalışır. Gerekirse: `docker compose -f docker-compose.prod.yml --env-file .env.prod down -v` (demo verisi silinir, seed yeniden ingest edilir) → `up -d --build` |
| **Nginx conf hatası** | `nginx -t` fail / reload reddedilir | reload öncesi hep `nginx -t`. Bozulursa: `sudo rm /etc/nginx/sites-enabled/rag.ufukcetinkaya.com && sudo systemctl reload nginx` → **diğer siteler etkilenmeden** eski haline döner |
| **Kötü sürüm / hatalı deploy** | API 5xx | `git checkout <önceki-tag> && docker compose ... up -d --build`; ya da anlık durdurma: `docker compose -f docker-compose.prod.yml --env-file .env.prod stop api` |
| **Maliyet / kötüye kullanım** | Beklenmedik token harcaması | Anında kill switch: `.env.prod`'a `Demo__Enabled=false` ekle → `up -d` (tüm chat "limited" döner); günlük bütçe zaten `DailyTokenBudget` ile kaplı |

**Tam geri çekilme (demo'yu kaldır, diğer siteler kalsın):**
```bash
sudo rm -f /etc/nginx/sites-enabled/rag.ufukcetinkaya.com && sudo systemctl reload nginx
docker compose -f docker-compose.prod.yml --env-file .env.prod down    # -v eklemezsen veri kalır
```

### Doğrulanan güvenlik davranışları
- Diagnostics çift katman kapalı: uygulama (Production'da `DiagnosticsEnabled=false` → 404) **+** Nginx (`/api/diagnostics` → 404).
- `/health` sığ public; `/health?deep=true` yalnızca **loopback** (container içinden). Nginx'in arkasında dışarıdan deep çağrılamaz.
- Rate limiter **DB tabanlı** — API restart'ında sayaç sıfırlanmaz (doğrulandı: 1→2).
- Nginx `X-Forwarded-For` → API gerçek client IP'yi okur (doğrulandı: farklı IP'ler ayrı sayılır); `ForwardLimit=1` ile client XFF spoofing engellenir.

---

## Geliştirici notu — OneDrive

Depo `OneDrive\Desktop\RAG` altında. OneDrive senkronizasyonu ile git bazen `bin/`, `obj/`
gibi dizinlerde dosya kilidi çakışması yaşayabilir (bu dizinler zaten `.gitignore`'da). Bir
sorun görülürse OneDrive senkronunu kısa süreliğine duraklatmak yeterlidir; depoyu taşımak
zorunlu değildir.
