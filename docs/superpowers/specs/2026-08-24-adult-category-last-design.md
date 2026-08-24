# Adult Kategorilerini Sona Yerleştirme Tasarımı

## Amaç

Yetişkin içerik kategorilerini güvenilir ve çok dilli biçimde tanımak; kategori listelerinde ve kategori filtresi `Tümü` durumundayken listelenen içeriklerde bu kategorileri global olarak en sona yerleştirmek.

## Kapsam

- Sınıflandırma yalnızca `GroupTitle` üzerinden yapılır.
- İçerik adı sınıflandırmaya katılmaz. Örneğin normal bir kategorideki `Sex and the City` yetişkin içerik sayılmaz.
- Canlı TV, Filmler ve Diziler sayfalarının `Tümü` görünümü kapsamdadır.
- Belirli bir kategori seçildiğinde o kategorinin kendi ad/tarih sırası korunur.
- Arama sonuçlarının mevcut alaka ve canlı-önce sırası değiştirilmez.
- Gizlenen kategori kuralları ve çocuk profili filtreleri mevcut davranışını korur.

## Merkezi sınıflandırıcı

Tek bir `AdultCategoryClassifier` sınıfı kategori listesi, playlist organizasyonu, M3U tür sezgisi ve `Tümü` içerik sıralaması tarafından paylaşılır.

Sınıflandırıcı:

- büyük/küçük harf farkını kaldırır;
- aksanları normalize eder;
- köşeli parantez, ayraç, tire ve alt çizgi gibi kategori süslerini kelime ayıracı kabul eder;
- kısa terimleri yalnızca tam kelime/token olarak eşleştirir; `sex` terimi `Sussex` içinde eşleşmez;
- çoğul ve yaygın dil varyantlarını destekler.

Desteklenen ana dil grupları:

- İngilizce: `adult`, `adults`, `for adults`, `mature`, `porn`, `erotic`, `xxx`, `18+`;
- Türkçe: `yetişkin`, `erotik`, `porno`, `+18`;
- Almanca: `erwachsene`, `für erwachsene`, `erotik`, `porno`, `ab 18`;
- Fransızca: `adulte`, `adultes`, `pour adultes`, `érotique`, `porno`;
- İspanyolca: `adulto`, `adultos`, `adulta`, `adultas`, `para adultos`, `erótico`, `porno`;
- Portekizce: `adulto`, `adultos`, `adulta`, `adultas`, `conteúdo adulto`, `erótico`, `pornô`;
- İtalyanca: `adulto`, `adulti`, `adulta`, `adulte`, `per adulti`, `erotico`, `porno`;
- Arapça: `للبالغين`, `بالغين`, `للكبار`, yaygın yetişkin/erotik kategori ifadeleri;
- Rusça: `для взрослых`, `взрослые`, `порно`, `эротика`.

Mevcut güvenilir sağlayıcı/marka terimleri korunur. Belirsiz kısa ifadeler kelime sınırı olmadan eşleştirilmez.

## Kategori listesi sırası

Kategori adları üç segmente ayrılır:

1. Kullanıcının dil/ülke tercihine uyan normal kategoriler
2. Diğer normal kategoriler
3. Yetişkin kategorileri

Her segment kendi içinde alfabetik sıralanır. Yetişkin segmenti dil/ülke tercihinden bağımsız olarak her zaman en sonda kalır.

## Canlı TV ve Filmler: `Tümü` içerik sırası

`Tümü` seçiliyken sorgu playlistteki eşsiz ham kategori adlarını hafif bir sorguyla alır. Merkezi sınıflandırıcı yetişkin kategori adlarını belirler. Ham değerler boşluk ve büyük/küçük harf varyantlarını kaybetmeden playlist kanal sayısı/son güncelleme imzasına bağlı kısa ömürlü cache'te tutulur. Kanal sorgusunda bu adlar SQL tarafından çevrilebilen bir `IN` kümesi olarak ilk sıralama anahtarı yapılır:

1. Normal kategorideki içerikler
2. Mevcut kullanıcı sırası: yeni/eski/A-Z/Z-A
3. Yetişkin kategorideki içerikler
4. Aynı mevcut kullanıcı sırası

Birleşik sıralama nedeniyle yalnızca `Id` taşıyan mevcut keyset cursor yeterli değildir. `Tümü` görünümünde kararlı offset sayfalama kullanılır; belirli kategori ve arama akışları mevcut keyset davranışını korur. Böylece yetişkin içerikler yalnızca her sayfanın değil, bütün sonucun sonuna taşınır.

## Diziler: `Tümü` içerik sırası

Bellekteki seri sıralama önbelleğinin ilk anahtarı merkezi sınıflandırıcının yetişkin kategori sonucu olur. Seçilen yeni/eski/A-Z/Z-A sırası her iki segmentin içinde korunur. Sıralama önbelleği mevcut kaynak, gizli gruplar ve sıralama anahtarıyla çalışmaya devam eder.

## Hata ve performans davranışı

- Kategori metadata sorgusu başarısız olursa mevcut sorgu hata yönetimi devreye girer; kısmi veya rastgele bir sıralama yayınlanmaz.
- Eşsiz kategori adları, içerik satırlarından çok daha küçük olduğundan belleğe tüm içerikleri almak gerekmez; aynı scroll oturumundaki sonraki sayfalar kategori cache'ini yeniden kullanır.
- Yetişkin kategori bulunmazsa mevcut sıralama semantik olarak değişmez.
- Seçili kategori ve arama performansı etkilenmez.

## Test stratejisi

- Çok dilli ve çoğul kategori örnekleri yetişkin olarak tanınır.
- `For Adults` tanınır.
- `Sussex`, `Adult Swim Classics` gibi açıkça normal kabul edilen istisneler yanlış pozitif üretmez. `Adult Swim Classics` için açık istisna uygulanır.
- Playlist organizasyonu normal grupları yetişkin gruplardan önce tutar.
- Kategori listesi tercih edilen dil gruplarını önde, yetişkin grupları mutlak sonda tutar.
- Canlı/film `Tümü` sorgusunda küçük sayfa boyutuyla normal içerikler tüm sayfalarda yetişkin içeriklerden önce gelir.
- A-Z, Z-A, yeni ve eski sıralamalarında segment içi sıra korunur.
- Diziler `Tümü` görünümünde aynı adult-last davranışını uygular.
- Belirli yetişkin kategori seçildiğinde içeriklerin kendi normal sırası korunur.
- Arama sıralaması değişmez.

## Kabul ölçütleri

- `For Adults` kategori listesinde en sona gider.
- Desteklenen dillerdeki yaygın yetişkin kategori adları aynı davranışı gösterir.
- `Tümü` görünümünde bu kategorilerin içerikleri sayfa sınırlarından bağımsız olarak en sonda yer alır.
- Normal kategoriye bağlı içerik yalnızca başlığındaki bir kelime nedeniyle yeniden sıralanmaz.
- Mevcut testler, Desktop build ve Android build hatasız tamamlanır.
