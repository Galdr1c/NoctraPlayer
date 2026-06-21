# Promosyon Kodu Yönetimi

Bu sürümde promosyon kodu girişi profil ayarlarından çıkarılıp `GlobalSettingsWindow` içine taşındı. Kullanıcı, Global Ayarlar ekranındaki promosyon kartına kod girer; kod doğrulanırsa Free sürüm, kodun `durationDays` değeri kadar süreli Premium olur.

## Kullanıcı Akışı

1. Uygulamada Global Ayarlar ekranı açılır.
2. Promosyon kodu kartındaki textbox alanına kod girilir.
3. `Kodu Kullan` butonuna basılır.
4. Kod geçerliyse Premium bitiş tarihi hesaplanır ve global `settings.json` içine kaydedilir.
5. Premium durum değişikliği `LicenseService.SubscriptionChanged` ile UI tarafına bildirilir.

## Kod Kaynağı

Uygulamada hazır veya fallback promosyon kodu yoktur. Kodlar uzak JSON yapılandırmasından gelmelidir; dokümanda kullanılan kod değerleri yalnızca format örneğidir ve gerçek kampanya kodu değildir.

Gerçek kampanya kodlarını public repository, README veya istemci kodu içine yazmayın. Üretim kampanyaları için sunucu tarafı redemption servisi kullanın.

## Uzak Kontrol

Uygulama promosyon kodlarını uzak bir JSON adresinden okuyabilir. URL üç şekilde verilebilir:

1. `NOCTRA_PROMO_CODES_URL` environment değişkeni.
2. Kullanıcının `%LOCALAPPDATA%/Noctra/settings.json` dosyasındaki `promoCodeConfigUrl` alanı.
3. `LicenseService.cs` içindeki `DefaultRemotePromoCodesUrl` sabiti.

### Mobile Konfigürasyonu

Mobile platformlarda (Android/iOS) environment variable kullanımı pratik olmadığından, promo code URL'si compile-time sabit olarak tanımlanır:

**Dosya:** `Noctra.Mobile/Services/MobileAppConfig.cs`

```csharp
public static class MobileAppConfig
{
    public const string PromoCodesUrl = "https://api.kynora.studio/noctra/promo-codes.json";
}
```

Mobil uygulama başlangıcında `App.axaml.cs` bu URL'yi settings'e otomatik olarak enjekte eder:

```csharp
if (string.IsNullOrWhiteSpace(settingsService.Settings.PromoCodeConfigUrl))
{
    settingsService.Settings.PromoCodeConfigUrl = MobileAppConfig.PromoCodesUrl;
}
```

**Geliştirme Testi İçin:**

Test ortamında local veya development URL kullanabilirsiniz:

1. Local dosya: `file:///storage/emulated/0/Download/test-promo-codes.json`
2. Development server: `http://192.168.1.100:8000/test-promo-codes.json`
3. Test repository: `https://raw.githubusercontent.com/kynora/noctra-test/main/promo-codes.json`

Test için proje root'unda `test-promo-codes.json` örnek dosyası bulunur.

**Production Build:**

Production build öncesi `MobileAppConfig.PromoCodesUrl` sabitini production URL'ine güncelleyin.

Örnek JSON:

```json
{
  "codes": [
    {
      "code": "PROMO-EXAMPLE-7D",
      "durationDays": 7,
      "isActive": true,
      "allowReuse": false,
      "validUntilUtc": "2026-12-31T23:59:59Z",
      "description": "7 gunluk Premium format ornegi"
    },
    {
      "code": "PROMO-EXAMPLE-30D",
      "durationDays": 30,
      "isActive": true,
      "allowReuse": false,
      "description": "30 gunluk Premium format ornegi"
    }
  ]
}
```

Alternatif olarak doğrudan dizi formatı da parse edilebilir:

```json
[
  {
    "code": "NOC-WEEK-2026",
    "durationDays": 7,
    "isActive": true
  }
]
```

## Alanlar

| Alan | Açıklama |
| --- | --- |
| `code` | Kullanıcının gireceği promosyon kodu. |
| `durationDays` | Premium süresi. 0 veya negatif değer geçersiz sayılır. |
| `isActive` | Kod aktif/pasif durumu. |
| `allowReuse` | `false` ise aynı cihazda aynı kod tekrar kullanılamaz. |
| `validUntilUtc` | Kodun son kullanım zamanı. UTC formatında verilmelidir. |
| `description` | Geliştirici/JSON dokümantasyonu için açıklama. |

## Kayıt Edilen Ayarlar

Promosyon bilgileri global ayarlarda saklanır:

```json
{
  "promoCodeConfigUrl": "https://example.com/noctra-promo-codes.json",
  "activePromoCode": "PROMO-EXAMPLE-7D",
  "promoPremiumExpiresAtUtc": "2026-06-01T12:00:00Z",
  "redeemedPromoCodes": [
    "PROMO-EXAMPLE-7D"
  ]
}
```

Dosya konumu:

```text
%LOCALAPPDATA%/Noctra/settings.json
```

Profil ayarı yüklendiğinde global promosyon alanları ana `settings.json` dosyasından senkronize edilir. Böylece promosyon durumu profil bazlı değil uygulama bazlı kalır.

## Davranış Kuralları

- Premium Store paketi zaten kalıcı Premium olduğu için promosyon kodu kullanımı reddedilir.
- Kod boşsa kullanıcıdan kod girmesi istenir.
- Kod bulunamazsa geçersiz sayılır.
- `isActive=false` olan kodlar reddedilir.
- Süresi dolmuş `validUntilUtc` değerleri reddedilir.
- `allowReuse=false` ise aynı kod aynı cihazda tekrar kullanılamaz.
- Kullanıcının mevcut süreli Premium hakkı bitmeden yeni geçerli kod girilirse yeni süre mevcut bitiş tarihinin üzerine eklenir.
- Promo kod URL'si yapılandırılmadıysa yapılandırma hatası gösterilir.
- Uzak JSON okunamazsa veya indirilemezse yükleme hatası gösterilir; bu durum yanlış/geçersiz kod mesajıyla karıştırılmaz.
- Kod listesi yüklendiği halde eşleşme yoksa promosyon kodu bulunamadı/geçersiz sonucu döner.
- Yerel/fallback kod kullanılmaz.

## Güvenlik Notu

Bu çözüm client-side doğrulama yapar. Basit kampanya, test ve kapalı dağıtım senaryoları için uygundur. Üretim seviyesinde kötüye kullanımı engellemek için sunucu tarafında tek kullanımlık kod doğrulama, cihaz/kullanıcı bazlı redemption kaydı ve iptal/geri alma mekanizması önerilir.
