# IPTVPlayer

Windows (WPF) tabanli, .NET 8 ile gelistirilmis bir IPTV oynaticisi.
Uygulama M3U/Xtream/Stalker kaynaklarini destekler, EPG verisini otomatik yukler, canli ve VOD/Series iceriklerini tek arayuzde yonetir.

## Mevcut Durum (Su Anki Hali)

- Proje tipi: `WPF + MVVM (CommunityToolkit.Mvvm)`
- Hedef framework: `net8.0-windows`
- Veri katmani: `EF Core + SQLite`
- Oynatici: `LibVLCSharp`
- Cozum yapisi:
  - `IPTVPlayer` (UI/WPF)
  - `IPTVPlayer.Core` (is mantigi, servisler, modeller)
  - `IPTVPlayer.Tests` (xUnit testleri)

## Temel Ozellikler

### 1. Profil ve Saglayici Yonetimi
- Coklu profil destegi
- Profil bazli playlist ayrimi
- Saglayici turleri:
  - M3U
  - Xtream Codes
  - Stalker Portal
- Profil son kullanim zamani takibi
- Profil silerken bagli veri temizligi:
  - Watch history
  - Playlistler
  - Gerekirse provider hesabi

### 2. Playlist ve Kanal Yonetimi
- URL'den M3U ekleme
- Dosyadan M3U ekleme
- Xtream API ile kanal cekme (fallback: M3U endpoint)
- Stalker Portal ile kanal cekme
- Kanal listesinde:
  - Arama
  - Grup filtresi
  - Icerik tipi filtresi (Live/VOD/Series)
  - Favoriler filtresi
  - Siralama (yeni/eski, A-Z, Z-A)
  - Sayfali/yuksek performansli artimli yukleme
- Kanal duzenleme ve secim olaylari
- Favori ve My List yonetimi

### 3. Oynatma Deneyimi
- LibVLC tabanli oynatma
- Live ve VOD/Series ayri davranislari
- Buffering/progress durumu
- Ses seviyesi, mute
- Ses ve altyazi track secimi
- Tam ekran ve kontrol paneli davranislari
- Zapping overlay
- Baglanti/hata durumu gosterimi

### 4. EPG (Elektronik Program Rehberi)
- Otomatik EPG cozumleme ve yukleme
- Ulke tespiti (kanal adlarindan)
- Kaynak onceliklendirme:
  - Provider EPG
  - M3U icindeki tvg kaynagi
  - Ulke bazli acik EPG kaynaklari
- Arka plan EPG senkronizasyonu
- Gunluk cache kontrolu (gereksiz indirme azaltma)
- Kanal-program eslestirme ve simdiki/gelecek program sorgulari
- EPG istatistik ekrani:
  - Toplam program
  - Toplam kanal
  - Son guncelleme
  - Son hata

Not: Ayrintili EPG aciklamalari icin `EPG_README.md` dosyasina bakabilirsiniz.

### 5. Metadata, Series ve Devam Etme
- VOD/Series iceriklerinde metadata zenginlestirme (TMDB entegrasyon noktasi mevcut)
- Series/Season/Episode model yapisi
- Intro/Credits zaman damgasi tabanli davranislar
- Izleme gecmisi (watch history) takibi
- Continue Watching rail'i

### 6. Ayarlar ve Uygulama Davranisi
- Tema (dark/light) degistirme ve kalicilik
- Dil ayari
- Oynatma ayarlari:
  - Auto play next
  - Intro/Credits auto skip secenekleri
  - Varsayilan ses seviyesi
- Altyazi ayarlari
- Indirme tercihleri icin ayar modelleri
- TMDB API key ayari

### 7. Performans ve Dayaniklilik
- Baslangicta schema fixup adimlari
- Batch insert ile hizli kanal kaydi
- Debounce arama/filtreleme
- Auto image preload/caching adimlari
- Global exception handling + crash log

## Veritabani Varliklari (Ozet)

- `Playlists`
- `Channels`
- `Series`, `Seasons`, `Episodes`
- `EpgPrograms`
- `Profiles`
- `ProviderAccounts`
- `WatchHistories`

## Kurulum ve Calistirma

Gereksinimler:
- .NET 8 SDK
- Windows

Komutlar:

```powershell
dotnet restore
dotnet build IPTVPlayer.sln
dotnet run --project IPTVPlayer\IPTVPlayer.csproj
```

## Testler

```powershell
dotnet test IPTVPlayer.Tests\IPTVPlayer.Tests.csproj
```

Mevcut test kapsami:
- `M3UParserTests`
- Temel unit test iskeleti

## Proje Yapisi

```text
IPTVPlayer/
  IPTVPlayer/          # WPF UI
  IPTVPlayer.Core/     # Servisler, modeller, viewmodel'ler, EF Core
  IPTVPlayer.Tests/    # xUnit testleri
  EPG_README.md        # EPG detay dokumani
```

## Yol Haritasi (Ileride Yapacaklarimiz)

- Gelismis test kapsami (servis ve viewmodel seviyesinde daha fazla senaryo)
- CI pipeline (build + test + artefact)
- Provider baglantilarinda daha detayli health-check ve tanilanabilir hata kodlari
- EPG eslestirme kalitesini artiran ek heuristikler
- UI/UX iyilestirmeleri (buyuk playlistlerde daha iyi gozlemleme ve geri bildirim)
- Opsiyonel telemetry/diagnostic modu
- Paketleme ve dagitim sureci (installer/release otomasyonu)

## Yapilanlar Gunlugu

Bu bolumu her yeni tamamlanan ozellikte guncelleyecegiz.

- 2026-02-13: Projeye kapsamli `README.md` eklendi; mevcut durum, ozellikler, kurulum, test, yol haritasi ve gunluk bolumu dokumante edildi.

## Katki Notu

Yeni ozellik eklendiginde veya mevcut davranis degistiginde su iki adimi birlikte guncelleyin:

1. Kod degisikligi
2. `README.md` icindeki ilgili bolum (Ozellikler / Yol Haritasi / Yapilanlar Gunlugu)
