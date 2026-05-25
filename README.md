# Noctra Media Player

Noctra, Windows için geliştirilen modern bir IPTV medya oynatıcısıdır. Uygulama; M3U, Xtream Codes ve Stalker Portal kaynaklarını tek bir arayüzde yönetir, canlı yayın / film / dizi ayrımı yapar, izleme geçmişini takip eder ve Free/Premium sürüm mantığıyla çalışır.

> Noctra içerik sağlamaz. Uygulama yalnızca kullanıcının erişim yetkisine sahip olduğu IPTV sağlayıcılarını ve oynatma listelerini görüntülemek için tasarlanmıştır.

## Öne Çıkanlar

- **Çoklu profil sistemi:** Profil bazlı sağlayıcı, favori, izleme geçmişi ve ayar izolasyonu.
- **Sağlayıcı desteği:** M3U, Xtream Codes ve Stalker Portal entegrasyonu.
- **İçerik türleri:** Live TV, VOD/Filmler ve Series/Diziler için ayrı deneyim.
- **Modern Avalonia UI:** Dark/Light tema, lokalizasyon, kart tabanlı içerik listeleri ve premium odaklı ayarlar ekranı.
- **LibVLC oynatıcı:** Canlı yayın, VOD ve dizi bölümleri için VLC tabanlı medya oynatma.
- **EPG sistemi:** Özel EPG kaynakları, saat ofseti, arka plan yenileme ve EPG eşleştirme.
- **İndirme sistemi:** VOD ve dizi bölümleri için yerel indirme, kuyruk, duraklat/devam et ve kaldığı yerden sürdürme.
- **Premium modeli:** Free/Premium edition altyapısı, Microsoft Store yönlendirmesi ve süreli promosyon kodu desteği.
- **Güvenlik ve bakım:** PIN/çocuk profili desteği, adult içerik filtreleme, cache/veritabanı temizleme ve tanı raporu üretimi.

## Teknoloji Yığını

| Katman | Teknoloji |
| --- | --- |
| Uygulama UI | Avalonia UI 11 |
| Dil / Runtime | C# / .NET 8 |
| Oynatma | LibVLCSharp + VideoLAN.LibVLC.Windows |
| MVVM | CommunityToolkit.Mvvm |
| Veri | SQLite + Entity Framework Core |
| Paketleme | MSIX / Windows Application Packaging Project |
| Test | xUnit |

## Proje Yapısı

```text
Noctra.Avalonia/      Ana masaüstü uygulaması, pencere/view katmanı, Avalonia servisleri
Noctra.Core/          ViewModel, model, veri erişimi, lisans, playlist, EPG ve medya servisleri
Noctra.Tests/         Birim ve senaryo testleri
Noctra.Packaging/     Microsoft Store Free/Premium MSIX paketleme projesi
Tester/               Sağlayıcı/stream test aracı ve deep stream test altyapısı
build/                Paketleme ve Store sertifikasyon yardımcı scriptleri
docs/                 Store submission ve geliştirme planları
```

## Temel Özellikler

### Profil ve Sağlayıcı Yönetimi

- Çoklu profil oluşturma, düzenleme ve silme.
- Profil avatarı seçimi.
- Profil bazlı M3U, Xtream Codes ve Stalker Portal hesabı.
- Sağlayıcı verisini güvenli yenileme: başarısız bağlantılarda mevcut listeyi koruma.
- Favoriler, “Listem” ve izleme geçmişi verilerini yenileme sırasında koruma.

### İçerik ve Oynatma

- Canlı TV, film ve dizi sekmeleri.
- Kategori/grup filtreleme, arama ve sıralama.
- Dizi detayında sezon/bölüm yapısı.
- Bölüm paneli, sonraki bölüm önerisi ve “baştan başla” akışı.
- İzleme ilerlemesi, devam etme ve tamamlanma takibi.
- Ses, altyazı, seek, tam ekran ve overlay kontrolleri.
- Sleep timer desteği.

### Metadata ve Akıllı Ayrıştırma

- M3U için kategori ve içerik tipi tahmini.
- Xtream/Stalker sağlayıcılarında sağlayıcı tiplerine öncelik verme.
- TMDB zenginleştirmesi: poster, arka plan, oyuncular, puan, açıklama ve bölüm adı.
- TMDB kullanımı sağlayıcı tipine göre optimize edilir; Xtream/Stalker tarafında gereksiz API çağrıları azaltılır.
- Çok dilli adult içerik tespiti ve kategori sıralama koruması.

### EPG

- Playlist kaynaklı ve özel EPG URL desteği.
- Birden fazla özel EPG kaynağı.
- Free/Premium limitlerine göre özel EPG kaynağı sınırı.
- EPG saat ofseti.
- Manuel ve otomatik yenileme.
- EPG verisi ve database temizliği.

