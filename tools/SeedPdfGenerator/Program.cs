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
        "Bu politika, Aurora Bilişim A.Ş. bünyesinde çalışan tüm personelin çalışma düzenini, haklarını ve yükümlülüklerini tanımlar. Politika; tam zamanlı, yarı zamanlı ve stajyer statüsündeki tüm çalışanları kapsar. Alt yükleniciler ve serbest çalışan danışmanlar için ayrı bir sözleşme eki uygulanır.",
        "Politikada yer almayan konularda yürürlükteki İş Kanunu ve şirket yönetmelikleri esas alınır. Politika yılda en az bir kez İnsan Kaynakları Direktörlüğü tarafından gözden geçirilir ve güncellenen sürüm intranet üzerinden tüm çalışanlara duyurulur.",
        "Politikanın herhangi bir maddesiyle bireysel iş sözleşmesi arasında çelişki olması hâlinde, çalışan lehine olan hüküm uygulanır. Politika değişiklikleri geriye dönük olarak çalışan aleyhine uygulanamaz.",
        "Bu belgede geçen 'yönetici' ifadesi çalışanın doğrudan bağlı olduğu birim amirini, 'İK' ifadesi İnsan Kaynakları Direktörlüğü'nü işaret eder. Tüm başvuru ve talepler aksi belirtilmedikçe İK portalı üzerinden yapılır.",
    }),
    ("2. Çalışma Düzeni ve Uzaktan Çalışma", new[]
    {
        "Aurora Bilişim hibrit çalışma modeli uygular. Çalışanlar haftada en fazla 2 (iki) gün uzaktan çalışabilir; kalan günlerde ofiste bulunmaları beklenir. Bu sınır, ekip yöneticisinin yazılı onayı olmadan aşılamaz.",
        "Pazartesi ve Perşembe günleri tüm ekiplerin ofiste bulunması zorunlu çekirdek günlerdir ve bu iki gün uzaktan çalışma talebine kapalıdır. Çekirdek günlerde alınan yıllık izinler bu kuralın istisnasıdır.",
        "Uzaktan çalışma talebi, ilgili haftadan en az 3 iş günü önce yöneticiye iletilmeli ve onaylanmalıdır. Onaylanmayan talepler ofiste çalışma olarak değerlendirilir. Acil durumlar (sağlık, aile) için aynı gün talep istisnaen değerlendirilebilir.",
        "Çekirdek çalışma saatleri 10:00 ile 16:00 arasıdır; tüm çalışanların bu saatlerde erişilebilir ve toplantılara hazır olması beklenir. Esnek başlangıç ve bitiş, günlük 8 saatlik toplam çalışma korunmak kaydıyla mümkündür.",
        "Uzaktan çalışılan günlerde çalışanın sessiz ve kesintisiz bir çalışma ortamı sağlaması beklenir. Toplantılarda kamera kullanımı ekip içi kararlara bırakılmıştır ancak müşteri toplantılarında kamera açık tutulur.",
        "Tam uzaktan (kalıcı remote) çalışma yalnızca istisnai pozisyonlar için, İK ve üst yönetim onayıyla tanımlanır. Bu statüdeki çalışanlar da yılda en az 4 kez ofis etkinliklerine katılmakla yükümlüdür.",
    }),
    ("3. İzin Hakları", new[]
    {
        "Yıllık ücretli izin, 1 yılını dolduran çalışanlar için 20 iş günüdür. 5 yıl ve üzeri kıdeme sahip çalışanlar için bu süre 26 iş gününe yükselir. İzin hakkı, işe giriş tarihine göre yıllık olarak tahakkuk eder.",
        "Yıllık izin talepleri en az 7 gün önceden İK portalına girilmeli ve yönetici tarafından onaylanmalıdır. Yoğun proje dönemlerinde yönetici, ekip devamlılığı için izin tarihinde değişiklik önerebilir.",
        "Mazeret izni yılda 5 iş günüdür ve önceden onay gerektirir. Evlilik izni 3 iş günü, vefat izni (birinci derece yakın) 3 iş günü olarak uygulanır. Mazeret izni yıllık izinden düşülmez.",
        "Doğum izni ve babalık izni yürürlükteki kanun hükümlerine göre verilir; babalık izni 5 iş günüdür. Süt izni, ilgili mevzuattaki süreler boyunca günlük olarak kullanılır.",
        "Kullanılmayan yıllık izinler bir sonraki yıla en fazla 5 gün devredilebilir; devredilen izinler o yılın ilk 6 ayında kullanılmalıdır. Bunun dışındaki bakiye izinler yanar.",
        "Ücretsiz izin talepleri istisnaidir; en fazla 3 aya kadar, İK ve yönetici onayıyla verilebilir. Ücretsiz izin süresince özel sağlık sigortası dondurulur.",
    }),
    ("4. Ekipman ve Gider Destekleri", new[]
    {
        "Şirket, her çalışana ev ofisi kurulumu için bir defaya mahsus 12.000 TL ekipman desteği sağlar. Bu destek ergonomik sandalye, monitör, klavye ve masa gibi kalemleri kapsar; harcamalar fatura ile belgelenmelidir.",
        "Ekipman desteği işe başlangıçtan itibaren ilk 60 gün içinde talep edilmelidir. Destek nakit ödenmez; onaylı satın alımlar üzerinden veya anlaşmalı tedarikçiden temin edilir.",
        "Uzaktan çalışılan aylarda internet gideri katkısı olarak aylık 600 TL ödenir. Katkı, o ay içinde en az bir gün uzaktan çalışan personele uygulanır ve bordroya yansıtılır.",
        "Şirket tarafından tahsis edilen dizüstü bilgisayar, telefon ve aksesuarlar zimmetlidir; işten ayrılışta 5 iş günü içinde iade edilmelidir. İade edilmeyen ekipmanın güncel bedeli son maaştan mahsup edilebilir.",
        "Zimmetli ekipmanın arızası durumunda BT departmanına başvurulur; kişisel kullanımdan doğan hasarlar çalışan sorumluluğundadır. Ekipman üçüncü kişilere devredilemez veya kişisel işler için kullanılamaz.",
    }),
    ("5. Eğitim ve Gelişim", new[]
    {
        "Her çalışan için yıllık eğitim ve gelişim bütçesi 15.000 TL'dir. Bütçe; online kurslar, konferans katılımı, kitap ve sertifika sınavları için kullanılabilir.",
        "Eğitim talepleri yöneticinin onayına tabidir ve çalışanın yıllık gelişim planıyla uyumlu olmalıdır. Kullanılmayan eğitim bütçesi bir sonraki yıla devredilmez.",
        "Şirket tarafından finanse edilen ve 25.000 TL'yi aşan sertifika/eğitim programlarında, çalışanın eğitimden sonra en az 12 ay şirkette kalması beklenir; erken ayrılışta orantılı geri ödeme uygulanabilir.",
        "İç mentorluk programı gönüllülük esasına dayanır. Mentor olan çalışanlara yıllık performans değerlendirmesinde ek katkı puanı verilir.",
    }),
    ("6. Bilgi Güvenliği", new[]
    {
        "Uzaktan çalışırken şirket kaynaklarına erişim yalnızca kurumsal VPN üzerinden yapılır; VPN kullanımı zorunludur. Kişisel cihazlardan şirket verisine erişim yasaktır.",
        "Parolalar en az 12 karakter olmalı ve 90 günde bir değiştirilmelidir. Son 5 parola tekrar kullanılamaz. Çok faktörlü kimlik doğrulama (MFA) tüm kritik sistemlerde zorunludur.",
        "Şirket verisi kişisel bulut hesaplarında (kişisel e-posta, kişisel depolama) saklanamaz. Gizli sınıflandırmalı belgeler yalnızca şirket onaylı platformlarda paylaşılır.",
        "Halka açık ağlarda (kafe, havaalanı) şirket sistemlerine yalnızca VPN ile bağlanılır. Cihaz kaybı veya çalınması durumunda 1 saat içinde BT güvenlik ekibine bildirim zorunludur.",
        "Kimlik avı (phishing) şüphesi taşıyan e-postalar tıklanmadan güvenlik ekibine iletilmelidir. Yıllık zorunlu bilgi güvenliği eğitimi tüm çalışanlar için tamamlanmalıdır. İhlaller disiplin sürecine tabidir.",
    }),
    ("7. İşe Alım ve Deneme Süresi", new[]
    {
        "Yeni işe alınan çalışanlar için deneme süresi 90 gündür. Deneme süresi boyunca performans, uyum ve ekip içi işbirliği değerlendirilir.",
        "Deneme süresi sonunda olumlu değerlendirilen çalışanlar kadroya alınır. İşe alım süreçleri İnsan Kaynakları ve ilgili birim yöneticisi tarafından ortak yürütülür.",
        "İç başvurular dış adaylara göre önceliklidir; açık pozisyonlar önce 5 iş günü boyunca iç ilan olarak duyurulur. Çalışan referansıyla işe alımlarda referans primi uygulanır.",
        "Oryantasyon programı işe başlangıçtan itibaren ilk 2 hafta içinde tamamlanır ve şirket kültürü, süreçler ve güvenlik konularını kapsar.",
    }),
    ("8. Performans Değerlendirme", new[]
    {
        "Performans değerlendirmesi yılda 2 kez, Haziran ve Aralık aylarında yapılır. Değerlendirme hedeflere ulaşma, ekip çalışması, teknik yetkinlik ve gelişim kriterlerini içerir.",
        "Değerlendirme 1 ile 5 arasında bir ölçekte puanlanır; 3 ve üzeri 'beklentiyi karşılıyor' kabul edilir. Yıl sonu değerlendirmesi terfi ve ücret artışı kararlarına esas oluşturur.",
        "Değerlendirme sonuçları çalışanla birebir görüşmede paylaşılır. Beklentinin altında kalan çalışanlar için 90 günlük bir gelişim planı tanımlanır.",
        "360 derece geri bildirim, kıdemli ve yönetici pozisyonları için yılda bir kez uygulanır. Geri bildirimler anonimdir ve gelişim amaçlıdır.",
    }),
    ("9. Sağlık ve Sigorta", new[]
    {
        "Tüm tam zamanlı çalışanlar özel sağlık sigortası kapsamındadır; sigorta işe başlangıçtan itibaren 30 gün içinde aktive edilir. Eş ve çocuklar indirimli prim ile pakete eklenebilir.",
        "Yıllık check-up hakkı tüm çalışanlara sağlanır ve anlaşmalı sağlık kuruluşlarında kullanılır. Diş ve göz paketleri isteğe bağlı ek prim ile eklenebilir.",
        "İş kazası ve meslek hastalığı süreçleri yürürlükteki mevzuata göre yürütülür; iş kazaları 24 saat içinde İK'ya ve ilgili kuruma bildirilir.",
        "Kurumsal psikolojik destek hattı tüm çalışanlara ücretsiz ve gizli olarak sunulur. Yıllık grip aşısı kampanyası sonbahar döneminde ofiste düzenlenir.",
    }),
    ("10. Seyahat ve Harcama", new[]
    {
        "İş seyahatlerinde konaklama ve ulaşım şirket tarafından karşılanır. Şehir içi ulaşım için günlük harcırah 500 TL'dir. Uçak seyahatlerinde ekonomi sınıfı esastır; 6 saatin üzerindeki uçuşlarda üst sınıf yöneticinin onayına tabidir.",
        "Harcama talepleri fatura veya fiş ile 15 gün içinde sisteme girilmelidir. Onaysız veya belgesiz harcamalar geri ödenmez.",
        "Konaklama, seyahat edilen şehirdeki anlaşmalı otellerde yapılır; gecelik üst limit büyükşehirler için farklı belirlenir. Minibar ve kişisel harcamalar kapsam dışıdır.",
        "Yurt dışı seyahatlerde vize, seyahat sigortası ve aşı masrafları şirketçe karşılanır. Seyahat avansı talep edilebilir ve dönüş sonrası 15 gün içinde mahsuplaştırılır.",
    }),
    ("11. Disiplin ve Etik", new[]
    {
        "Çalışanlar şirket etik kurallarına ve gizlilik yükümlülüklerine uymakla yükümlüdür. Etik ihlaller; sözlü uyarı, yazılı uyarı, kınama ve iş akdinin feshi şeklinde kademeli olarak yaptırıma bağlanır.",
        "Çıkar çatışması durumları İnsan Kaynaklarına bildirilmelidir. Rüşvet, veri sızıntısı, taciz ve ayrımcılık sıfır tolerans kapsamındadır ve doğrudan fesih nedeni olabilir.",
        "İhbar (whistleblowing) hattı üzerinden yapılan bildirimler gizli tutulur; iyi niyetli ihbarcı misillemeye karşı korunur.",
        "Sosyal medyada şirket adına açıklama yalnızca yetkili kişilerce yapılır. Çalışanlar kişisel hesaplarında şirketi temsil etmediklerini açıkça belirtmelidir.",
    }),
    ("12. İş Sağlığı ve Çalışma Ortamı", new[]
    {
        "Ofis çalışma alanları ergonomi standartlarına uygun tasarlanır. Çalışanlar uzun süreli ekran kullanımında düzenli molalar vermeye teşvik edilir.",
        "Yangın tatbikatı yılda bir kez yapılır ve tüm çalışanların katılımı zorunludur. Acil durum toplanma alanları ve ilk yardım ekipleri her katta ilan edilmiştir.",
        "Ofiste sigara içilmez; yalnızca belirlenmiş açık alanlar kullanılabilir. Ofis mutfağı ve dinlenme alanları ortak kullanım kurallarına tabidir.",
    }),
    ("13. Ücret ve Yan Haklar", new[]
    {
        "Maaşlar her ayın son iş günü ödenir. Bordro İK portalı üzerinden elektronik olarak erişilebilir. Fazla mesai, önceden onaylanmış olması kaydıyla ilgili mevzuata göre ücretlendirilir veya serbest zaman olarak kullanılır.",
        "Yemek kartı ve ulaşım desteği tüm ofis çalışanlarına sağlanır. Yıllık ikramiye, şirket ve bireysel performansa bağlı olarak yıl sonunda değerlendirilir.",
        "Doğum, evlilik ve çocuk eğitimi gibi durumlarda tanımlı sosyal yardımlar İK politikasının ekinde belirtilir. Ücret bilgileri gizlidir ve üçüncü kişilerle paylaşılamaz.",
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
