# Noctra Ortak Mobile-First UI Mimarisi

## 1. Amaç

Noctra'nın Android ve Windows arayüzlerinde platforma özgü olmayan bütün
görsel yapı ve etkileşimleri tek kaynaktan üretmek. Mobil uygulamadaki mevcut
tasarım, düzen ve etkileşim dili kaynak kabul edilir. Bundan sonra ortak bir
ekranda yapılan değişiklik iki platforma da otomatik yansır.

Bu çalışma yalnız dosya tekrarını azaltmak değildir. Ortak UI; küçük telefon,
büyük telefon, tablet, dar masaüstü penceresi ve geniş masaüstü penceresinde
aynı bileşenlerin ölçüye ve giriş biçimine göre uyarlanmasını sağlar.

## 2. Değişmez ilkeler

1. Mobil tasarım kaynak gerçektir; eski masaüstü görünümü referans alınmaz.
2. `Noctra.Core` Avalonia veya platform UI bağımlılığı almaz.
3. Ortak UI, Android ya da Windows projesine referans vermez.
4. Platform farkları işletim sistemi adına göre değil, mümkün olduğunda
   yeteneklere göre modellenir: dokunma, hover, klavye, güvenli alan, native
   video yüzeyi, pencere komutları ve reklam alanı.
5. Büyük listelerde sanallaştırma, kademeli yükleme, görsel önbelleği ve mevcut
   performans bütçeleri korunur.
6. Eski platform ekranı, ortak karşılığı iki platformda doğrulanmadan silinmez.
7. Her faz iki platformda derlenebilir ve geri alınabilir durumda tamamlanır.

## 3. Proje yapısı

Yeni `Noctra.UI` projesi `net8.0` hedefler. Böylece mevcut
`Noctra.Avalonia` (`net8.0-windows`) ve `Noctra.Mobile` (`net10.0`) projeleri
aynı kütüphaneye referans verebilir.

```text
Noctra.Core
├─ modeller ve iş servisleri
├─ MainViewModel, PlayerViewModel ve diğer ortak ViewModel'ler
└─ UI'dan bağımsız platform sözleşmeleri

Noctra.UI
├─ Resources
│  ├─ Tokens, Colors, Themes
│  └─ CommonStyles, SettingsStyles, PlayerStyles
├─ Converters ve Localization
├─ Behaviors
├─ Controls
│  ├─ kartlar, boş/yükleniyor durumları
│  ├─ sanallaştırılmış adaptive grid/feed
│  └─ responsive scaffold ve sheet altyapısı
├─ Views
│  ├─ ana içerik sayfaları
│  ├─ profil, ayarlar, yasal metin ve upsell
│  └─ player overlay, EPG, timeline ve sheet'ler
└─ Hosting
   ├─ UI yetenekleri ve breakpoint durumu
   └─ platform slot/sözleşmeleri

Noctra.Mobile
├─ Android host/shell
├─ reklam bannerı
├─ SurfaceView, PiP, rotation ve system bars
├─ Android back/lifecycle
└─ Android platform servisleri

Noctra.Avalonia
├─ Windows host ve pencere chrome'u
├─ LibVLC/native video host
├─ klavye, mouse ve pencere komutları
├─ dosya seçici ve Microsoft Store entegrasyonu
└─ Windows platform servisleri
```

`Noctra.UI`, `Noctra.Core` ve ortak Avalonia paketlerine referans verir;
`Noctra.Mobile` veya `Noctra.Avalonia` namespace referans vermez. Böylece döngüsel
bağımlılık oluşmaz.

## 4. Responsive model

Ortak görsel ağaç, üç ölçü sınıfı kullanır:

| Sınıf | Kullanım | Temel davranış |
|---|---|---|
| Compact | Telefon/dar pencere | Tek sütun, bottom navigation, tam genişlik sheet |
| Medium | Büyük telefon/tablet/dar masaüstü | Daha fazla kolon, genişletilebilir rail, sınırlı sheet genişliği |
| Expanded | Tablet landscape/geniş masaüstü | Navigation rail, çok kolon, uygun ekranlarda yan panel |

Kesin eşikler tek bir `AdaptiveLayoutMetrics` sınıfında tutulur; XAML içinde
dağınık magic number kullanılmaz. İlk varsayılanlar compact `< 600`, medium
`600–1023`, expanded `>= 1024` logical piksel olur. Eşikler render ve gerçek
cihaz testleriyle ayarlanabilir.

Görsel dil bütün sınıflarda mobilden gelir: aynı kart biçimi, radius, renk,
tipografi, sheet başlıkları, boş durumlar, spinner, timeline ve transport
kontrolleri kullanılır. Geniş ekranda yalnız yerleşim kapasitesi artar; ayrı bir
"masaüstü tasarımı" oluşmaz.

Input davranışı ölçüden ayrı değerlendirilir. Hover/focus durumları pointer ve
klavye bulunan cihazlarda eklenir; dokunma hedefleri hiçbir ölçüde mobil minimum
boyutunun altına düşmez.

## 5. Ortaklaştırılacak katmanlar

