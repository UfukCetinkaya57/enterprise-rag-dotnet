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
        // Uzun belge: farklı bölümlere yayılmış, ayırt edici sorular (retrieval'ı zorlar)
        new("Performans değerlendirmesi hangi aylarda yapılır?", ["Haziran", "Aralık"], false),
        new("İş seyahatlerinde şehir içi ulaşım için günlük harcırah ne kadar?", ["500"], false),
        new("Parolalar kaç günde bir değiştirilmelidir?", ["90 gün", "90"], false),
        new("Babalık izni kaç iş günüdür?", ["5 iş günü", "5 gün", "5"], false),
        new("Özel sağlık sigortası ne zaman aktive edilir?", ["30 gün", "30"], false),
        new("Zimmetli ekipman işten ayrılışta kaç iş günü içinde iade edilmelidir?", ["5 iş günü", "5 gün", "5"], false),
        new("Kullanılmayan yıllık izin bir sonraki yıla en fazla kaç gün devredilebilir?", ["5 gün", "5"], false),

        // --- Context dışı (sistem "dokümanlarda yok" demeli) ---
        new("Şirket araç tahsisi var mı?", [], true),
        new("Maaş zammı oranı tam olarak yüzde kaçtır?", [], true),
        new("Yurt dışından çalışma politikası nedir?", [], true),
        new("Şirketin hisse senedi opsiyon programı nasıl işliyor?", [], true),
    ];
}
