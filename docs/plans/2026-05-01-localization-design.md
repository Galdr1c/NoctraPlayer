# Noctra Localization Design

**Date:** 2026-05-01

**Goal:** Noctra uygulamasında fazlı geçişe uygun, runtime dil değiştirmeyi destekleyen, merkezi ve test edilebilir bir yerelleştirme altyapısı kurmak.

## Problem

Mevcut durumda kullanıcıya görünen metinlerin önemli kısmı AXAML dosyalarına gömülü durumda. Bu yapı:

- Metinleri merkezi olarak yönetmeyi zorlaştırıyor
- Runtime dil değişimini pahalı hale getiriyor
- Eksik çeviri takibini zorlaştırıyor
- Fazlı geçişte hangi ekranların dönüştürüldüğünü belirsiz bırakıyor

## Scope

### Faz 1

- `MainWindow`
- `SettingsWindow`
- `GlobalSettingsWindow`
- Ana navigasyon, arama watermark/tooltips, ayar başlıkları ve açıklamaları

### Faz 2

- Dialog başlıkları ve gövdeleri
- Update/status/error mesajları
- ViewModel içindeki kullanıcıya görünen stringler

### Faz 3

- Diğer görünür view'lar
- Uygulamadaki kalan sabit UI metinleri

### Faz 4

- Eksik çeviri raporlama veya doğrulama aracı

## Chosen Approach

Hibrit bir yapı kullanılacak:

- Merkezi `ILocalizationService`
- Dil bazlı çeviri dosyaları
- Avalonia XAML için küçük bir translate bridge

Bu yaklaşım:

- Mevcut dağınık string yapısını tek servis arkasında toplar
- Runtime dil değişimini destekler
- Fazlı geçişe izin verir
- Çeviri kaynağını ileride değiştirmeyi kolaylaştırır

## Architecture

### Core

- `Noctra.Core/Services/Interfaces/ILocalizationService.cs`
- `Noctra.Core/Services/LocalizationService.cs`
- `Noctra.Core/Localization/Translations/`

Servis sorumlulukları:

- `CurrentLanguage` bilgisini tutmak
- `SetLanguage(...)` ile aktif dili değiştirmek
- `GetString(key)` ile çeviri döndürmek
- `LanguageChanged` olayı ile UI yenilemesini tetiklemek

### UI Bridge

- `Noctra.Avalonia/Localization/LocalizationExtension.cs`

Örnek kullanım:

```xml
<TextBlock Text="{loc:Translate Settings.Language.Title}" />
```

Bu bridge:

- `Text`, `Content`, `ToolTip.Tip`, `Watermark` gibi alanlarda kullanılabilir
- Aktif dil değişince bağlı kontrollerin yeniden değer almasını sağlar

## Translation Files

Önerilen dosyalar:

- `Noctra.Core/Localization/Translations/tr-TR.json`
- `Noctra.Core/Localization/Translations/en-US.json`
- `Noctra.Core/Localization/Translations/de-DE.json`
- `Noctra.Core/Localization/Translations/fr-FR.json`
- `Noctra.Core/Localization/Translations/es-ES.json`

Örnek:

```json
{
  "Settings.Language.Title": "Uygulama Dili",
  "Settings.Language.Description": "Arayüz dilini değiştirin",
  "Settings.General.Title": "Genel"
}
```

## Key Naming Strategy

Anahtarlar ekran bazlı ve düz isimlendirme ile tutulacak:

- `Shell.Search.Placeholder`
- `Shell.Settings.Tooltip`
- `Settings.Language.Title`
- `Settings.Language.Description`
- `Settings.General.Title`
- `Settings.General.AutoUpdate.Title`
- `Settings.General.AutoUpdate.Description`

Bu sayede:

- Arama ve bakım kolaylaşır
- Ekran bazlı geçiş takibi net olur
- Eksik anahtarları bulmak kolaylaşır

## App Integration

`App.axaml.cs` açılışta:

- `ISettingsService.Settings.Language` değerini alır
- `ILocalizationService` içine uygular

Dil değişince:

- Ayarlar kaydedilir
- `ILocalizationService` yeni dili yükler
- `LanguageChanged` olayı görünür metinleri yeniler

Bu işlem veri/state reload yapmaz. Yalnızca kullanıcıya görünen metinler güncellenir.

## Fallback Behavior

Eksik veya hatalı çeviri uygulamayı bozmamalıdır.

Sıra:

1. Seçili dil
2. `en-US`
3. Anahtarın kendisi

Ek kurallar:

- Eksik anahtarlar loglanır
- Başlangıçta çeviri dosyası okunamazsa `en-US` yüklenir

## Language Normalization

UI seçici mevcut kısa kodları koruyacak:

- `tr`
- `en`
- `de`
- `fr`
- `es`

Servis içinde bunlar kültür kodlarına normalize edilecek:

- `tr -> tr-TR`
- `en -> en-US`
- `de -> de-DE`
- `fr -> fr-FR`
- `es -> es-ES`

## Testing Strategy

### Unit Tests

- `GetString` seçili dilde doğru değeri döndürmeli
- Eksik anahtarda `en-US` fallback çalışmalı
- Dil kodları doğru normalize edilmeli
- `LanguageChanged` olayı tetiklenmeli

### UI Verification

- Ayarlarda dil değişince `MainWindow`, `SettingsWindow`, `GlobalSettingsWindow` metinleri anında değişmeli
- Arama watermark ve tooltip metinleri güncellenmeli

### Regression Checks

- `Settings.Language` yükleme/kaydetme akışı bozulmamalı
- Uygulama yeniden açıldığında dil korunmalı

## Migration Strategy

### Commit 1

Altyapı:

- Localization service
- Translation files
- XAML translate extension
- DI kaydı

### Commit 2

Faz 1 dönüşümü:

- `MainWindow`
- `SettingsWindow`
- `GlobalSettingsWindow`

Kural:

- Aynı ekranda yarı sabit yarı localizable yapı bırakılmayacak
- Dönüştürülen metinler tamamen anahtar tabanlı hale getirilecek

## Risks

- Avalonia tarafında markup extension yenileme modeli dikkatli kurulmazsa runtime dil değişimi görünmez kalabilir
- Gömülü stringlerin yüksek sayıda olması ilk faz dışına taşabilecek kaçaklar oluşturabilir
- Encoding sorunu olan mevcut AXAML metinleri dönüşüm sırasında görünür hale gelebilir

## Recommendation

İlk fazı sadece kabuk ve ayar ekranlarıyla sınırlayıp altyapıyı burada stabilize etmek en güvenli yol. Ardından kullanıcıya görünen hata ve durum metinleri ikinci fazda ele alınmalıdır.
