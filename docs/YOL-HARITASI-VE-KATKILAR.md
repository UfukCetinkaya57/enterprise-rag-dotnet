# Enterprise RAG (.NET) — Yol Haritası ve Katkılar

Bu dosya, projeye eklenen her önemli entegrasyonun **kaydıdır**. Her madde şu formatta:
**Ne yaptım · Neden (problem) · Nasıl (teknik) · Mülakatta/paylaşımda nasıl anlatılır.**

> Amaç: portfolyo anlatısı, LinkedIn/blog gönderisi taslağı ve mülakat hazırlığı için tek kaynak.
> Proje: provider-agnostik RAG + agentic sistem · .NET 10 · Clean Architecture (Domain/Application/Infrastructure/Api) · PostgreSQL + pgvector · Semantic Kernel · Docker · canlı demo.

---

## Özet (bir bakışta)

| # | Entegrasyon | Durum | Özü |
|---|-------------|-------|-----|
| 1 | Hybrid Search (vektör + keyword + RRF) | ✅ Canlı | Anlamsal + tam-eşleşme aramayı RRF ile birleştir |
| 2 | Evaluation Harness (RAGAS tarzı) | ✅ | Recall@k, MRR, answer accuracy, refusal, faithfulness |
| 3 | Dayanıklılık (per-attempt timeout + retry) | ✅ Canlı | Gemini 503'te 30sn hang → hızlı nazik hata |
| 4 | BYOK + çoklu-anahtar rotasyonu | ✅ Canlı | Kullanıcı kendi key'i; havuz round-robin + failover |
| 5 | Multi-turn konuşma hafızası + query rewriting | ✅ Canlı | "Peki ya X?" takip soruları çalışır |
| 6 | Çok-sağlayıcılı BYOK (OpenAI/Gemini/Grok) | ✅ Canlı | Kullanıcı cevap modelini seçer; embedding sabit |
| 7 | Swagger/OpenAPI + CI/CD + integration test | ✅ Canlı | Self-dokümante API + yeşil CI + gerçek-DB testleri |
| 8 | Redis cache + Cohere cross-encoder reranker | ✅ | Dağıtık cache + gerçek cross-encoder, ikisi de config-seçilebilir |
| 9 | Parent-Document Retrieval (small-to-big) | ✅ | Küçük child ile ara, LLM'e büyük parent bağlamı ver |

**Test durumu:** 51 unit + 5 integration test, GitHub Actions'ta otomatik (yeşil).
**Mimari ilke:** Her özellik bir *port* (interface) arkasında; somut sağlayıcı değişse iş mantığı değişmez.

---

## 1. Hybrid Search — vektör + keyword + RRF

**Problem:** Saf vektör (anlamsal) arama, tam-eşleşmesi gereken terimleri (kod, kısaltma, "VPN", özel isim) kaçırabilir. Saf keyword arama ise anlamı kaçırır.

**Çözüm:** İki aramayı paralel çalıştırıp **Reciprocal Rank Fusion (RRF)** ile birleştirdim:
- Vektör arama (pgvector, cosine) → anlamsal yakınlık
- Keyword arama (PostgreSQL full-text, `ts_rank`) → tam terim eşleşmesi
- RRF: skorları değil **sıraları** birleştirir → `Σ 1/(k+rank)`, k=60. Ölçek/ağırlık ayarı gerektirmez.

**Teknik incelikler:**
- `websearch_to_tsquery` tüm kelimeleri AND'liyordu ("yıllık izin kaç gün" → 4 kelime birden gerekiyordu, 0 sonuç). Lexeme'leri `tsvector_to_array(...)` ile OR'ladım.
- Türkçe karakter uyumu: seed PDF ASCII ("yillik"), kullanıcı "yıllık" yazıyor → `unaccent` extension + `immutable_unaccent` wrapper ile eşleştirdim.
- Generated column (`content_tsv`) + GIN index ile keyword araması hızlı.

**Mülakatta:** "RAG'te retrieval kalitesini nasıl artırdın?" → "Hybrid search: vektör anlamı, keyword tam-eşleşmeyi yakalar; RRF ikisini skor-ölçeğinden bağımsız birleştirir. RRF'in güzelliği ağırlık tuning'i gerektirmemesi."

---

## 2. Evaluation Harness — "RAG'imi ölçebiliyorum"

