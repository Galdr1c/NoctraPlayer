# Mobile Promo Code Implementation Summary

## Problem
Masaüstünde `.env` dosyasından `NOCTRA_PROMO_CODES_URL` environment variable ile çekilen promo code URL'si, mobile platformlarda (Android/iOS) çalışmıyordu.

## Çözüm
Mobile için compile-time configuration sistemi oluşturuldu.

## Değişiklikler

### 1. MobileAppConfig.cs (YENİ)
**Dosya:** `Noctra.Mobile/Services/MobileAppConfig.cs`

```csharp
public static class MobileAppConfig
{
    public const string PromoCodesUrl = "https://example.com/noctra-promo-codes.json";
}
```

- Promo code URL'si compile-time sabit olarak tanımlandı
- Production build öncesi bu dosyada URL güncellenmeli
- Test için local/development URL kullanılabilir

### 2. App.axaml.cs (GÜNCELLENDİ)
**Dosya:** `Noctra.Mobile/App.axaml.cs`

`OnFrameworkInitializationCompleted()` metoduna şu kod eklendi:

```csharp
// Inject mobile-specific promo code URL into settings
if (Services?.GetService(typeof(ISettingsService)) is ISettingsService settingsService)
{
    if (string.IsNullOrWhiteSpace(settingsService.Settings.PromoCodeConfigUrl))
    {
        settingsService.Settings.PromoCodeConfigUrl = Mobile.Services.MobileAppConfig.PromoCodesUrl;
    }
}
```

- App başlangıcında promo URL otomatik enjekte edilir
- Settings'de zaten URL varsa override edilmez

### 3. test-promo-codes.json (YENİ)
**Dosya:** `test-promo-codes.json`

Test için 3 örnek promo code:
- `MOBILE-TEST-7D` - 7 gün, tekrar kullanılabilir
- `MOBILE-TEST-30D` - 30 gün, tekrar kullanılabilir
- `NOCTRA-PREMIUM-90D` - 90 gün, tek kullanımlık

### 4. PROMO_CODES_README.md (GÜNCELLENDİ)
Mobile configuration bölümü eklendi:
- Compile-time URL setup açıklaması
- Development/test seçenekleri
- Production build talimatları

## UI Durumu
Mobile Settings UI zaten mevcuttu (satır 830-890):
- Promo code input field ✅
- Apply button ✅
- Status message display ✅
- Premium status display ✅

## Backend Durumu
`SettingsViewModel.ApplyPromoCodeAsync()` metodu zaten mevcuttu ve çalışır durumda:
- LicenseService entegrasyonu ✅
- Success/error handling ✅
- UI state updates ✅

## Kullanım

### Development Test
1. `MobileAppConfig.PromoCodesUrl`'yi test URL'ine set edin:
   ```csharp
   public const string PromoCodesUrl = "http://192.168.1.100:8000/test-promo-codes.json";
   ```

2. Local HTTP server başlatın:
   ```bash
   cd D:\IPTVPlayer
   python -m http.server 8000
   ```

3. Mobile app'i build edip çalıştırın

4. Settings > Promo Code bölümünden test kodlarını girin

### Production Build
1. `MobileAppConfig.PromoCodesUrl`'yi production URL'e güncelleyin:
   ```csharp
   public const string PromoCodesUrl = "https://api.kynora.studio/noctra/promo-codes.json";
   ```

2. Production build alın

## Test Kodları
```
MOBILE-TEST-7D      → 7 günlük premium
MOBILE-TEST-30D     → 30 günlük premium
NOCTRA-PREMIUM-90D  → 90 günlük premium (tek kullanım)
```

## Teknik Detaylar

### LicenseService Flow
1. User promo code girer
2. `SettingsViewModel.ApplyPromoCodeAsync()` çağrılır
3. `LicenseService.ApplyPromoCodeAsync()` uzak JSON'ı çeker
4. `GetRemotePromoCodesUrl()` sırasıyla kontrol eder:
   - Environment variable `NOCTRA_PROMO_CODES_URL`
   - Settings `PromoCodeConfigUrl` ← Mobile burayı kullanır
   - `DefaultRemotePromoCodesUrl` constant
5. Code validate edilir ve premium süre eklenir

### Settings Persistence
Promo durumu global `settings.json`'a kaydedilir:
```json
{
  "promoCodeConfigUrl": "https://...",
  "activePromoCode": "MOBILE-TEST-7D",
  "promoPremiumExpiresAtUtc": "2026-06-28T...",
  "redeemedPromoCodes": ["MOBILE-TEST-7D"]
}
```

## Sorun Giderme

### "Promo kodu yapılandırması bulunamadı"
- `MobileAppConfig.PromoCodesUrl` empty veya null
- App başlangıcında injection çalışmamış

### "Promo kodu yapılandırması yüklenemedi"
- URL erişilemiyor (network/CORS)
- JSON formatı hatalı
- Server 404/500 dönüyor

### "Promo kodu bulunamadı"
- Girilen kod JSON'da yok
- Code case-sensitive değil ama whitespace/typo olabilir

### "Promo kodu aktif değil"
- JSON'da `isActive: false`

### "Promo kodu daha önce kullanılmış"
- `allowReuse: false` ve kod `redeemedPromoCodes` listesinde

## Not
Bu implementasyon client-side validation kullanır. Production için server-side redemption servisi önerilir (rate limiting, fraud prevention, analytics).
