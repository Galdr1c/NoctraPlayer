# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Canlı TV Kanal Navigasyonu**: Canlı TV yayınları için oynatıcı arayüzüne (overlay ve PiP) özel "Önceki Kanal" ve "Sonraki Kanal" butonları eklendi. (VOD ve Dizilerdeki 10 saniye atlama butonlarının yerini alır.)
  - **Dinamik Bağlam Çözümlemesi**: İzlenen kanal Geçmiş veya Arama ekranından başlatılmış olsa bile, sıradaki kanalın sıfırdan veritabanı (`AppDbContext`) üzerinden sorgulanıp oynatım listesine dahil edilmesini sağlayan veritabanı fallback sistemi eklendi. Gruptaki kanallar her koşulda sıralı şekilde atlatılabilir.
  - **PiP Etkileşimi**: Saydam köşe sorunlarını aşmak ve Avalonia'nın tıklama yutma hatalarını engellemek için PiP ekranındaki kanal geçiş butonları native `Click` event'leri ile C# arka planına bağlandı.
- **MainWindow Mimari Optimizasyonu (Refactoring)** (2026-02-20):
    - **Bileşen Odaklı Yapı (Component-Based)**: `MainWindow.axaml` içerisinde bulunan tüm ana görünümler (`HomeView`, `LiveView`, `MoviesView`, `SeriesView`, `MyListView`, `DownloadsView`, `HistoryView`, `FavoritesView`, `SearchView`) kendi bağımsız `UserControl` (.axaml ve .cs) dosyalarına ayrıldı.
    - **Performans ve Bakım Kolaylığı**: Ana pencere kodu (XAML ve C#) büyük ölçüde sadeleştirildi. Görüntüleme mantığı, kaydırma efektleri (Parallax) ve sayfalama algoritmaları yalnızca ilgili görünümler belleğe yüklendiğinde ve kendi içlerinde çalışacak şekilde izole edildi.
    - **Modüler Bağlam Menüleri (Context Menus)**: Medya (Kanal/Dizi) sağ tık ve "Listeme Ekle / Favorilere Ekle" gibi dinamik eylemler genel `MainWindow` dosyasından çıkarılıp her UserControl'ün kendi özgü ve güvenli alanına taşındı.  
    - **Derleme Hataları ve Ad Alanı Temizliği**: Bileşen ayrımı sırasında oluşan `x:Name` çakışmaları (CS0542) ve ad alanı çakışmaları (`global::Avalonia.Controls.StyledElement`, `global::Avalonia.Media` - CS0234) kalıcı olarak çözüldü.

- **Picture-in-Picture (PiP) Architecture Modernizasyonu** (2026-02-19):
    - **Single-Window Mimarisi**: VLC (Direct3D11) motorunun Windows üzerinde HWND (pencere tutamacı) kilitlemesi nedeniyle oluşan siyah ekran ve çökme sorunlarını gidermek için tasarlanmıştır. PiP modu artık harici bir pencere açmak yerine, `MainWindow`'u minimal bir "Shell" haline getirerek mevcut HWND'yi korur.
    - **8 Yönlü Orantılı Boyutlandırma**: Pencereyi 4 köşe ve 4 kenardan, 16:9 en-boy oranını koruyacak şekilde büyütüp küçülten özel bir vektörel boyutlandırma mantığı eklendi.
    - **Tüm Yüzeyden Sürükleme**: `MouseCaptureLayer` üzerinden tüm video yüzeyini kapsayan global bir sürükleme (Window Move Drag) sistemi entegre edildi.
    - **1:1 Kontrol Tasarımı & Estetik**: PiP arayüzü görseldekiyle birebir örtüşmesi için iyileştirildi. Orta kontrollere dairesel `Play/Pause` ikonları eklendi. Pencere kenarlarına 12px köşe radiusu ve şık bir çerçeve (`PiPFrame`) uygulandı.
    - **Akıcı Boyutlandırma (Jitter-Free)**: Boyutlandırma mantığı piksel bazlı yuvarlama (pixel snap) ile optimize edilerek, büyütme/küçültme sırasındaki titremeler tamamen giderildi.
    - **Gelişmiş Kırpma (Clipping)**: Videonun köşeleri, PiP çerçevesinin kavislerine uyacak şekilde `PiPContainer` üzerinden dinamik olarak kırpıldı.
    - **Çift Tıklama Kararlılığı (Double-Click Fix)**: PiP modunda çift tıklama yapıldığında pencerenin bug'a girmesi engellendi. Artık çift tıklama, pencereyi güvenli bir şekilde tam ekran moduna döndürüyor.
    - **Olay Yönetimi (Event Handling)**: Fare tıklama olayları (`Handled = true`) izole edilerek, işletim sistemi seviyesindeki istenmeyen pencere komutlarının arayüzü bozması önlendi.
    - **Arayüz Restorasyonu (Layout Fix)**: PiP'den ana pencereye dönüşte `Dispatcher` üzerindeki `Background` önceliği kullanılarak, pencere boyutları ve içerik görünürlüğü "Atomic Restore" yöntemiyle senkronize edildi.
    - **Premium Estetik**: `Border.BoxShadow` ve transparan yüzen kontrol barı ile modern, native hissettiren bir görünüm sağlandı.
    - **Etkileşim Güvenliği**: PiP modunda `PlayerOverlayLayer` tamamen devre dışı bırakılarak, sadece PiP'e özel kontrol barının aktif kalması sağlandı (karışıklık önlendi).
- `Avalonia` ana istemci tarafinda indirilebilir icerik sistemi (`DownloadItems`) eklendi.
- `Indirme Merkezi` ve `Indirilenler` ayri gorunumleri eklendi.
- Indirme listesinde:
  - Aktif indirmeler
  - Kuyruk
  - Tamamlanan indirmeler
  bolumleri eklendi.
- Indirme satirlarina poster, durum, hiz, ETA, boyut, yuzde ve sira numarasi eklendi.
- Ust ozet metrikleri eklendi:
  - Toplam hiz
  - Aktif indirme sayisi
  - Bos disk alani
- Indirilen Series icerikleri icin indirilen episode odakli gosterim ve detay acilisi eklendi.
- Indirilen dosyalar icin klasor hiyerarsisi eklendi:
  - `.../Downloads/profile_{id}/Filmler/{Film Adi}/`
  - `.../Downloads/profile_{id}/Diziler/{Dizi Adi}/Sezon XX/`
- Provider-bagimsiz dizi ilerleme saklama altyapisi eklendi:
  - Yeni tablo: `SeriesEpisodeProgresses` (Profile + normalize series key + sezon + bolum).
  - Schema fixup hem `Avalonia` hem `WPF` girislerinde olusturuluyor.
- Dizi/Bolum tanima regex kapsamı genişletildi:
  - İspanyolca (`Temporada`, `Capitulo`), Portekizce, Fransızca (`Saison`) ve Almanca (`Staffel`, `Folge`) desteği eklendi.
  - Sezon ve bölüm belirteçleri arasındaki boşluklar için tolerans artırıldı (örn: `S01 E01`).
  - Çok dilli regex desteği (TR, EN, ES, PT, FR, DE) ile tüm dizi ayrıştırma, tanıma ve normalizasyon mantığı `SeriesInfoParser`'a merkezileştirildi.
- `MediaService`, `SeriesProgressIdentity`, `M3UParser` ve `MainViewModel` servisleri merkezi parser'ı kullanacak şekilde modernize edildi.
- Arama ve dizi gruplama için "Canonical Key" (mükemmel analiz) sistemi tüm uygulamada standart hale getirildi.
- `MetadataService.cs` ve `PlaylistOrganizerService.cs` servisleri merkezi parser'ı ve normalizasyon motorunu kullanacak şekilde modernize edildi, legacy regex'ler kaldırıldı.
- Canonical dizi gruplama sistemi geliştirildi:
  - Farklı provider'lardan gelen benzer isimli diziler artık tek bir dizi altında birleştirilir.
  - Favori/Listeye ekle aksiyonları canonical anahtar üzerinden tüm eşleşen kayıtlara uygulanır.
- İndirme tamamlama ve doğrulama sistemi iyileştirildi:
  - Şifreleme işlemi atomik hale getirildi (`.tmp` üzerinden yazma ve rename).
  - HMAC doğrulaması için tek-geçişli (`IncrementalHash`) yönteme geçildi (performans artışı).
  - %99.9 gibi çok küçük farklarda sunucunun bağlantıyı kesmesi durumunda tolerans eklendi.
  - Hatalı/yarım kalan `.nctra` dosyalarının oluşması engellendi.  
- `PlaylistOrganizerService.cs` içindeki tüm regex tanımları kaldırıldı ve `SeriesInfoParser`'a taşındı.
- `PlaylistOrganizerService.cs` içindeki `GenerateSimilarityKey` ve `GenerateEpgId` metotları, `SeriesInfoParser`'ın merkezi temizleme ve normalizasyon fonksiyonlarını kullanacak şekilde güncellendi.
- **Ağ Durumu Algılama (Network Status)**:
  - Video oynatıcı overlay panelindeki ağ durum göstergesi (Wi-Fi/Ethernet/Mobil veri/Offline) dinamik hale getirildi.
  - Ethernet tespit mekanizması iyileştirildi; sanal ağ adaptörleri filtrelenerek gerçek internet bağlantısının (Gateway üzerinden) tespiti sağlandı.
  - `PlayerViewModel` üzerindeki sabit "Wi-Fi" tanımı kaldırılarak dinamik `INetworkService` entegrasyonu sağlandı (Arayüzde yanlış durum gösterimi düzeltildi).
  - Ağ durumu metninin yanına ilgili ikonlar (Ethernet, Wi-Fi, Mobil veri, Offline) eklendi.
  - Uygulama artık Ethernet, Wi-Fi ve Çevrimdışı durumlarını sadece "Wi-Fi" yazmak yerine doğru şekilde algılayıp gösteriyor.
  - Video oynatıcı arayüzünde (Overlay) ve ana ekranda (Header) ağ durumuna göre dinamik ikonlar (Ethernet/Wi-Fi/Offline) eklendi.
- **Bağlantı Analizi (Connection Analysis)**:
  - Profil ekleme ekranındaki "Bağlantıyı Analiz Et" butonu güçlendirildi.
  - **Xtream**: Sunucu erişiminin yanı sıra kullanıcı adı/şifre doğruluğunu da (`player_api.php`) kontrol eder.
  - **Stalker**: MAC adresi girilmemiş olsa bile sunucu erişilebilirliğini test etmeye izin verir.
  - **M3U**: Bağlantı hızını (ping) ve HTTP durum kodunu analiz eder.
  - Analiz sonuçları (renkli ikon ve ms bilgisi) profil türü değiştiğinde otomatik temizlenir.
- **Profil Yönetimi İyileştirmeleri**:
  - **Veri Koruma (Caching)**: Profil türleri arasında (Xtream <-> Stalker) geçiş yaparken girilen verilerin kaybolması önlendi.
  - **Akıllı Temizlik**: Stalker moduna geçerken URL otomatik temizlenir (sadece sunucu bırakılır), Stalker'dan çıkarken MAC adresi kullanıcı adından temizlenir.
  - Türkçe karakter sorunları ("Ayarlari" -> "Ayarları") giderildi.
  - Profil ekleme penceresine "Kapat" butonu eklendi ve buton yerleşimleri (Ortalama/Padding) iyileştirildi.
- **Kritik Hata Düzeltmeleri**:
  - Video oynatıcı penceresi kapatılırken oluşan `System.ArgumentNullException (LibVLCSharp)` çökme sorunu giderildi. Artık bellek temizliği (callback detach) güvenli şekilde yapılıyor.

### Issues Resolved
- **Controls Persisting Unintentionally**: Resim içinde resim (PiP) kontrolleri ana oynatıcıda göründü ve otomatik olarak gizlenmeyi reddetti.
- Görünürlüklerini kesin olarak bağlamak için bir `IsPiPMode` izleyici ve bir `IsPiPControlsVisible` hesaplanmış özelliği eklendi. Etkinliksizlik zamanlayıcısı 4,0 saniyeden 2,5 saniyeye kısaltıldı.
- **Visual PiP Jitter**: Belirli köşelerden yeniden boyutlandırma, ciddi kullanıcı arayüzü titremesine neden oluyordu.
- 8 yönlü sürekli yeniden boyutlandırma temiz bir `WindowResizeService`'e çıkarıldı ve titremeye eğilimli tutamaçlar (TopLeft, Top, Left) kaldırıldı, böylece PiP için düzgün sınır eşlemesi korundu.
- **Corner "Ears" Bleeding**: Koyu renkli, sözde yuvarlak köşeli bir öğe, şeffaf sınırların dışına taşmıştı.
- Tüm sözde köşe maskeleri kaldırıldı ve PiP sınırlarının, `CornerRadius="0"` ile işletim sisteminin yerel dikdörtgen çerçevesine uyması sağlandı.
- **VLC External Output Window**: PiP'i kapattıktan sonra, yeni bir video açmak bazen LibVLC'nin uygulama içinde render etmek yerine ayrı bir Direct3D penceresi oluşturmasına neden oluyordu.
- `MemoryVideoView` içinde `WaitForHandleReadyAsync`'i kullanıma sunarak ve `PlayChannelAsync`'i çalıştırmadan önce bekleyerek kritik bir UI iş parçacığı yarış durumunu düzelttik; bu sayede LibVLC her zaman geçerli bir `HWND`'ye bağlanır.

### Testing and Verification

### Fixed
- **Picture-in-Picture (PiP) Hata Düzeltmeleri**:
  - PiP modunda şeffaf çerçeve dışına taşan ve "fare kulağı" ("ears") gibi siyah üçgenlere yol açan yapay köşelikler (Corner Masks) için köklü çözüme gidildi. `PiPContainer` ve `PiPFrame` çerçevelerinin `CornerRadius` değeri sıfırlanarak, PiP penceresinin işletim sisteminde keskin, net bir formda (dikdörtgen) görüntülenmesi sağlandı. Orijinal dev oynatıcıyı taklit etmeye çalışan sahte Corner Masks XAML kodu ve C# logic tetikleyicileri gereksiz karmaşıklığı önlemek için projeden tamamen çıkartıldı. `MainWindow.axaml.cs` temizlendi.
  - PiP penceresinin boyutlandırılmasında, işletim sistemi koordinat uyumsuzluğundan kaynaklanan "titreme" (jitter) sorununu gidermek amacıyla sadece orantıyı (16:9) koruyan en stabil tutamaklar (BottomRight, Right, Bottom) aktif bırakıldı; sorun çıkaran 5 farklı tutamak (TopLeft, Top, vb.) kapatıldı.

### Removed
- `PiPWindow.axaml` ve `PiPWindow.axaml.cs`: Yeni Single-Window PiP mimarisine geçiş nedeniyle tamamen atıl (deprecated) duruma düştüğü için projeden kaldırıldı.

### Changed
- **PiP Mimari Soyutlaması**: `MainWindow` içerisinde bulunan karmaşık 8-yönlü, orantı-korumalı (16:9) Picture-in-Picture yeniden boyutlandırma matematiği ve durum değişkenleri, temiz kod (Clean Code) prensipleri gereği yeni `WindowResizeService` sınıfına soyutlandı. `MainWindow.axaml.cs` dosyasının boyut ve karmaşıklığı büyük ölçüde azaltıldı.
- README tamamen guncellenerek proje gercekligiyle esitlendi:
  - Avalonia ana uygulama
  - WPF legacy notu
  - Guncel indirme akislari ve calistirma komutlari
- `Downloads` menusu varsayilan davranisi `Indirilenler` landing olacak sekilde duzenlendi.
- `Indirme Merkezi` gorunumu buton ile ac/kapat modeline cevrildi.
- Indirme ilerleme bari sabit width hesaplarindan `ProgressBar` kullanimina gecirildi.
- Indirme durum event akislari optimize edilerek UI refresh modeli iyilestirildi.
- Daha iyi bir düzen için indirme ekranında ve dizi detay görünümünde dizi adı ve sezon/bölüm bilgileri ayrı ayrı gösterildi.
- `WatchHistoryService` artik dizi oynatiminda `EpisodeId` disinda provider-bagimsiz episode ilerlemesini de yazar.
- `MainViewModel.ApplyProfileProgressAsync` artik iki kaynakla calisir:
  - Once mevcut `EpisodeId` gecmisi (mevcut davranis),
  - Sonra provider-bagimsiz `SeriesEpisodeProgresses` kayitlari.
- Eski kayitlar icin tek seferlik gecis eklendi:
  - Uygun dizi acildiginda legacy watch-history satirlari yeni provider-bagimsiz tabloya tasinir.
- `Tamamlanan indirmeler` listesi oturum bazli olacak sekilde guncellendi (uygulama yeniden acilisinda sifirdan baslar).
- Oynatma akisinda online oncelik modeli eklendi:
  - `Downloads` disinda ve ag mevcutsa kaynak URL tercih edilir
  - `Downloads` ekraninda veya offline durumda yerel indirilen dosya kullanilir
- Indirilen dizi detay sayfasi icin alternatif gorunum eklendi:
  - `Downloads` icinde dizi detayinda sezon bazli sade (duz) episode listesi gosterimi.
- Indirilen icerik oynatiminda overlay aksiyonlari sadeleştirildi:
  - `Indir` butonu gizlenir.
  - `Hakkinda` butonu/paneli gizlenir.
  - Ses/altyazi, kalite ve tam ekran kontrolleri korunur.
  - VOD icin de indirilen oynatim algisi guclendirildi (`.nctra`, `file://`, profile download path),
    boylece offline/indirilen VOD oynatiminda `Indir` ve `Hakkinda` butonlari gizli kalir.
- **Track Names**: Düzensiz ses ve altyazı etiketlerini temizlemek için geliştirilmiş normalizasyon (sağlayıcı etiketlerini, dil kodlarını ve gereksiz ön ekleri kaldırır)
- **UI Performance**: İçerik indirme işlemi tamamlandığında İndirmeler sayfasının otomatik yenilenmesi hızlandırıldı (gecikme 900 ms'den 250 ms'ye düşürüldü).

### Fixed
- **PiP Etkileşim Gecikmesi ve Çizim Titremesi (Jitter) Çözümleri** (2026-02-20):
  - ~~**Interaction Delay Fix**: PiP modundayken pencereyi taşımak veya boyutlandırmak için "iki kez tıklama" zorunluluğu giderildi.~~ *(Kullanıcı isteği üzerine bu düzeltme geri alındı. Orijinal "önce odaklan, sonra tıkla/sürükle" deneyimi `Activate()` ve `Focus()` çağrılarıyla geri getirildi).*
  - **PiP ESC Kapatma Hatası**: PiP modundayken `ESC` tuşuna basıldığında videonun tamamen kapanması hatası giderildi. Artık sadece PiP modundan çıkılıp ana pencereye dönülüyor.
  - **PiP Kontrol Görünmezlik Hatası (Double Click)**: PiP üzerine çift tıklandığında veya odaklanıldığında kontrollerin kaybolması/buga girmesi sorunu çözüldü. Artık fare tıklamaları `UserInteractionCommand` aracılığıyla arayüz zamanlayıcısını (AutoHideTimer) doğru şekilde tetikliyor.
  - **Anti-Jitter (Titreme Önleyici)**: Pencereyi Top (Üst) ve Left (Sol) kenarlarından 16:9 boyutlandırırken işletim sistemi seviyesinde oluşan titremeler engellendi. Boyutlandırma esnasında pozisyon ve ebat güncellemeleri parçalanmak yerine `Dispatcher.UIThread.Post` (Render Önceliği) ile tek bir atomik çizim karesinde birleştirilerek mükemmel bir akıcılık elde edildi.
- **Video Player Görüntü ve Arayüz Düzeltmeleri** (2026-02-19):
  - **Artifact Çözümü**: VLC `vmem` modülü kaynaklı görüntü bozulmaları (dikdörtgen artifact) giderildi.
    - Render motoru `NativeControlHost` (doğrudan HWND) altyapısına geçirildi.
    - Bu sayede buffer kopyalama ve chroma dönüşüm işlemleri aradan çıkarılarak saf, donanım hızlandırmalı ve artifact'siz görüntü sağlandı.
  - **Overlay İyileştirmesi**: Native pencere üzerinde arayüz çizimi (Airspace sorunu) çözüldü.
    - Kontroller (Play/Pause, Seek, vb.) için video penceresi ile senkronize çalışan **Floating Transparent Window** teknolojisi eklendi.
    - Pencere boyutu değişimi ve Fullscreen geçişlerinde kontrollerin kaybolmaması için Z-Order (`Topmost`) yönetim mekanizması geliştirildi.
    - Alt-Tab geçişlerinde overlay penceresinin diğer uygulamaların üzerinde kalmaması için Aktivasyon takibi eklendi.
    - **Performans Optimizasyonu**: Pencere yeniden boyutlandırma ve taşıma sırasında overlay'in geriden gelmesi (lag) "Hide-on-Interaction" (Debounce) yöntemiyle çözüldü. Hareket bitince overlay anında ve pürüzsüzce belirir.
- **Ayarlar ve Görünüm İyileştirmeleri** (2026-02-19):
  - **Premium Sekme Tasarımı**: Ayarlar penceresindeki sekmeler (Profil, Görünüm vb.) tamamen yenilendi.
    - Özel `ControlTemplate` ile modern seçim göstergesi (indicator) ve pürüzsüz hover efektleri eklendi.
    - Tüm sekme ikonları yüksek kaliteli `MaterialIcon` kütüphanesine geçirildi.
  - **Akıllı ComboBox Seçimi**: Yenileme sıklığı gibi açılır menülerin boş görünmesi sorunu, `Index` bazlı eşleştirme sistemiyle giderildi; artık varsayılan seçenekler otomatik seçili gelir.
  - **Canlı Senkronizasyon**: Ayarlarda yapılan yenileme sıklığı (EPG/Kanal) değişikliklerinin arka plandaki zamanlayıcılara anında yansıması sağlandı (yeniden başlatma gerektirmez).
  - **Düzen ve Boşluk Düzeltmeleri**:
    - `Padding` yerine `Margin` sistemine geçilerek tüm temalarda kararlı alt boşluk sağlandı (60px-80px arası optimize edildi).
    - `ProfilesWindow` (Profil Yönetimi) ekranına tam `ScrollViewer` desteği eklendi, butonların kesilmesi engellendi.
  - **Kritik Hata Düzeltmeleri**:
    - Kapat butonu görünürlüğü artırıldı (Material Icon entegrasyonu).
    - Türkçe karakter hataları, özellikle "Hakkında" bölümündeki metinlerde ("Tum Haklari" -> "Tüm Hakları") düzeltildi.
    - Ayarlar penceresi açıkken yapılan değişikliklerin diğer tüm pencerelerde anında güncellenmesi sağlandı.
- Uygulama başlangıcında aktif/yarım kalan indirmelerin otomatik olarak devam etmesi sağlandı (stuck durumu giderildi).
  - Yazma stream kapanisi sonrasi sifreleme/finalize garantilendi.
  - Kismen tamamlanmis dosyalarda finalize fallback duzeltildi.
- Provider degisiminde dizi bolum progress/tick kaybinin ana nedeni giderildi:
  - Dizi ilerlemesi artik playlist/episode id degisiminden bagimsiz okunur.
- Profil silme akislarinda (`AddProfileViewModel` / `ProfilesViewModel`) yeni
  `SeriesEpisodeProgresses` kayitlari da temizleniyor.
- Ag kesintisi/yanit alamama durumlarinda indirmenin kayitlardan kaybolmasi engellendi.
- `Paused/Resume` davranisinda uygulama yeniden acilisi sonrasi durum toparlama duzeltildi.
- Iptal edilen indirmelerde artik dosya/artik temizligi daha guvenilir.
- Klasorden manuel silinen local indirilen dosyalar icin stale kayit temizligi iyilestirildi.
- Indirme ekraninda kuyruk satirlarinda yalnizca uygun butonlarin gorunmesi duzeltildi.
- Aktif indirme sayisinin kuyrugu da saymasi hatasi duzeltildi.
- Indirilenler ekraninda `Diziler` / `Filmler` basliklarinin icerik yokken gorunmesi duzeltildi.
- `%99.9` civari finalize/lock hatalarinda sifreleme asamasi icin retry mekanizmasi eklendi.
- `Tamamlanan` bolumunde gorunup `Indirilenler`de gorunmeyen VOD icerikler icin fallback listeleme eklendi.
- Yerel indirilen dosyanin online acilis senaryolarinda uygulama kararsizligina sebep olmasi engellendi.
- Indirilen dizi oynatiminda `Siradaki Bolum` aksiyonu guvenli hale getirildi:
  - Siradaki bolum yerelde yoksa gecis yapmaz.
  - Kullaniciya overlay durum mesaji gosterilir (`Siradaki bolum indirilmemis.`).
- `Indirilenler > Dizi > Bolum` acilisinda hata olusursa uygulamanin kapanmasi engellendi:
  - Oynatma event zincirine `try/catch` eklendi.
  - Episode acma akisina guvenli hata yakalama eklendi.
  - Hata durumunda uygulama kapanmak yerine durum mesaji gosterir.
- `Icerik oynatilamadi: Dosya dogrulamasi basarisiz` hatasi icin otomatik onarim eklendi:
  - Bozuk `.nctra` dosyasi tespit edilirse, ilgili `.nctra.part` varsa yeniden sifrelenir.
  - Onarimdan sonra oynatma cache decrypt tekrar denenir.
  - DB'de `TempFilePath` eksik olsa bile ayni klasordeki `*.nctra.part` dosyasi bulunup onarim fallback'i calisir.
  - Path karsilastirma normalize edilerek (file://, mutlak yol) eslestirme guvenilirligi artirildi.
- Tamamlanmis indirmelerde kalan `.nctra.part` artik dosyalari temizleme iyilestirildi:
  - Finalize adiminda `part` silme retry ile yapilir.
  - Periyodik cleanup'ta completed kayitlar icin stale `TempFilePath` temizlenir.
- Indirme akisinda `The response ended prematurely` / erken kapanan yanit senaryosu iyilestirildi:
  - Toplam byte biliniyorsa eksik inen dosya artik `tamamlandi` sayilmaz.
  - Gecici baglanti kesintilerinde otomatik devam denemesi eklendi (`3` deneme).
  - Denemeler siniri asilirsa indirme `Duraklatildi` kalir ve kullaniciya `Devam Et` mesaji gosterilir.
- Kullaniciya gosterilen hata metinleri standartlastirildi:
  - Yeni ortak esleyici: `UserFriendlyErrorMessage`.
  - Teknik/ham `ex.Message` metinleri yerine daha acik mesajlar kullaniliyor
    (`Ag hatasi`, `Kimlik dogrulama hatasi`, `Sunucu hatasi`, `Dosya dogrulama hatasi`, vb.).
  - Uygulanan baslica alanlar:
    - `MainViewModel` durum mesajlari
    - `SettingsViewModel` EPG/Kanal yenileme mesajlari
    - `AddProfileViewModel` analiz/kaydetme hatalari
    - `PlayerViewModel` indirme hata mesaji
    - `MainWindow` oynatma hata mesaji
    - `AvaloniaDialogService` dialog hata detaylari
    - `ContentDownloadService` indirme durdurma/otomatik devam mesajlari
    - `VideoPlayerService` oynatma baslatma hata mesaji
- Uygulama başlangıcında aktif/yarım kalan indirmelerin otomatik olarak devam etmesi sağlandı (stuck durumu giderildi).

### Performance
- Indirme sirasinda progress persistence seyreltildi (zaman + byte esik tabanli).
- Sik DB yazimi ve event spam azaltilarak uzun indirmelerde dusen hiz etkisi azaltildi.
- Download cleanup kontrolleri her cagrida degil belirli araliklarla calisacak sekilde optimize edildi.
- `Cancel/Pause/Resume` komutlarinda UI tarafinda gereksiz ek refresh cagrilari kaldirildi.
- Poster/logo yukleme akisi optimize edildi:
  - `RemoteImage` preload artik eszamanli istekleri sinirliyor (throttle), istek firtinasi azaltildi.
  - `image.tmdb.org` `http` URL'leri otomatik `https`'e normalize ediliyor.
  - HTTP resim isteginde timeout suresi artirildi ve `http -> https` fallback denemesi eklendi.
  - Guvenli performans ayarlari ile gorsel yukleme hizi iyilestirildi:
    - Placeholder fallback gecikmesi `1500ms -> 300ms`.
    - `MaxConnectionsPerServer` `24 -> 32`.
    - HTTP timeout `18s -> 14s`.
    - Retry modeli daha hizli hale getirildi (`2` deneme, `120ms` taban gecikme).
    - Kalici HTTP hatalarinda (`400/401/403/404/410`) fail-fast (gereksiz retry yok).
    - Warmup gecikmesi `140ms -> 50ms`.
- **Add Profile UI**: Profil ekleme/düzenleme penceresi modernize edildi:
  - "True" şeklinde görünen hatalı başlıklar düzeltildi.
  - Pencereye kapatma (X) butonu eklendi.
  - "Xtream bilgileri dönüştürüldü" uyarı mesajı kaldırıldı.
  - Buton metinleri ortalandı ve hizalama düzeltildi.
  - Türkçe karakter sorunları (Ayarlari -> Ayarları, vb.) giderildi.
  - **Profil Sil** butonu için kırmızı hover efekti ve köşe yumuşatma eklendi.
  - **Avatar Düzenle** butonu için hover sırasında ikon büyütme efekti eklendi.
  - **Gelişmiş Bağlantı Analizi**:
      - URL geçerliliği ve sunucu yanıt süresi (Ping) kontrolü eklendi.
      - Hata durumlarında detaylı bilgi (404 Bulunamadı, 401 Yetkisiz vb.) gösterimi eklendi.
      - Bağlantı kalitesine göre renkli ikonlar (Yeşil/Sarı/Kırmızı) entegre edildi.
