using KurumsalRAG.Application.Abstractions;

namespace KurumsalRAG.Application.Evaluation;

/// <summary>
/// Seed dokümanına (Aurora Bilişim politikası) dayalı altın soru seti. Gerçekler chunk id'ye
/// değil METİN parçalarına bağlı (chunk id'ler her ingest'te değişir). context-içi + context-dışı
/// (red beklenen) sorular karışık — hem doğruluk hem hallucination direncini ölçer.
/// </summary>
public static class GoldenSet
{
    public static IReadOnlyList<GoldenQuestion> Questions { get; } =
    [
        // --- Context içi (doğru cevap beklenir) ---
        new("Çalışanlar haftada kaç gün uzaktan çalışabilir?", ["2 (iki) gün", "en fazla 2", "iki gün"], false),
        new("Yıllık izin kaç gün?", ["20 iş günü", "20 iş", "26 iş"], false),
        new("Ev ofisi ekipman desteği ne kadar?", ["12.000"], false),
        new("Uzaktan çalışılan aylarda internet gideri katkısı ne kadar?", ["600"], false),
        new("Yıllık eğitim ve gelişim bütçesi ne kadar?", ["15.000"], false),
        new("Uzaktan çalışırken VPN kullanımı zorunlu mu?", ["VPN"], false),
        new("Çekirdek çalışma saatleri nedir?", ["10:00", "16:00"], false),
        new("Deneme süresi kaç gündür?", ["90 gün", "90"], false),
        new("Çekirdek ofis günleri hangileridir?", ["Pazartesi", "Perşembe"], false),

        // --- Context dışı (sistem "dokümanlarda yok" demeli) ---
        new("Şirket araç tahsisi var mı?", [], true),
        new("Maaş zammı oranı tam olarak yüzde kaçtır?", [], true),
        new("Yurt dışından çalışma politikası nedir?", [], true),
    ];
}