### İndirme Merkezi

- VOD ve dizi bölümü indirme.
- Uygulama içi çözümleme cache’i ile oynatma.
- Aktif indirmeler, kuyruk, hız ve disk alanı takibi.
- Duraklat/devam et, iptal ve uygulama yeniden açıldığında durum geri yükleme.
- Varsayılan indirme yapısı:

```text
%LOCALAPPDATA%/Noctra/Downloads/profile_{id}/Filmler/{Film Adı}/...
%LOCALAPPDATA%/Noctra/Downloads/profile_{id}/Diziler/{Dizi Adı}/Sezon 01/...
```

### Global Ayarlar

Global ayarlar `GlobalSettingsWindow` üzerinden yönetilir:

- Dark/Light tema.
- Uygulama dili.
- Otomatik güncelleme kontrolü.
- Donanım hızlandırma.
- Kullanım istatistikleri tercihi.
- Cache/veritabanı temizleme.
- Hata raporu oluşturma.
- Premium satın alma/yükseltme yönlendirmesi.
- Promosyon kodu ile süreli Premium aktivasyonu.

## Premium ve Promosyon Kodları

Noctra, iki farklı Premium akışını destekler:

1. **Edition tabanlı Premium:** Microsoft Store için ayrı `Free` ve `Premium` paketleri üretilebilir. Premium paket çalıştığında premium özellikler otomatik aktiftir.
2. **Promosyon kodu ile süreli Premium:** Free sürümde, Global Ayarlar ekranındaki promosyon alanından kod girilerek belirli gün kadar Premium açılabilir.

Uygulamada hazır veya fallback promosyon kodu yoktur. Promosyon kodları uzak JSON yapılandırmasından gelmelidir; README'de gösterilen değerler yalnızca format örneğidir ve gerçek kampanya kodu değildir.

Kod yönetimi iki kaynaktan yapılabilir:

1. `NOCTRA_PROMO_CODES_URL` environment değişkeni.
2. `LicenseService.cs` içindeki `DefaultRemotePromoCodesUrl` sabiti.

Uzak JSON örneği:

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
    }
  ]
}
```

Uzak JSON okunamazsa veya kod listesi boşsa promosyon kodu bulunamadı/geçersiz sonucu döner; yerel/fallback kod kullanılmaz. Aynı kodun aynı cihazda tekrar kullanılmasını önlemek için kullanılan kodlar `settings.json` içinde `redeemedPromoCodes` alanına kaydedilir.

> Daha detaylı kullanım ve JSON şeması için `PROMO_CODES_README.md` dosyasına bakın.

## Free / Premium Limitleri

Premium kontrolü `LicenseService` üzerinden yapılır. Ana özellik kontrolleri:

- `ad_free`
- `epg_auto_refresh`
- `resume_playback`
- `sleep_timer`

Ana limit kontrolleri:

- Profil sayısı.
- Özel EPG URL sayısı.

Limit değerleri `Noctra.Core/Models/SubscriptionTier.cs` içinde `TierLimits` üzerinden yönetilir.

## Lokalizasyon

Çeviri dosyaları aşağıdaki klasördedir:

```text
Noctra.Core/Localization/Translations/
```

Mevcut dil dosyaları:

- `tr-TR.json`
- `en-US.json`
- `de-DE.json`
- `es-ES.json`
- `fr-FR.json`

Yeni UI metni eklerken sabit metin yerine çeviri key’i kullanılması önerilir.

## Ayar ve Veri Konumları

Uygulama çalışma verilerini kullanıcı dizininde saklar:

```text
%LOCALAPPDATA%/Noctra/
```

Önemli dosya/klasörler:

```text
settings.json                 Global ayarlar ve promosyon/premium bilgileri
Settings/profile_{id}.json    Profil bazlı ayarlar
Downloads/                    İndirilen içerikler
noctra_v1.db                  SQLite uygulama veritabanı
```

## Geliştirme Ortamı

### Gereksinimler

- Windows 10 1809 veya üzeri.
- .NET 8 SDK.
- Visual Studio 2022 veya Rider/VS Code.
- Microsoft Store/MSIX paketleme için Visual Studio 2022 MSIX Packaging Tools.

### Opsiyonel `.env` Değerleri

Kök dizine veya çıktı klasörüne `.env` dosyası koyabilirsiniz:

```env
TMDB_API_KEY=your_tmdb_api_key
NOCTRA_PROMO_CODES_URL=https://example.com/noctra-promo-codes.json
DEV_PASSWORD=your_developer_password
```

- `TMDB_API_KEY`: Metadata zenginleştirme için kullanılır.
- `NOCTRA_PROMO_CODES_URL`: Uzak promosyon kodu listesi.
- `DEV_PASSWORD`: Global ayarlardaki geliştirici modunu açmak için kullanılır.

## Çalıştırma

```powershell
dotnet restore .\Noctra.Avalonia\Noctra.Avalonia.csproj
dotnet build .\Noctra.Avalonia\Noctra.Avalonia.csproj -c Debug
dotnet run --project .\Noctra.Avalonia\Noctra.Avalonia.csproj
```

Release build:

```powershell
dotnet build .\Noctra.Avalonia\Noctra.Avalonia.csproj -c Release
```

> Not: Repository içinde `.sln` dosyası yoksa komutları doğrudan `.csproj` dosyaları üzerinden çalıştırın.

## Test

```powershell
dotnet test .\Noctra.Tests\Noctra.Tests.csproj
```

Belirli testleri çalıştırmak için:

```powershell
dotnet test .\Noctra.Tests\Noctra.Tests.csproj --filter "FullyQualifiedName~LicenseService"
dotnet test .\Noctra.Tests\Noctra.Tests.csproj --filter "FullyQualifiedName~M3UParser"
```

## Provider / Stream Test Aracı

`Tester/` projesi, sağlayıcı entegrasyonlarını ve stream kalitesini test etmek için yardımcı araç içerir.

Örnekler:

```powershell
dotnet run --project .\Tester\NoctraProviderTester.csproj -- --type m3u --url "http://example.com/list.m3u" --deep-test