**Problem:** Bir RAG sistemini "iyileştirdim" demek için ölçmek gerek. Regresyonu (bir değişiklik kaliteyi düşürdü mü?) yakalamanın tek yolu metrik.

**Çözüm:** Altın soru seti (golden set) + otomatik metrik hesaplayan bir harness:
- **Retrieval:** Recall@k (beklenen parça getirildi mi), MRR (kaçıncı sırada)
- **Generation:** answer accuracy (doğru gerçeği içeriyor mu), refusal accuracy (context dışı soruyu reddediyor mu), faithfulness (groundedness)
- 12 soru: 9 context-içi + 3 context-dışı (reddetmeli).

**Ölçülen sonuç (canlı):** Recall=%100, MRR=1.0, answer accuracy=%100, faithfulness≈0.95.

**Teknik incelik:** 12 soru × 2 LLM çağrısı free-tier RPM'i aşıyordu → sorular arası `delayMs` parametresi ekledim.

**Mülakatta:** "RAG kalitesini nasıl ölçersin?" → "RAGAS tarzı bir harness: Recall@k ve MRR ile retrieval'ı, faithfulness ile halüsinasyonu, refusal accuracy ile 'bilmiyorum diyebiliyor mu'yu ölçüyorum. Golden set regresyon kalkanı."

---

## 3. Dayanıklılık — per-attempt timeout + akıllı retry

**Problem (canlı bug):** Chat bazen 30 saniye takılıp boş dönüyordu. Kök neden: Google'ın `gemini-flash-lite` endpoint'i zaman zaman **503** (aşırı yük) dönüyor ve bu 503'ü ~20 saniye bekleterek veriyordu. Timeout'suz retry bunu 30sn'lik bir hang'e şişiriyordu.

**Çözüm:** Polly ile **her denemeye ayrı timeout** (6sn) + 1 retry, retry'ın içine sarılı. Artık:
- Geçici tekleme → retry ile kurtulur
- Kalıcı yoğunluk → max ~13sn'de nazik "yoğunluk var" mesajı (hang yok)

**Mülakatta:** "Dış servis yavaşlarsa ne olur?" → "Per-attempt timeout olmadan retry, tek yavaş yanıtı toplam süreye çarpar. Timeout'u retry'ın içine koyup her denemeyi sınırladım; kullanıcı asla 30sn beklemez."

---

## 4. BYOK + Çoklu-Anahtar Rotasyonu

**Problem:** Ücretsiz demo tek Gemini anahtarını paylaşıyor; yoğunlukta 429/503 yiyor.

**Çözüm iki katmanlı:**
1. **BYOK (Bring Your Own Key):** Kullanıcı `X-User-Api-Key` header'ıyla kendi anahtarını verir → kendi kotasından gider, demo limitine takılmaz. Anahtar yalnızca istek scope'unda; body/log/cache'e girmez.
2. **Çoklu-anahtar havuzu:** `GEMINI_API_KEYS` (N anahtar) → round-robin dağıtım + 429/503 alan anahtarı cooldown'a alma (failover). Bir anahtar dolsa diğerine geçer.

**Teknik incelik:** Anahtar seçimi bir `DelegatingHandler`'da her istekte yapılır; retry'ın **içine** kayıtlı ki tek istek içinde bile dolu anahtardan sağlam anahtara failover olsun.

**Mülakatta:** "Rate limit'i nasıl yönettin?" → "İki yol: kullanıcı kendi anahtarını verebilir (BYOK), ya da ücretsiz havuzda N anahtar arasında round-robin + cooldown failover. Anahtar seçimi HTTP handler'ında, retry'ın içinde — bir istek içinde bile provider'a yük dağıtır."

---

## 5. Multi-turn Konuşma Hafızası + Query Rewriting

**Problem:** Her soru bağımsızdı. "Peki ya 5 yılını dolduranlar?" gibi bir takip sorusu tek başına anlamsız — retrieval yanlış chunk getirir.

**Çözüm:**
- **Konuşma geçmişi:** Session bazlı, TTL'li tur depolama (`conversation_turns`).
- **Query rewriting:** Takip sorusunu geçmişle **bağımsız tam soruya** çevirir (LLM ile), sonra o soruyu embed eder. Geçmiş boşsa (ilk soru) LLM çağrısı yapılmaz (maliyet).
- Son 5 tur prompt'a User/Assistant olarak eklenir.

**Canlı kanıt:** "peki ya 5 yılını dolduranlar?" → arka planda "5 yılını dolduran çalışanlar için yıllık izin kaç gündür?" → doğru cevap (26 iş günü).