### 5.1 Kaynaklar ve altyapı

- Mobil `Tokens.axaml`, `Colors.axaml`, Dark/Light theme ve ortak stiller
  `Noctra.UI` içine taşınır ve canonical kaynak olur.
- Masaüstü `DesktopModernStyles.axaml` içindeki yalnız platform chrome'una ait
  kurallar masaüstünde kalır; görsel bileşen kuralları ortak stillere taşınır.
- İki taraftaki `LocalizationSource`, `TranslateExtension`, enum/equality,
  responsive metrik, indirme, subtitle ve içerik converter'ları ortaklaştırılır.
- Android veya Windows API kullanan converter/servisler platform projesinde kalır.

### 5.2 Kartlar, görseller ve büyük listeler

Mobil kartlar temel alınarak `ContinueWatchingCard`, `LiveTvCard`, `VodCard`
ve `SeriesCard` ortak kontrollere dönüşür. `Mobile`/`Desktop` ön ekleri kalkar.

`MobileVirtualizingCardGrid` ve `DesktopVirtualizingCardGrid`, tek
`AdaptiveVirtualizingCardGrid` içinde birleşir. Kolon sayısı, kart ölçüsü,
overscan ve satır yüksekliği `AdaptiveLayoutMetrics` üzerinden hesaplanır.
Mevcut incremental collection'lar, item recycling ve scroll state korunur.

Remote image yükleme de tek ortak kontrol/coordinator üzerinden yürür; platform
yalnız decode/native bitmap ayrıntısı gerektiriyorsa adapter sağlar.

### 5.3 İçerik sayfaları

Aşağıdaki ekranlar ortak `UserControl` olur:

- Home
- Live
- Movies
- Series
- Search
- Favorites
- My List
- History
- Downloads
- Series Detail

Live/Movies/Series için tek `AdaptiveCatalogView` şablonu kullanılır; başlık,
kart türü, koleksiyon ve empty-state metinleri parametrelenir. Kategori/sıralama
sheet'i, paging ve scroll-state aynı altyapıyı kullanır.

### 5.4 Profil, ayarlar ve yardımcı akışlar

Profile list, profile loading, profile setup/add-edit, avatar picker, PIN,
settings, legal consent/document, review prompt ve upsell içerikleri ortak
`UserControl` olarak taşınır.

Mobil bu içerikleri page/sheet olarak; masaüstü gerektiğinde aynı içeriği ince
bir `Window` host içinde açar. Window başlığı, drag alanı, resize ve native close
yalnız masaüstü host'un sorumluluğudur. İç form, validation, kartlar ve eylemler
tek kaynaktır.

### 5.5 Player UI

Mobil player tasarımı bütünüyle kaynak kabul edilir. Aşağıdakiler ortak olur:

- top overlay
- center/compact controls
- timeline ve süre yerleşimi
- transport bar
- subtitle ve subtitle preview overlay
- info, more, quality, sleep, track, appearance ve episode sheet'leri
- EPG panelinin görsel/presentation katmanı
- watermark presentation

Android SurfaceView, HDR, PiP resize, rotation, safe-area ve MediaSession kodu
Android host'ta kalır. Windows LibVLC video yüzeyi, pencere tam ekranı ve native
klavye/mouse entegrasyonu Windows host'ta kalır. Ortak player katmanı yalnız
`PlayerViewModel`, `IPlayerSurfaceHost` ve `IUiCapabilities` sözleşmelerini
görür.

Video yüzeyi, reklam veya native host ortak overlay'in içine doğrudan taşınmaz;
ortak view'in tanımladığı slot'a platform host tarafından yerleştirilir. Böylece
mevcut Android SurfaceView/PiP/HDR düzeltmeleri korunur.

## 6. State ve olay akışı

Ortak ekranlar doğrudan platform singleton'larına ulaşmaz. Akış:

```text
Platform host
  ├─ IUiCapabilities / safe area / input mode
  ├─ native surface ve reklam slotları
  └─ platform servis implementasyonları
               ↓
Noctra.UI ortak view + behavior + command
               ↓
Noctra.Core ortak ViewModel ve servis sözleşmeleri
```

Code-behind yalnız saf presentation işi için kullanılır: ölçüm, focus, pointer
gesture, animation ve scroll restoration. İş eylemleri command veya açık UI
sözleşmeleri üzerinden ViewModel'e gider.

Mobildeki hafif shell ViewModel ile Core `MainViewModel` arasındaki çift bağlama
kademeli olarak tek `ShellViewModel`/navigation state modeline indirilir. İçerik
sayfaları her zaman Core ViewModel kullanır.

## 7. Geçiş fazları

### Faz 0 — Koruma ve ölçüm

- Mevcut Android/Windows smoke-test referansı alınır.
- UI kaynak sözleşme testleri ve iki platform build kapısı eklenir.
- Kritik scroll position, item count, player lifecycle ve memory metrikleri
  kaydedilir.

### Faz 1 — `Noctra.UI` temeli

- Proje eklenir ve iki platformdan referanslanır.
- Mobil token/theme/style, localization ve platformdan bağımsız converter'lar
  taşınır.
