# Noctra Shared Mobile-First UI Implementation Plan

**Design:** `docs/superpowers/specs/2026-09-09-shared-mobile-first-ui-architecture-design.md`  
**Method:** Faz bazlı red-green-refactor; her faz sonunda Android + Windows build,
test, performans ve yaşam döngüsü kapısı

## Phase 0 — Baseline ve mimari sözleşmeleri

1. Mevcut mobil/masaüstü XAML, code-behind, converter, localization, behavior ve
   resource çiftlerinin envanterini `Noctra.Tests` kaynak-sözleşme testinde sabitle.
2. `Noctra.UI` projesinin solution'a eklenmesini, yalnız `Noctra.Core` yönünde
   referans vermesini ve iki platform tarafından referanslanmasını test et.
3. Ortak UI projesinin `Noctra.Mobile`/`Noctra.Avalonia` namespace veya platform
   API bağımlılığı almamasını doğrulayan testler ekle.
4. Responsive sınıflar için pure `AdaptiveLayoutMetrics` testlerini önce kırmızı
   yaz: compact `<600`, medium `600–1023`, expanded `>=1024`; kolon, padding,
   sheet genişliği ve touch-target alt sınırlarını doğrula.
5. Mevcut çözüm build/test baseline'ını kaydet; kullanıcı değişikliklerine ait
   mevcut başarısızlıkları yeni refaktör hatalarından ayır.

## Phase 1 — `Noctra.UI` foundation

1. `Noctra.UI/Noctra.UI.csproj` (`net8.0`) oluştur; Avalonia, Fluent, Inter,
   Material Icons ve `Noctra.Core` referanslarını ekle.
2. Projeyi `NoctraPlayer.sln` içine ve iki platform csproj'sine ekle.
3. `AdaptiveLayoutClass`, `AdaptiveLayoutMetrics`, `IUiCapabilities` ve
   varsayılan capabilities implementasyonunu ekle.
4. Mobil `Tokens.axaml`, `Colors.axaml`, Dark/Light theme,
   `CommonStyles.axaml`, `SettingsStyles.axaml` ve platformdan bağımsız stil
   parçalarını ortak resource dictionary'lere taşı.
5. Platform App resource ağacını ortak dictionary'leri canonical kaynaktan
   yükleyecek şekilde değiştir; eski resource anahtarlarına geçici alias sağla.
6. Localization source/markup extension ve platformdan bağımsız converter'ları
   ortaklaştır; platform dosyalarını wrapper/alias haline getir.
7. Focused contract/unit testleri, `Noctra.UI`, Mobile ve Desktop buildlerini
   çalıştır.

## Phase 2 — İlk dikey dilim: primitive + Home

1. Önce `PremiumSpinner`, empty state ve responsive page scaffold için kırmızı
   source/XAML load testleri ekle.
2. Mobil tasarımı temel alan `PremiumSpinner`, `EmptyStateView` ve
   `AdaptivePageScaffold` ortak kontrollerini ekle.
3. Mobile/Desktop remote-image koordinasyonunun ortak sözleşmesini çıkar;
   platform decode ayrıntısını adapter'da bırak.
4. Continue Watching kartını mobil XAML ve gesture davranışıyla ortaklaştır.
5. `AdaptiveVirtualizingCardGrid` için kolon/row hesaplama ve scroll-state
   testlerini yaz; mobil ve masaüstü grid kodunun ortak algoritmasını taşı.
6. Ortak `HomeView` oluştur ve iki platform wrapper'ını yalnız host haline getir.
7. Compact/medium/expanded Home ölçü testi, iki platform build/test ve manuel
   resize smoke testi yap.
8. Ortak bir stil değerini değiştirerek iki hostta aynı anda yansıdığını kanıtla.

## Phase 3 — Katalog ekranları ve ortak sheet altyapısı

1. Live/Movies/Series tekrarlarını kaynak-sözleşme testleriyle tanımla.
2. Mobil görsel düzeni kullanan parametrik `AdaptiveCatalogView` oluştur.
3. Live, Movies ve Series wrapper'larını ortak view'e bağla; Core
   `MainViewModel` koleksiyonlarını koru.
4. Mobil sort/category akışını ortak `SelectionSheet` ve
   `CategorySelectionView` bileşenlerine taşı.
5. `MobileContentSortSelection` ile `DesktopContentSortSelection` kataloglarını
   tek ortak politika sınıfına indir.
6. Card action request/policy/sheet'i ortaklaştır; touch long-press ile desktop
   right-click/keyboard context eylemlerini aynı command matrisine bağla.
7. Scroll restore, incremental load, adult-last sıralama ve boş/loading state
   regresyonlarını çalıştır.

## Phase 4 — Kartlar, kütüphane ve arama

1. Live/VOD/Series kartlarını mobil tasarımdan ortak kontrollere taşı.
2. `AdaptiveVirtualizingCardGrid` ve `AdaptiveSectionedCardFeed` ile iki
   platformdaki ayrı grid/feed implementation'larını değiştir.
