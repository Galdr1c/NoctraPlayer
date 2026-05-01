# Noctra

Noctra, .NET 8 tabanli IPTV oynaticisidir.  
Ana istemci `Avalonia` uzerindedir ve Live / VOD / Series iceriklerini tek arayuzde yonetir.

## Guncel Mimari

- UI: `Noctra.Avalonia` (ana uygulama)
- Core: `Noctra.Core` (viewmodel, servisler, veri erisimi)
- Test: `Noctra.Tests`
- Veritabani: `SQLite + EF Core`
- Oynatici: `LibVLCSharp` (MemoryVideoView ile)


## Temel Ozellikler

### Profil ve Saglayici
- Coklu profil
- Profil bazli playlist ayrimi
- M3U / Xtream / Stalker destegi
- Profil olusturma-duzenleme-silme
- Avatar secimi ve profil bazli veri izolasyonu

### Icerik ve Oynatma
- Live, VOD, Series icerik tipleri
- Arama, grup filtresi, siralama
- Series detay (Season / Episode)
- Overlay kontrolleri (play/pause, seek, ses, track secimi)
- Sonraki bolum promptu ve bolumler paneli
- Watch history + progress takibi

### EPG
- EPG kaynak onceliklendirme
- Arka plan EPG senkronizasyonu
- Manuel EPG yenileme
- Ayarlar ekraninda EPG durum/istatistik gorunumu

### Tema ve UI
- Dark / Light tema secimi
- Tema degisiminin anlik uygulanmasi
- Avalonia temalari:
  - `Noctra.Avalonia/Resources/Themes/DarkTheme.axaml`
  - `Noctra.Avalonia/Resources/Themes/LightTheme.axaml`

## Indirme Sistemi (Guncel)

### Genel
- VOD ve Series episode indirme
- Sifreli yerel dosya formati (`.nctra`)
- Icerigi uygulama ici oynatma (cozumleme cache ile)
- Kuyruk, duraklat/devam et, iptal
- Uygulama tekrar acildiginda indirme durumu geri yukleme

### Indirilenler Ekrani
- Varsayilan gorunum: `Indirilenler` (Film/Dizi ayrimi)
- Ayrica `Indirme Merkezi` gorunumu:
  - Aktif indirmeler
  - Kuyruk
  - Toplam hiz, aktif indirme sayisi, bos alan
- Kuyrukta sadece `Iptal` butonu
- Aktifte `Duraklat/Devam Et + Iptal`

### Son Davranislar
- Indirme ilerleme bari `ProgressBar` ile daha dogru gosterim
- Ag kesintilerinde indirme kaydinin kaybolmamasi (Paused/Resume akisi)
- Manual dosya silinirse stale kayitlarin temizlenmesi
- Indirme klasor yapisi duzenlendi:
  - `.../Downloads/profile_{id}/Filmler/{Film Adi}/...`
  - `.../Downloads/profile_{id}/Diziler/{Dizi Adi}/Sezon 01/...`

## Veri Modeli (Ozet)

- `Profiles`
- `ProviderAccounts`
- `Playlists`
- `Channels`
- `Series`, `Seasons`, `Episodes`
- `WatchHistories`
- `EpgPrograms`
- `DownloadItems`

## Calistirma

Gereksinimler:
- .NET 8 SDK
- Windows

Komutlar:

```powershell
dotnet restore
dotnet build Noctra.sln
dotnet run --project Noctra.Avalonia/Noctra.Avalonia.csproj
```

## Test

```powershell
dotnet test Noctra.Tests/Noctra.Tests.csproj
```

## Microsoft Store Packaging

Noctra now includes a dedicated `MSIX` packaging layer for Microsoft Store submission, with two package variants from the same codebase:

- `Free` edition: `Noctra`
- `Premium` edition: `Noctra Premium`

Primary entry points:

```powershell
.\build\package-store.ps1
.\build\test-store-package.ps1
```

Important notes:

- Local package builds require Visual Studio packaging tools or Build Tools with Desktop Bridge/MSIX support.
- Real Store submission requires replacing placeholder identity values in `Noctra.Packaging/StoreAssociation.props` and `Noctra.Packaging/store-profiles.json` with the exact Partner Center values.
- The biggest remaining certification risk is the packaged runtime behavior of `LibVLCSharp` and `VideoLAN.LibVLC.Windows`.

See `docs/microsoft-store-submission.md` for the full workflow.

## Proje Yapisi

```text
Noctra.Avalonia/   # Ana UI (Avalonia)
Noctra.Core/       # Is mantigi, servisler, modeller, EF Core
Noctra.Tests/      # xUnit testleri
```

## Kisa Gelistirme Notu

Ozellik eklerken su iki adimi birlikte yapin:
1. Kod degisikligi
2. README guncellemesi (ilgili baslik)
