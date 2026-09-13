using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

// Seed dokümanını (Aurora Bilişim İK Politikası) programatik üretir → SeedData/seed-policy.pdf.
// Metni değiştir + `dotnet run` → PDF yeniden üretilir. Uzun/çok-konulu belge, chunking
// stratejileri arasındaki gerçek farkı (eval'de) ortaya çıkarmak için tasarlandı.

QuestPDF.Settings.License = LicenseType.Community;

// Her biri (başlık, paragraflar) bir bölüm. Golden set bu içerikteki gerçeklere dayanır.
var sections = new (string Title, string[] Paragraphs)[]
{
    ("1. Amaç ve Kapsam", new[]
    {
        "Bu politika, Aurora Bilişim A.Ş. bünyesinde çalışan tüm personelin çalışma düzenini, haklarını ve yükümlülüklerini tanımlar. Politika; tam zamanlı, yarı zamanlı ve stajyer statüsündeki tüm çalışanları kapsar.",
        "Politikada yer almayan konularda yürürlükteki İş Kanunu ve şirket yönetmelikleri esas alınır. Politika yılda en az bir kez İnsan Kaynakları Direktörlüğü tarafından gözden geçirilir.",
    }),
    ("2. Çalışma Düzeni ve Uzaktan Çalışma", new[]
    {
        "Aurora Bilişim hibrit çalışma modeli uygular. Çalışanlar haftada en fazla 2 (iki) gün uzaktan çalışabilir; kalan günlerde ofiste bulunmaları beklenir.",
        "Pazartesi ve Perşembe günleri tüm ekiplerin ofiste bulunması zorunlu çekirdek günlerdir ve bu iki gün uzaktan çalışma talebine kapalıdır.",
        "Uzaktan çalışma talebi, ilgili haftadan en az 3 iş günü önce yöneticiye iletilmeli ve onaylanmalıdır. Onaylanmayan talepler ofiste çalışma olarak değerlendirilir.",
        "Çekirdek çalışma saatleri 10:00 ile 16:00 arasıdır; tüm çalışanların bu saatlerde erişilebilir olması beklenir. Esnek başlangıç/bitiş, günlük 8 saatlik toplam çalışma korunmak kaydıyla mümkündür.",
    }),
    ("3. İzin Hakları", new[]
    {
        "Yıllık ücretli izin, 1 yılını dolduran çalışanlar için 20 iş günüdür. 5 yıl ve üzeri kıdeme sahip çalışanlar için bu süre 26 iş gününe yükselir.",
        "Mazeret izni yılda 5 iş günüdür ve önceden onay gerektirir. Evlilik izni 3 iş günü, vefat izni (birinci derece yakın) 3 iş günü olarak uygulanır.",
        "Doğum izni ve babalık izni yürürlükteki kanun hükümlerine göre verilir; babalık izni 5 iş günüdür. Kullanılmayan yıllık izinler bir sonraki yıla en fazla 5 gün devredilebilir.",
    }),
    ("4. Ekipman ve Gider Destekleri", new[]
    {
        "Şirket, her çalışana ev ofisi kurulumu için bir defaya mahsus 12.000 TL ekipman desteği sağlar. Bu destek ergonomik sandalye, monitör ve klavye gibi kalemleri kapsar.",
        "Uzaktan çalışılan aylarda internet gideri katkısı olarak aylık 600 TL ödenir. Katkı, o ay içinde en az bir gün uzaktan çalışan personele uygulanır.",
        "Şirket tarafından tahsis edilen dizüstü bilgisayar ve telefon zimmetlidir; işten ayrılışta 5 iş günü içinde iade edilmelidir. İade edilmeyen ekipmanın bedeli son maaştan mahsup edilebilir.",
    }),
    ("5. Eğitim ve Gelişim", new[]
    {
        "Her çalışan için yıllık eğitim ve gelişim bütçesi 15.000 TL'dir. Bütçe; online kurslar, konferans katılımı ve sertifika sınavları için kullanılabilir.",
        "Eğitim talepleri yöneticinin onayına tabidir ve çalışanın gelişim planıyla uyumlu olmalıdır. Kullanılmayan eğitim bütçesi bir sonraki yıla devredilmez.",
    }),
    ("6. Bilgi Güvenliği", new[]
    {
        "Uzaktan çalışırken şirket kaynaklarına erişim yalnızca kurumsal VPN üzerinden yapılır; VPN kullanımı zorunludur. Kişisel cihazlardan şirket verisine erişim yasaktır.",
        "Parolalar en az 12 karakter olmalı ve 90 günde bir değiştirilmelidir. Çok faktörlü kimlik doğrulama (MFA) tüm kritik sistemlerde zorunludur.",
        "Şirket verisi kişisel bulut hesaplarında (kişisel e-posta, kişisel depolama) saklanamaz. İhlaller disiplin sürecine tabidir.",
    }),
    ("7. İşe Alım ve Deneme Süresi", new[]
    {
        "Yeni işe alınan çalışanlar için deneme süresi 90 gündür. Deneme süresi boyunca performans ve uyum değerlendirilir.",
        "Deneme süresi sonunda olumlu değerlendirilen çalışanlar kadroya alınır. İşe alım süreçleri İnsan Kaynakları ve ilgili birim yöneticisi tarafından ortak yürütülür.",
    }),
    ("8. Performans Değerlendirme", new[]
    {
        "Performans değerlendirmesi yılda 2 kez, Haziran ve Aralık aylarında yapılır. Değerlendirme hedeflere ulaşma, ekip çalışması ve gelişim kriterlerini içerir.",
        "Yıl sonu değerlendirmesi terfi ve ücret artışı kararlarına esas oluşturur. Değerlendirme sonuçları çalışanla birebir görüşmede paylaşılır.",
    }),
    ("9. Sağlık ve Sigorta", new[]
    {
        "Tüm tam zamanlı çalışanlar özel sağlık sigortası kapsamındadır; sigorta işe başlangıçtan itibaren 30 gün içinde aktive edilir. Eş ve çocuklar indirimli prim ile pakete eklenebilir.",
        "Yıllık check-up hakkı tüm çalışanlara sağlanır. İş kazası ve meslek hastalığı süreçleri yürürlükteki mevzuata göre yürütülür.",
    }),
    ("10. Seyahat ve Harcama", new[]
    {
        "İş seyahatlerinde konaklama ve ulaşım şirket tarafından karşılanır. Şehir içi ulaşım için günlük harcırah 500 TL'dir.",
        "Harcama talepleri fatura/fiş ile 15 gün içinde sisteme girilmelidir. Onaysız veya belgesiz harcamalar geri ödenmez.",
    }),
    ("11. Disiplin ve Etik", new[]
    {
        "Çalışanlar şirket etik kurallarına ve gizlilik yükümlülüklerine uymakla yükümlüdür. Etik ihlaller yazılı uyarı, kınama ve iş akdinin feshi ile sonuçlanabilir.",
        "Çıkar çatışması durumları İnsan Kaynaklarına bildirilmelidir. Rüşvet, veri sızıntısı ve taciz sıfır tolerans kapsamındadır.",
    }),
};

