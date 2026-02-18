# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
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

### Changed
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
- `%100` gorunup tamamlanmama durumu giderildi:
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
    - Preload kapasitesi `180 -> 220`.