**Teknik incelik:** Multi-turn açıkken cache devre dışı (takip cevabı bağlama bağlı); refusal/boş cevaplar geçmişe yazılmaz (sonraki rewrite'ı zehirlememek için).

**Mülakatta:** "Takip sorularını nasıl ele alırsın?" → "Query rewriting / contextualization: konuşma geçmişini kullanıp soruyu bağımsız hale getiriyorum, sonra embed ediyorum. Klasik conversational-RAG tekniği."

---

## 6. Çok-Sağlayıcılı BYOK (OpenAI / Gemini / Grok)

**Problem:** BYOK sadece Gemini içindi. Kullanıcı "ben GPT-4/Grok kullanmak istiyorum" diyemiyordu.

**Çözüm:** Kullanıcı `X-User-Ai-Provider` (openai/gemini/grok) + `X-User-Api-Key` ile sağlayıcı seçer. **Cevap** onun modelinden üretilir; **embedding/retrieval HER ZAMAN bizim Gemini havuzumuzda** kalır.

**Neden embedding sabit:** Sağlayıcıların embedding'leri farklı boyut/uzayda (Gemini 768, OpenAI 1536) ve **birbiriyle uyumsuz**. DB'deki vektörler Gemini ile indexlendiği için retrieval hep Gemini embedding'iyle yapılmalı — yoksa benzerlik matematiksel olarak anlamsız olur. Gerçek ürünlerin (ChatGPT vb.) yaptığı gibi: embedding sabit, LLM seçilebilir.

**Teknik incelik:** OpenAI ve Grok aynı Chat Completions API'sini paylaşır (tek adapter, sadece BaseUrl + model farklı, config'ten). Kritik bir hata yakalandı ve düzeltildi: OpenAI anahtarının yanlışlıkla Gemini embedding'ine gitmesi.

**Mülakatta:** "Provider-agnostic derken?" → "Tek `ILlmProvider` portu arkasında 3 sağlayıcı; kullanıcı runtime'da seçiyor. Embedding uyumsuzluğunu bilerek izole ettim — retrieval sabit kalır, sadece cevap üreten model değişir."

---

## 7. Swagger/OpenAPI + CI/CD + Integration Test

