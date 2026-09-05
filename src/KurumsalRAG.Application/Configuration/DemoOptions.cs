namespace KurumsalRAG.Application.Configuration;

/// <summary>
/// Public canlı demo zırhı konfigürasyonu (appsettings "Demo" bölümü).
/// Hepsi config'ten — hard-code yok. Kill switch, bütçe, kotalar, TTL, cache, upload.
/// </summary>
public sealed class DemoOptions
{
    public const string SectionName = "Demo";

    /// <summary>Kill switch: false ise tüm chat uçları nazik "limited" cevabı döner.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Örnek (seed) dokümanların session kimliği. Retrieval her session'a ek olarak bunu görür.</summary>
    public string SeedSessionId { get; init; } = "seed";

    /// <summary>
    /// /api/diagnostics/* uçları etkin mi. Production'da FALSE olmalı: bu uçlar LLM çağırır
    /// ve token bütçesi/kotalar dışındadır — public'te token yakma vektörüdür.
    /// </summary>
    public bool DiagnosticsEnabled { get; init; } = true;

    /// <summary>Startup'ta bir kez ingest edilecek örnek doküman etkin mi.</summary>
    public bool SeedEnabled { get; init; } = true;

    /// <summary>Seed PDF yolu (uygulama çıktı dizinine göre relatif).</summary>
    public string SeedFilePath { get; init; } = "SeedData/seed-policy.pdf";

    /// <summary>Günlük global token bütçesi (input+output+embedding). Aşımda LLM çağrısı yapılmaz.</summary>
    public long DailyTokenBudget { get; init; } = 200_000;

    /// <summary>Seed hariç bu saatten eski dokümanlar/chunk'lar silinir.</summary>
    public int DocumentTtlHours { get; init; } = 24;

    public IpQuotaOptions Ip { get; init; } = new();
    public SessionQuotaOptions Session { get; init; } = new();
    public CacheOptions Cache { get; init; } = new();
    public UploadOptions Upload { get; init; } = new();
}

public sealed class IpQuotaOptions
{
    /// <summary>IP başına günlük sorgu limiti.</summary>
    public int QueriesPerDay { get; init; } = 30;

    /// <summary>IP başına günlük upload limiti.</summary>
    public int UploadsPerDay { get; init; } = 3;
}

public sealed class SessionQuotaOptions
{
    /// <summary>Session başına en fazla doküman sayısı.</summary>
    public int MaxDocuments { get; init; } = 2;

    /// <summary>Session başına toplam upload boyutu (byte).</summary>
    public long MaxTotalBytes { get; init; } = 20 * 1024 * 1024; // 20 MB
}

public sealed class CacheOptions
{
    /// <summary>Yanıt cache'i TTL (saat).</summary>
    public int TtlHours { get; init; } = 24;
}

public sealed class UploadOptions
{
    /// <summary>Tek dosya için maksimum boyut (byte).</summary>
    public long MaxBytes { get; init; } = 10 * 1024 * 1024; // 10 MB

    /// <summary>Maksimum sayfa sayısı.</summary>
    public int MaxPages { get; init; } = 50;
}
