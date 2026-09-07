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
}
