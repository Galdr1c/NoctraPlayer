# Noctra WPF -> Avalonia Gecis Plani (Dusuk Risk)

Bu planin hedefi: mevcut `Noctra` WPF uygulamasini calisir durumda tutarken, yeni `Noctra.Avalonia` UI katmanini adim adim olusturmak ve tasarimi gorunur fark olmadan eslemek.

## Strateji

- `Noctra` (WPF) aynen korunur.
- `Noctra.Core` is kurallari ve ViewModel katmani yeniden kullanilir.
- Yeni UI projesi `Noctra.Avalonia` paralel ilerler.
- Her fazda calisan bir ara cikti uretilir, sonra bir sonraki faza gecilir.

## Kapsam ve Mimari Karari

- Taasinacak:
  - Tum XAML ekranlari (`MainWindow`, `Views/*`)
  - UI servis implementasyonlari (`WpfDispatcherService`, `WpfDialogService`, `WpfThemeService`)
  - WPF spesifik behavior/helper siniflari
- Oldugu gibi kalacak:
  - `Noctra.Core` (servisler, model, ViewModel, EF Core)
  - `Noctra.Tests` (unit test tabani)

## Fazlar

## Faz 0 - Hazirlik ve Iskelet

Hedef: Avalonia proje iskeleti + DI + App startup.

1. `Noctra.Avalonia` projesi eklenir.
2. `Noctra.Core` referansi baglanir.
3. App startup, DI konteyneri ve ana pencere acilisi calisir hale getirilir.
4. `IDispatcherService`, `IDialogService`, `IThemeService` icin Avalonia implementasyonlari yazilir.

Bitis Kriteri:
- `Noctra` (WPF) hala calisiyor.
- `Noctra.Avalonia` bos shell pencere aciyor.

## Faz 1 - Medya Omurgasi

Hedef: LibVLC oynatma akisinin Avalonia tarafinda temel calismasi.

1. `LibVLCSharp` + Avalonia VideoView entegre edilir.
2. `PlayerViewModel` ile play/pause/seek baglantisi kurulur.
3. Tam ekran giris/cikis davranisi temel seviyede calisir.

Bitis Kriteri:
- Canli ve VOD URL oynatma Avalonia tarafinda test edilir.

## Faz 2 - Tema ve Tasarim Tokenlari

Hedef: Renk/font/spacing sistemi birebir.

1. `Resources/Themes/DarkTheme.xaml` ve `LightTheme.xaml` Avalonia stillerine tasinir.
2. Font ve brush key adlari korunur (mecburi degilse yeniden adlandirma yapilmaz).
3. `Resources/Converters.xaml` altindaki converter baglantilari Avalonia uyumlu hale getirilir.

Bitis Kriteri:
- Ekranlar henuz bitmemis olsa da tema tokenlari birebir calisir.

## Faz 3 - Ana Ekran Gocu

Hedef: `MainWindow` ve ana gezinme akisi.

1. `MainWindow.xaml` -> `MainWindow.axaml`.
2. Header/nav/search/list yapisi ayni hiyerarsi ile kurulur.
3. `WindowChrome` yerine Avalonia pencere dekorasyon modeli uygulanir.
4. `InputBindings/MouseBinding` kullanimlari command/event tabanli yeniden eslenir.

Bitis Kriteri:
- Ana ekran akislari (gezinti, arama, secim) Avalonia tarafinda calisir.

## Faz 4 - Overlay ve Player UX

Hedef: `VideoOverlayView` + `PlayerOverlayWindow`.

1. Side-sheet, zapping, volume toast, buffering overlay tasinir.
2. Storyboard/Trigger animasyonlari Avalonia Animation/Transitions ile yeniden yazilir.
3. Overlay pencere davranislari (owner/bounds/sync) birebir eslenir.

Bitis Kriteri:
- Oynatici UX davranislari WPF ile ayni hissi verir.

## Faz 5 - Yardimci Pencereler

Hedef: Profil/ayar/diyalog pencereleri.

