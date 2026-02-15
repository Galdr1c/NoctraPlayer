# WPF -> Avalonia Taskboard

Durum anahtari:
- `[ ]` baslanmadi
- `[/]` devam ediyor
- `[x]` tamamlandi

## Faz 0 - Iskelet

- [x] `Noctra.Avalonia` projesini olustur
- [x] `Noctra.Core` referansi ekle
- [x] `App` startup + DI konfigurasyonu
- [x] `MainWindow` shell acilisi
- [x] Avalonia `IDispatcherService` implementasyonu
- [x] Avalonia `IDialogService` implementasyonu (stub)
- [x] Avalonia `IThemeService` implementasyonu

## Faz 1 - Oynatici PoC

- [x] `LibVLCSharp` Avalonia paketlerini ekle
- [x] Tek `VideoView` ile oynatma denemesi
- [x] `PlayerViewModel` baglantisi
- [x] Play/Pause/Stop komutlari calisiyor
- [x] Tam ekran giris/cikis temel akisi

## Faz 2 - Tema ve Stil

- [x] `DarkTheme` tokenlari tasindi
- [x] `LightTheme` tokenlari tasindi
- [x] Fontlar tasindi
- [x] Ana `Style` kaynaklari tasindi (key envanteri + placeholder)
- [x] Converter altyapisi tasindi

## Faz 3 - Ana UI

- [x] Header ve pencere kontrolleri (ilk tasima)
- [x] Sol nav alanlari (ilk tasima)
- [x] Home/Live/Movies/Series gorunumleri (shell seviyesinde)
- [x] Arama overlay
- [x] Context menu aksiyonlari
- [x] Scroll davranislari

## Faz 4 - Player Overlay

- [x] `VideoOverlayView` tasindi
- [x] Side panel animasyonlari
- [x] Zapping overlay
- [x] Volume toast
- [x] Buffer/loading katmanlari

## Faz 5 - Yardimci Pencereler

- [x] `ProfilesWindow`
- [x] `SettingsWindow`
- [x] `GlobalSettingsWindow`
- [x] `AddProfileWindow`
- [x] `EditChannelWindow`
- [x] `AvatarPickerWindow`
- [x] `DialogWindow`
- [x] `UpsellWindow`

## Faz 6 - Parite ve Stabilizasyon

- [x] Gorsel parite checklist (ekran bazli)
- [ ] Performans kontrolu
- [/] Hata kayitlarinin temizlenmesi
- [/] Smoke test senaryolari
- [x] Paketleme ve dagitim denemesi

## Blokajlar

- [/] Bu ortamda Avalonia template kurulumu icin NuGet source eksik
  - Not: `dotnet new install Avalonia.Templates` komutu kaynak bulamadigi icin basarisiz.