3. Search, Favorites, My List ve History ekranlarını ortaklaştır.
4. Search live-first, stable ordering, fuzzy candidate ve minimum-query UX
   testlerinin tamamını çalıştır.
5. Remote image ownership/decode/failure-cache sözleşmelerini ve büyük liste
   performans testlerini çalıştır.

## Phase 5 — Downloads

1. Mobil Downloads görünümünü canonical kabul ederek ortak DownloadsView çıkar.
2. İndirme durum kartları, speed/ETA, poster fallback, storage göstergesi ve
   episode grouping'i ortaklaştır.
3. Platform storage picker/permission/notification davranışlarını adapter'da
   bırak.
4. Pause/resume/retry/restart, process restart ve profile delete cleanup
   testlerini çalıştır.

## Phase 6 — Profiles, setup, settings ve yardımcı ekranlar

1. Profile list/loading/setup, avatar picker ve PIN içeriklerini ortak
   `UserControl` bileşenlerine taşı.
2. Masaüstü Window sınıflarını ortak içeriği host eden ince wrapper'lara çevir;
   drag/resize/close davranışını Windows'ta bırak.
3. Mobil Settings XAML'ini canonical kabul ederek ortak SettingsView oluştur;
   platforma özgü ayar satırlarını capability görünürlüğüyle slotlara ayır.
4. Legal consent/document, review prompt ve upsell içeriklerini ortaklaştır.
5. Profile/import cleanup, settings autosave, billing restore/premium renewal ve
   banner eligibility regresyonlarını çalıştır.

## Phase 7 — Player ortak presentation katmanı

1. Player presentation bileşenleri için source-contract ve pure layout testleri
   yaz; Android native surface kodunun ortak projeye taşınmadığını doğrula.
2. Mobil tasarımdan ortak timeline, duration labels, center/compact controls,
   transport bar ve top overlay oluştur.
3. Subtitle/preview/watermark presentation bileşenlerini ortaklaştır.
4. Info, More, Quality, Sleep, Track, Subtitle Appearance ve Episodes sheet'lerini
   ortaklaştır.
5. Mobil EPG panelinin görsel ağacını ve presentation hesaplarını ortak UI'ye
   taşı; platform playback/surface lifecycle'ını hostta bırak.
6. Windows `VideoOverlayView` ve Android `MobilePlayerView` bileşenlerini aynı ortak overlay
   ağacını native surface slotlarıyla host edecek biçime getir.
7. Android HDR/SDR, dört rotation, PiP resize, EPG rotation, playback exit,
   background/resume ve MediaSession testlerini çalıştır.
8. Windows fullscreen/windowed resize, seek, track/sheet, keyboard/mouse ve
   playback exit testlerini çalıştır.

## Phase 8 — Adaptive shell ve navigation state

1. Tek `ShellViewModel` ve ortak destination/navigation state için önce test yaz.
2. Mobil bottom navigation ve geniş ekran rail'i aynı ortak adaptive navigation
   content'ine bağla.
3. Android shell'de reklam/native video/system bars/back/lifecycle slotlarını;
   Windows shell'de window chrome/native video/keyboard slotlarını koru.
4. Mobil lightweight MainViewModel ile Core MainViewModel çift bağlamasını kaldır.
5. Page lifetime, settings scope, detail lifetime ve scroll restoration
   testlerini çalıştır.

## Phase 9 — Duplicate cleanup ve release doğrulaması

1. Her ekran için iki hostta doğrulama tamamlandıktan sonra eski çift XAML,
   code-behind, converter, localization, style ve grid dosyalarını kaldır.
2. Yalnız gerçekten platforma özgü sınıflarda Mobile/Desktop ön eklerini bırak.
3. Duplicate canonical UI class/resource bulunmadığını source-contract testiyle
   zorunlu kıl.
4. `CHANGELOG.md` `[Unreleased]` kaydını semptom, kök neden, çözüm, kasıtlı olarak
   ortaklaştırılmayanlar ve doğrulamayla tamamla.
5. Focused ve full test suite, solution build, Android Debug/Release ve Windows
   Free/Premium package buildlerini çalıştır.
6. Android gerçek cihaz/emülatör ve Windows responsive smoke testlerini yap.
7. `git diff --check`, scoped diff review ve bağımsız code review gerçekleştir;
   doğrulanmış bulguları düzeltip testleri tekrar çalıştır.

## Her faz için durdurma kriteri

Bir faz aşağıdaki koşullardan biri gerçekleşirse bir sonraki faza ilerlemez:

- Yeni build/test hatası
- Scroll state veya incremental loading kaybı
- Android native surface/PiP/HDR regresyonu
- Windows window/native-player regresyonu
- Ölçü değişiminde içerik taşması veya erişilemeyen eylem
- Eski view silinmeden önce iki platform doğrulamasının eksik olması

Sorun, aynı faz içinde çözülür; başarısız yaklaşım temizlenir ve çalışan eski
wrapper fallback'i korunur.