- Eski resource anahtarları geçici forwarding dictionary ile korunur.

### Faz 2 — Primitives, kartlar ve sanallaştırma

- Empty state, spinner, RemoteImage, pressable card ve kartlar taşınır.
- İki virtualizing grid tek adaptive grid'e dönüştürülür.
- Home ortak view'e geçirilerek mimari dikey dilim olarak doğrulanır.

### Faz 3 — Katalog ve kütüphane ekranları

- Live, Movies, Series, Search, Favorites, My List, History ortaklaştırılır.
- Selection/category/card-action sheet'leri ve scroll state birleşir.

### Faz 4 — Downloads, profiles ve settings

- Downloads ve performans göstergeleri ortaklaştırılır.
- Profil, PIN, avatar, settings, legal, review ve upsell içerikleri taşınır.
- Masaüstü Window'lar yalnız host haline gelir.

### Faz 5 — Player presentation

- Mobil timeline/transport/overlay/sheet/EPG görselleri ortaklaştırılır.
- Android ve Windows native surface host'ları ortak slotlara bağlanır.
- HDR, PiP, rotation, playback exit ve MediaSession regresyonları ayrıca test
  edilir.

### Faz 6 — Shell ve temizlik

- Ortak responsive navigation content'i devreye alınır.
- Yalnız Android/Windows'a özel shell kodu platformlarda bırakılır.
- Doğrulanmış eski çift XAML/code-behind, converter ve style dosyaları silinir.
- İsimlerdeki `Mobile`/`Desktop` ön ekleri yalnız gerçekten platforma özgü
  sınıflarda korunur.

## 8. Hata yönetimi ve geri dönüş

- Her ortak view yükleme hatası uygulama çapında boş ekran üretmemeli; host
  kullanıcıya mevcut localized hata/empty state'i göstermelidir.
- Native surface veya platform capability bulunamazsa ilgili özellik fail-closed
  olur; katalog ve uygulama shell'i çalışmaya devam eder.
- Her faz ayrı değişiklik grubu olarak tutulur. Bir ekran için ortak view
  başarısız olursa platform wrapper geçici olarak eski view'e dönebilir.
- Eski view ancak iki platform build, test ve smoke doğrulamasından sonra
  kaldırılır.

## 9. Test ve kabul ölçütleri

Her fazda zorunlu kontroller:

1. `dotnet build` bütün çözümde başarılı.
2. `dotnet test --no-build` bütün test projelerinde başarılı.
3. Ortak XAML iki host tarafından yükleniyor; duplicate canonical resource
   anahtarı yok.
4. Compact, medium ve expanded ölçülerinde snapshot/measure testleri geçiyor.
5. Klavye focus sırası, hover ve touch hedefleri doğrulanıyor.
6. Büyük kataloglarda sanallaştırma korunuyor; ilk açılış, scroll ve memory
   ölçümleri mevcut baseline'ı anlamlı biçimde kötüleştirmiyor.
7. Android'de background/foreground, rotation, PiP, EPG ve playback exit smoke
   testleri geçiyor.
8. Windows'ta pencere resize, maximize/fullscreen, keyboard navigation, player
   exit ve Store edition smoke testleri geçiyor.
9. Ortak bir bileşende yapılan kontrollü stil değişikliğinin iki platformda da
   ek kopyalama olmadan görünmesiyle ana hedef kanıtlanıyor.

## 10. Bilinçli olarak ortaklaştırılmayacaklar

- Android Activity/Application, reklam SDK'ları, UMP/HMS consent, system bars,
  back dispatcher, MediaSession, SurfaceView, HDR ve PiP lifecycle
- Windows pencere chrome'u, drag/resize, Microsoft Store, Windows notification,
  file picker ve LibVLC native host
- Platform sertifika/paketleme ve uygulama yaşam döngüsü giriş noktaları

Bu parçalar aynı iş sözleşmelerine bağlanabilir ancak tek UI dosyasına zorla
taşınmaz. Böylece platform özellikleri korunurken ürünün görünen ve etkileşimsel
arayüzü tek kaynaktan yönetilir.

## 11. Tamamlanma tanımı

Çalışma, yalnız yeni proje eklendiğinde değil, aşağıdaki koşulların tümü
sağlandığında tamamlanmış sayılır:

- Platforma özgü olmayan görünür UI için tek canonical XAML/control vardır.
- Mobilde ortak bir UI değişikliği masaüstünde ayrıca düzenleme gerektirmez.
- Masaüstü, mobil tasarım dilini adaptive expanded düzenle kullanır.
- Player dahil ortak görsel katman mobil tasarım kaynaklıdır.
- Eski çift kaynaklar kaldırılmıştır ve çözümde yeni duplicate UI katmanı
  bırakılmamıştır.
- Performans, lifecycle ve platform özelliklerinde doğrulanmış regresyon yoktur.
- `CHANGELOG.md` altında semptom/kök neden/değişiklik/kasıtlı olarak
  değiştirilmeyenler/doğrulama bilgileri bulunur.