**Problem:** API dokümantasyonu yoktu, testler elle çalışıyordu, hepsi birim seviyesindeydi (fake'lerle).

**Çözüm üç parça:**
- **Swagger/OpenAPI:** `Microsoft.AspNetCore.OpenApi` + Scalar UI (`/docs`). Endpoint'ler görünür ve denenebilir.
- **GitHub Actions CI:** Her push/PR'da restore → build → test. Yeşil rozet.
- **Integration testler:** `WebApplicationFactory<Program>` + **Testcontainers** (gerçek pgvector container). LLM/embedding fake'lenir (dış çağrı yok, deterministik), ama Postgres/retrieval/session/cache **gerçekten** test edilir. 5 test: health, OpenAPI şeması, seed'den retrieval, boş-soru 400, session cookie.

**Teknik incelik:** Testcontainers her koşuda izole bir Postgres başlatır → CI'da da lokalde de aynı, "bende çalışıyordu" sorunu yok.

**Mülakatta:** "Testlerin nasıl?" → "Unit + integration. Integration testler WebApplicationFactory ile API'yi ayağa kaldırıp Testcontainers'ta gerçek pgvector'e karşı uçtan uca çalışıyor — retrieval'ın gerçekten doğru chunk'ı getirdiğini gerçek DB'de doğruluyorum. Hepsi CI'da otomatik."

---

## 8. Redis Cache + Cohere Cross-Encoder Reranker

**Problem:** (a) Cache tek-node Postgres'te — dağıtık/düşük-gecikme senaryosu yok. (b) Reranker'lar bi-encoder (cosine) skoruna dayanıyor; gerçek bir cross-encoder daha isabetli sıralar.

**Çözüm — iki config-seçilebilir adapter (mevcut port'ların arkasına):**

- **Redis cache** (`Cache:Provider = Postgres | Redis`): `IResponseCache`'in Redis implementasyonu. TTL Redis expiry ile (ayrı temizlik yok). Paylaşımlı Redis'te `RedisKeyPrefix` ile izolasyon. Demo'da Postgres varsayılan kalır.
- **Cohere cross-encoder** (`Retrieval:RerankerType = Llm | Hybrid | Cohere`): `IReranker`'ın Cohere Rerank API adapter'ı. Bi-encoder'da soru ve doküman ayrı embed edilir; **cross-encoder ikisini birlikte** değerlendirip alaka skoru üretir → daha isabetli. Bedeli: soru başına 1 dış çağrı (kotalı). Demo'da Hybrid (kotasız) varsayılan.

**Dayanıklılık (kod incelemesinden gelen kritik ders):** İkisi de **opsiyonel iyileştirme**, zorunluluk değil — bu yüzden ikisinde de graceful degradation:
- Redis erişilemezse → `AbortOnConnectFail=false` + try/catch → cache **miss** olarak devam (istek çökmez).
- Cohere API hata/kota dönerse → **retrieval sırasıyla** ilk top-n (rerank atlanır, istek çökmez).
- Cohere `top_n`, aday sayısını aşamaz → `Math.Min` ile clamp (küçük setlerde rerank sessizce atlanmasın).

**Mülakatta:** "Cache/reranking'i nasıl ölçeklerdin?" → "Cache'i port arkasında tuttuğum için Postgres'ten Redis'e geçiş tek config; TTL'i Redis expiry'sine devrettim. Reranking'de gerçek cross-encoder (Cohere) ekledim — bi-encoder cosine'dan farklı olarak query+doc'u birlikte skorlar. İkisini de opsiyonel iyileştirme olarak tasarladım: backend çökse bile ana akış (cache-miss / retrieval-sırası) çalışmaya devam eder."

---

## 9. Parent-Document Retrieval ("small-to-big")

**Problem — chunk boyutu ikilemi:** Küçük chunk = isabetli retrieval (dar, net eşleşme) ama LLM'e eksik bağlam. Büyük chunk = zengin bağlam ama retrieval gürültülü (embedding çok konuyu ortalar). İkisini aynı anda isteyemezsin.

**Çözüm:** Küçük **child** ile ARA, LLM'e child'ın ait olduğu **büyük parent** bloğu ver.
- Ingestion: metin → büyük parent blokları → her parent küçük child'lara bölünür. Her child kaydı, parent'ının tam metnini taşır (`parent_content`). Embedding **child** üzerinden.
- Retrieval: child ile ara/rerank → context'e child yerine **parent** koy. Aynı parent'a düşen birden çok child → parent bir kez (tekrar/token israfı yok).

**Teknik incelikler (kod incelemesinden):**
- Dedup anahtarı `DocumentId + parent` — farklı dokümandaki aynı boilerplate metin meşru bir kaynağı düşürmesin.
- `ParentMaxTokens <= MaxTokens` yanlış config → parent≈child, özellik anlamsızlaşır → ingestion'da uyarı.
- Config-seçilebilir (`Chunking:Strategy = FixedSize | ParentDocument`); strateji değişince re-ingest gerekir (belgelendi).

**Mülakatta:** "Chunk boyutunu nasıl seçersin?" → "İkilem var: küçük chunk iyi retrieval, büyük chunk iyi bağlam. Parent-document retrieval ile ikisini ayırdım — küçük child ile arıyorum (isabet), LLM'e büyük parent'ı veriyorum (tam cevap). Aynı parent'a düşen child'ları context'te tekilliyorum ki token israf olmasın."

---

## Kesişen mühendislik temaları (mülakatta vurgula)

- **Clean Architecture / Ports & Adapters:** Her dış detay (LLM, embedding, vektör store, cache, reranker) bir interface arkasında. Sağlayıcı değişince iş mantığı hiç değişmiyor.
- **Provider-agnostik:** OpenAI ↔ Gemini ↔ Grok tek config/tek header ile.
- **Production armor:** Session izolasyonu, IP rate limiting (DB tabanlı, restart-dayanıklı), token bütçesi, TTL temizliği, upload sertleştirme, prompt injection guard.
- **Güvenli deploy:** 15+ canlı sitenin olduğu paylaşımlı bir sunucuya, diğerlerini hiç etkilemeden (ayrı Docker ağı + nginx server bloğu).
- **Kalite disiplini:** Her özellikten sonra kod incelemesi; bulunan kritik hatalar (BYOK embedding sızıntısı vb.) düzeltilip regresyon testi eklendi.

---

*Son güncelleme: 2026-09-13 · Yol haritasındaki tüm planlı entegrasyonlar tamamlandı. Sıradakiler açık.*