dotnet run --project .\Tester\NoctraProviderTester.csproj -- --type xtream --host "http://host" --user "username" --pass "password" --deep-test --live 3 --vod 3 --duration 20
```

Ayrıntılı metrikler için `Tester/DEEP_TEST_GUIDE.md` dosyasına bakın.

## Microsoft Store Paketleme

Noctra aynı kod tabanından iki ayrı Store paketi üretebilir:

- `Noctra` → Free edition.
- `Noctra Premium` → Premium edition.

Store paketleme için:

```powershell
.\build\package-store.ps1
```

Sadece tek edition üretmek için:

```powershell
.\build\package-store.ps1 -Editions Free
.\build\package-store.ps1 -Editions Premium
```

Versiyon vererek paketlemek için:

```powershell
.\build\package-store.ps1 -VersionPrefix 1.2.0
```

Yerel sertifikasyon kontrolü:

```powershell
.\build\test-store-package.ps1
```

Gerçek Store gönderimi öncesinde şu dosyalardaki placeholder değerleri Partner Center bilgileriyle değiştirilmelidir:

```text
Noctra.Packaging/StoreAssociation.props
Noctra.Packaging/store-profiles.json
```

Tam akış için `docs/microsoft-store-submission.md` dosyasını inceleyin.

## Geliştirme Notları

- Yeni özellik eklerken ilgili ViewModel, servis ve test katmanını birlikte güncelleyin.
- UI metinlerini mümkün olduğunca lokalizasyon dosyalarına taşıyın.
- Premium özellik eklerken `LicenseService.Features` veya `LicenseService.Limits` üzerinden kontrol sağlayın.
- Global ayar niteliğindeki değerleri profil ayarlarından ayırıp `settings.json` üzerinde merkezi tutun.
- IPTV sağlayıcı yenilemelerinde kullanıcı verisini silmeden önce bağlantı/yanıt doğrulamasını tamamlayın.
- M3U, Xtream ve Stalker sağlayıcılarının veri modelleri farklı olduğu için agresif normalizasyon yerine sağlayıcı güveni mantığını koruyun.

## Bilinen Notlar

- MSIX paketleme için yalnızca `dotnet` CLI yeterli değildir; `.wapproj` için Visual Studio/MSBuild paketleme araçları gerekir.
- Store sertifikasyonunda en kritik alan LibVLC native bağımlılıklarının paket içinde doğru çalışmasıdır.
- Promosyon kodu sistemi client-side doğrulama yapar ve repository içinde gerçek kod tutulmamalıdır. Üretim seviyesinde tek kullanımlık kod, cihaz/kullanıcı bazlı redemption ve kötüye kullanım koruması için sunucu tarafı doğrulama önerilir.

## Kısa Yol Haritası

- Promosyon kodu metinlerinin tamamen localization key’lerine taşınması.
- Başarı/hata durumuna göre promosyon mesajı renginin ayrıştırılması.
- Uzak promosyon kodu paneli veya geliştirici arayüzü.
- Store submission sonrası gerçek product id ve publisher değerlerinin güncellenmesi.
- Paketlenmiş uygulamada LibVLC runtime doğrulamasının tamamlanması.
