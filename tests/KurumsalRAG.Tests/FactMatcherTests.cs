using KurumsalRAG.Application.Evaluation;

namespace KurumsalRAG.Tests;

public sealed class FactMatcherTests
{
    [Fact]
    public void Turkce_diakritik_tolere_edilir()
        // "yıllık" (Türkçe ı) beklenen; cevap ASCII "yillik" → eşleşmeli.
        => Assert.True(FactMatcher.ContainsAny("Yillik izin 20 is gunudur", ["yıllık"]));

    [Fact]
    public void Herhangi_biri_eslesirse_true()
        => Assert.True(FactMatcher.ContainsAny("izin 26 iş günüdür", ["20 iş", "26 iş"]));

    [Fact]
    public void Hicbiri_eslesmezse_false()
        => Assert.False(FactMatcher.ContainsAny("ekipman bütçesi 12.000 TL", ["20 iş", "26 iş"]));

    [Fact]
    public void Bos_beklenti_false()
        => Assert.False(FactMatcher.ContainsAny("herhangi bir metin", []));

    [Fact]
    public void Buyuk_kucuk_harf_tolere_edilir()
        => Assert.True(FactMatcher.ContainsAny("KURUMSAL VPN ZORUNLUDUR", ["vpn"]));

    // --- IsRefusal: LLM refusal'ı kelimesi kelimesine üretmeyebilir ---

    [Fact]
    public void Refusal_tam_metin_yakalanir()
        => Assert.True(FactMatcher.IsRefusal("Sağlanan dokümanlarda bu bilgi bulunmuyor."));

    [Fact]
    public void Refusal_chunk_ekiyle_yakalanir()
        => Assert.True(FactMatcher.IsRefusal("Sağlanan dokümanlarda bu bilgi bulunmuyor [chunk:1]."));

    [Fact]
    public void Refusal_cumle_ici_yakalanir()
        => Assert.True(FactMatcher.IsRefusal("Dokümanlarda maaş zammı oranının yüzde kaç olduğu bulunmuyor."));

    [Fact]
    public void Gercek_cevap_refusal_sayilmaz()
        => Assert.False(FactMatcher.IsRefusal("Yıllık izin 20 iş günüdür [chunk:1]."));
}