var doc = Document.Create(container =>
{
    container.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(2, Unit.Centimetre);
        page.DefaultTextStyle(t => t.FontSize(11).LineHeight(1.4f));

        page.Header().PaddingBottom(10).Column(col =>
        {
            col.Item().Text("Aurora Bilişim A.Ş.").FontSize(18).Bold();
            col.Item().Text("İnsan Kaynakları ve Çalışma Politikası").FontSize(13).FontColor(Colors.Grey.Darken1);
        });

        page.Content().Column(col =>
        {
            col.Spacing(10);
            foreach (var (title, paragraphs) in sections)
            {
                col.Item().PaddingTop(6).Text(title).FontSize(13).Bold().FontColor(Colors.Blue.Darken2);
                foreach (var p in paragraphs)
                    col.Item().Text(p).Justify();
            }
        });

        page.Footer().AlignCenter().Text(t =>
        {
            t.Span("Aurora Bilişim — İK Politikası · ");
            t.CurrentPageNumber();
            t.Span(" / ");
            t.TotalPages();
        });
    });
});

// Çıktı: SeedData/seed-policy.pdf (repo köküne göre). args[0] verilirse o yola yazar.
var output = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "KurumsalRAG.Api", "SeedData", "seed-policy.pdf");
output = Path.GetFullPath(output);
doc.GeneratePdf(output);
Console.WriteLine($"Seed PDF üretildi: {output}");
Console.WriteLine($"Bölüm sayısı: {sections.Length}");
