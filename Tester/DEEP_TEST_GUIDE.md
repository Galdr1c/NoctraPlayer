# Noctra Provider Tester — Deep Stream Test Kılavuzu

## Dosyalar

```
StreamAnalyzer.cs      → Headless VLC probe motoru (buraya ekle)
DeepTestExtension.cs   → Program.cs'e extension (partial class)
Program.cs             → Ana uygulama (--deep-test desteği)
```

---

## Entegrasyon: TestParser Projesine Ekle (Önerilen)

Mevcut `TestParser/` klasörüne sadece şu iki dosyayı kopyala:

```
TestParser/
  StreamAnalyzer.cs       ← YENİ
  DeepTestExtension.cs    ← YENİ
  Program.cs              (mevcut, deep-test için güncelle)
```

LibVLCSharp zaten `Noctra.Core` referansı üzerinden geliyor.
`StreamAnalyzer.cs` namespace'ini `Noctra.Diagnostics` bırak
veya `Debugger` olarak değiştir (mevcut `TestParser` namespace'i).

---

## Program.cs'e Eklenecek Kod

`Program.cs` içindeki `Main()` fonksiyonuna şu bloğu ekle:

```csharp
// --deep-test desteği
if (args.Contains("--deep-test"))
{
    var deepConfig   = app.ParseDeepTestArgs(args);
    var channels     = ...; // TestResult.Channels listesi

    var deepResult   = await app.RunDeepTestAsync(channels, deepConfig);
    app.PrintDeepTestReport(deepResult);

    // HTML raporuna ekle
    var sb = new StringBuilder();
    app.AppendDeepTestHtml(sb, deepResult);
    File.AppendAllText("report.html", sb.ToString());
}
```

---

## Kullanım Örnekleri

```bash
# M3U + deep test (5 Live, 5 VOD, 3 Dizi, 15s probe)
dotnet run -- --type m3u --url "http://example.com/list.m3u" --deep-test

# Özelleştirilmiş örneklem
dotnet run -- --type xtream --host "http://..." --user u --pass p \
             --deep-test --live 3 --vod 3 --duration 20

# Sadece belirli gruptan test et
dotnet run -- --type m3u --url "..." --deep-test --group "ULUSAL"

# Tekrarlanabilir test (aynı kanallar her çalışmada)
dotnet run -- --type m3u --url "..." --deep-test --seed 42

# Toplu test + deep probe
dotnet run -- --batch providers.json --deep-test --live 2 --vod 2 --duration 10
```

---

## Ölçülen Metrikler

| Metrik | Kaynak | Açıklama |
|--------|--------|----------|
| **TTFF** | `Playing` event | Play() → İlk görüntü (ms) |
| **TTFC** | `Buffering(100)` event | Play() → Buffer doldu (ms) |
| **Donma sayısı** | `Buffering(<30)` event | Kaç kez dondu |
| **Toplam donma** | Buffer timer | Toplam donma süresi (ms) |
| **LostPictures** | `media.Stats` | VLC atlanan kare sayısı |
| **DemuxCorrupted** | `media.Stats` | Bozuk paket sayısı |
| **DemuxDiscontinuity** | `media.Stats` | HLS/TS stream kesintisi |
| **AvgBitrateKbps** | `media.Stats.DemuxBitrate` polling | Gerçek ortalama bitrate |
| **PeakBitrateKbps** | Polling max | Tepe bitrate |
| **BitrateStdDevRatio** | Hesaplama | Bitrate kararlılığı (0=stabil) |
| **TotalBytesRead** | `media.Stats.DemuxReadBytes` | Toplam indirilen veri |
| **HttpLatencyMs** | HTTP HEAD | VLC'den bağımsız ping |
| **Çözünürlük/FPS** | `player.Size()` + `player.Fps` | Video kalitesi |
| **VideoCodec/AudioCodec** | `Media.Tracks` | Codec bilgisi |

---

## Kalite Skoru Hesabı (0-100)

```
Başlangıç: 100

TTFF cezası:
  > 10s  → -25
  > 5s   → -15
  > 3s   → -8
  > 1.5s → -3

Donma:
  Her donma → -10
  > 5s toplam donma → -20

Frame drop:
  > %20 → -25
  > %10 → -15
  > %5  → -8
  > %1  → -3

DemuxCorrupted:
  > 100 → -15
  > 20  → -8
  > 5   → -3

Bitrate instabilitesi:
  StdDev/Mean > 0.5 → -10

HTTP gecikme:
  > 3s → -10 | < 200ms → +5
```

---

## Sınırlamalar

- **VLC tekli sıralı test**: LibVLC headless modda thread-safe değil.
  Paralel test için ayrı process spawn edilmesi gerekir.
- **Stalker stream URL'leri**: Token gerektirdiğinden doğrudan probe çalışmaz.
  Önce `/c/` handshake → token → actual stream URL alınmalı.
- **DRM'li kanallar**: VLC açamaz, TTFF timeout ile başarısız döner.
- **HLS Manifest**: İlk segment indirilene kadar TTFF yüksek görünebilir (normal).
