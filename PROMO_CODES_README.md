# Promosyon Kodu Yönetimi

Bu sürümde promosyon kodu girişi profil ayarlarından çıkarılıp `GlobalSettingsWindow` içine taşındı. Kullanıcı, Global Ayarlar ekranındaki promosyon kartına kod girer; kod doğrulanırsa Free sürüm, kodun `durationDays` değeri kadar süreli Premium olur.

## Kullanıcı Akışı

1. Uygulamada Global Ayarlar ekranı açılır.
2. Promosyon kodu kartındaki textbox alanına kod girilir.
3. `Kodu Kullan` butonuna basılır.
4. Kod geçerliyse Premium bitiş tarihi hesaplanır ve global `settings.json` içine kaydedilir.
5. Premium durum değişikliği `LicenseService.SubscriptionChanged` ile UI tarafına bildirilir.

## Hazır Yerel Kodlar

Varsayılan/fallback kodlar `Noctra.Core/Services/LicenseService.cs` içindeki `DeveloperPromoCodes` listesindedir:

```text
NOC-8KQ2-MP7A  -> 7 gün Premium
NOC-T4Z9-P6XD  -> 30 gün Premium
```

Süreleri değiştirmek veya yeni kod eklemek için bu listeyi düzenleyebilirsiniz.

## Uzak Kontrol

Uygulama promosyon kodlarını uzak bir JSON adresinden okuyabilir. URL üç şekilde verilebilir:

1. `NOCTRA_PROMO_CODES_URL` environment değişkeni.
2. Kullanıcının `%LOCALAPPDATA%/Noctra/settings.json` dosyasındaki `promoCodeConfigUrl` alanı.
3. `LicenseService.cs` içindeki `DefaultRemotePromoCodesUrl` sabiti.

Örnek JSON:

```json
{
  "codes": [
    {
      "code": "NOC-8KQ2-MP7A",
      "durationDays": 7,
      "isActive": true,
      "allowReuse": false,
      "validUntilUtc": "2026-12-31T23:59:59Z",
      "description": "7 günlük Premium"
    },
    {
      "code": "NOC-T4Z9-P6XD",
      "durationDays": 30,
      "isActive": true,
      "allowReuse": false,
      "description": "30 günlük Premium"
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
  "activePromoCode": "NOC-8KQ2-MP7A",
  "promoPremiumExpiresAtUtc": "2026-06-01T12:00:00Z",
  "redeemedPromoCodes": [
    "NOC-8KQ2-MP7A"
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
- Uzak JSON okunamazsa yerel/fallback kodlar kullanılır.

## Güvenlik Notu

Bu çözüm client-side doğrulama yapar. Basit kampanya, test ve kapalı dağıtım senaryoları için uygundur. Üretim seviyesinde kötüye kullanımı engellemek için sunucu tarafında tek kullanımlık kod doğrulama, cihaz/kullanıcı bazlı redemption kaydı ve iptal/geri alma mekanizması önerilir.