1. `ProfilesWindow`, `SettingsWindow`, `GlobalSettingsWindow`.
2. `AddProfileWindow`, `EditChannelWindow`, `AvatarPickerWindow`, `UpsellWindow`, `DialogWindow`.
3. `ShowDialog` ve owner akislari Avalonia tarafinda sabitlenir.

Bitis Kriteri:
- Tum temel modal/non-modal pencere senaryolari tamamlanir.

## Faz 6 - Stabilizasyon ve Parite Testi

Hedef: Uretime hazirlik.

1. Performans optimizasyonu (scroll, image cache, preview).
2. Gorsel parite kontrol listesi ile tum ekranlarin karsilastirmasi.
3. Kritik senaryolar icin smoke test listesi.

Bitis Kriteri:
- WPF ile fonksiyonel parite + kabul edilebilir gorsel fark (<%5 lokal sapma).

## Dosya Bazli Goc Haritasi

- Yuksek risk:
  - `Noctra/MainWindow.xaml`
  - `Noctra/MainWindow.xaml.cs`
  - `Noctra/Views/VideoOverlayView.xaml`
  - `Noctra/Resources/Styles.xaml`
- Orta risk:
  - `Noctra/Views/SettingsWindow.xaml`
  - `Noctra/Views/GlobalSettingsWindow.xaml`
  - `Noctra/Views/ProfilesWindow.xaml`
- Dusuk risk:
  - `Noctra/Views/WatermarkView.xaml`
  - `Noctra/Views/DialogWindow.xaml`
  - `Noctra/Views/AvatarPickerWindow.xaml`

## Teknik Risk Kayitlari

1. `WindowChrome`, `SystemCommands`, Win32 monitor interop dogrudan tasinamaz.
2. WPF `Popup` davranislari (`NonTopmostPopup`) Avalonia tarafinda farkli calisir.
3. `IImageCacheService` WPF `BitmapImage` donuyor; arayuz UI agnostik hale getirilmeli.
4. Storyboard/EventTrigger yogun stiller birebir tasarimda en cok zaman alan bolum.
5. Hover preview (`HoverPreviewService`) WPF `VideoView` tipine bagli.

## Oncelikli Refactor (Goc oncesi)

Bu adimlar WPF'i bozmadan once yapilir:

1. `IImageCacheService` donus tipini UI bagimsiz hale getir.
2. `CachedImage` behavior'ini platforma ozel adapter yapisina cek.
3. `MainWindow.xaml.cs` icindeki pencere yonetimini (fullscreen, overlay sync) servislere bol.
4. `Dialog` acma mantigini ortak interface uzerinden merkezilestir.

## Sprint Plani (Ilk 2 Hafta)

Hafta 1:
1. Faz 0 tamamla (iskelet + DI + app startup).
2. Faz 1 PoC (tek pencerede oynatma).
3. Tema tokenlarinin %60'ini tasi.

Hafta 2:
1. `MainWindow` shell + nav + list iskeleti.
2. Arama ve secim akislarini bagla.
3. Tam ekran ve player gecisini stabilize et.

## Gunluk Kontrol Listesi

1. WPF build yesil mi? (`dotnet build Noctra\\Noctra.csproj`)
2. Core testleri yesil mi? (`dotnet test Noctra.Tests\\Noctra.Tests.csproj`)
3. Avalonia calisiyor mu? (shell/player smoke)
4. O gun tasinan UI parcasinin screenshot karsilastirmasi yapildi mi?
5. Acik regressive bug listesi guncellendi mi?

## Tamamlama Olcutleri (Go-Live)

1. Profil secimi -> ana ekran -> oynatma akisi sorunsuz.
2. Live/VOD/Series oynatma, ses, altyazi, kalite paneli calisir.
3. Tema degisimi ve ayarlar kaliciligi calisir.
4. WPF referansli tipler Avalonia projesinde bulunmaz.
5. En az bir tam smoke senaryosu ard arda 10 kez hatasiz gecer.

