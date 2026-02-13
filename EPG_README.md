# 🎯 NOCTRA - Otomatik EPG Sistemi

## ✅ Kullanıcı HİÇ EPG DÜŞÜNMEMELİ!

### 🔧 Nasıl Çalışıyor?

```
1. KULLANICI
   └─> M3U URL gir
   └─> [Ekle]

2. UYGULAMA (Otomatik)
   ├─> M3U parse et
   ├─> Ülke tespit et (kanal adlarından)
   │   ├─> "TRT 1", "Kanal D" → Türkiye (TR)
   │   ├─> "BBC", "ITV" → UK (GB)
   │   └─> "CNN", "ESPN" → USA (US)
   │
   ├─> EPG kaynakları bul (öncelik sırasıyla)
   │   ├─> 1. Provider EPG (Xtream/Stalker)
   │   ├─> 2. M3U x-tvg-url
   │   ├─> 3. noctra-epg.org (güncel, küçük)
   │   ├─> 4. epgshare01.online (fazla kanal)
   │   └─> 5. Global fallback
   │
   └─> EPG'yi arka planda yükle
       └─> Kullanıcı beklemez!

3. SONUÇ
   └─> "✓ 1,234 kanal yüklendi"
       (30 saniye sonra: EPG hazır!)
```

---

## 📦 EPG Kaynakları

### ✅ noctra-epg.org (Birincil)
```
Format: https://noctra-epg.org/files/epg-{COUNTRY}.xml.gz

Örnekler:
- TR: https://noctra-epg.org/files/epg-tr.xml.gz (126 kanal)
- US: https://noctra-epg.org/files/epg-us.xml.gz (12,201 kanal)
- UK: https://noctra-epg.org/files/epg-gb.xml.gz (939 kanal)
- DE: https://noctra-epg.org/files/epg-de.xml.gz (433 kanal)

Avantajlar:
✓ Güncel (her saat)
✓ Küçük dosya boyutu
✓ Hızlı indirme
✓ Ücretsiz
```

### ✅ epgshare01.online (Alternatif)
```
Format: http://epgshare01.online/epgshare01/epg_ripper_{COUNTRY}{NUM}.xml.gz

Örnekler:
- TR: epg_ripper_TR1.xml.gz (46,864 kanal)
- TR: epg_ripper_TR3.xml.gz (43,873 kanal)
- US: epg_ripper_US2.xml.gz (155,603 kanal)
- Global: epg_ripper_ALL_SOURCES1.xml.gz (188 MB!)

Avantajlar:
✓ Daha fazla kanal
✓ Birden fazla kaynak (TR1, TR3)
✓ Spor kanalları (US_SPORTS1)
✓ Yerel kanallar (US_LOCALS1)
✓ Ücretsiz
```

---

## 🔧 Kurulum

### 1. Dependencies
```bash
dotnet add package Microsoft.EntityFrameworkCore
dotnet add package Microsoft.Extensions.Logging
dotnet add package System.IO.Compression
```

### 2. Services Kaydı (Program.cs veya Startup.cs)
```csharp
// Services
builder.Services.AddSingleton<LanguageDetectionService>();
builder.Services.AddSingleton<EpgSourceResolver>();
builder.Services.AddScoped<IEpgService, EpgService>();
builder.Services.AddScoped<IPlaylistService, PlaylistService>();

// HttpClient
builder.Services.AddHttpClient();
```

### 3. Database Migration
```bash
# Package Manager Console'da
Add-Migration AddEpgFields
Update-Database
```

---

## 🎯 Kullanım

### M3U Playlist Ekle (Otomatik EPG)
```csharp
var playlistService = serviceProvider.GetService<IPlaylistService>();

var playlist = await playlistService.AddFromUrlAsync(
    name: "My Playlist",
    url: "http://example.com/playlist.m3u",
    profileId: 1
);

// EPG otomatik arka planda yüklenecek!
// Kullanıcı beklemez.
```

### Xtream Codes Ekle (Otomatik EPG)
```csharp
var playlist = await playlistService.AddFromXtreamAsync(
    name: "Xtream Provider",
    serverUrl: "http://server.com:8080",
    username: "user123",
    password: "pass456",
    profileId: 1
);

// Provider'ın kendi EPG'si öncelikli kullanılır
```

### EPG Sorgusu
```csharp
var epgService = serviceProvider.GetService<IEpgService>();

// Şu anki program
var currentProgram = await epgService.GetCurrentProgramAsync(
    channelId: "TRT1.tr",
    channelName: "TRT 1 HD"  // Fuzzy matching için
);

if (currentProgram != null)
{
    Console.WriteLine($"Şu an: {currentProgram.Title}");
    Console.WriteLine($"Açıklama: {currentProgram.Description}");
    Console.WriteLine($"Süre: {currentProgram.StartTime:HH:mm} - {currentProgram.EndTime:HH:mm}");
}

// Gelecek programlar
var upcoming = await epgService.GetUpcomingProgramsAsync("TRT1.tr", count: 5);

// Bugünün programları
var today = await epgService.GetTodayProgramsAsync("TRT1.tr");
```

### Manuel EPG Yenileme
```csharp
await playlistService.RefreshEpgAsync(playlistId: 1);
```

---

## ⚙️ Timezone Yönetimi (ÖNEMLİ!)

### XMLTV Tarih Formatları
```xml
<!-- 1. UTC ile -->
<programme start="20240210173000 +0000" stop="20240210180000 +0000" channel="TRT1.tr">

<!-- 2. Timezone ile -->
<programme start="20240210203000 +0300" stop="20240210210000 +0300" channel="TRT1.tr">

<!-- 3. Timezone yok (local time varsayılır) -->
<programme start="20240210173000" stop="20240210180000" channel="TRT1.tr">
```

### Parse Stratejisi
```csharp
private DateTime ParseXmlTvDate(string? dateStr)
{
    // 1. Timezone var mı?
    if (dateStr.Contains(" +") || dateStr.Contains(" -"))
    {
        // "20240210173000 +0300" → UTC'ye çevir
        DateTimeOffset.TryParseExact(dateStr, "yyyyMMddHHmmss zzz", ..., out var dto);
        return dto.UtcDateTime;
    }
    else
    {
        // "20240210173000" → Local time varsay → UTC'ye çevir
        DateTime.TryParseExact(dateStr, "yyyyMMddHHmmss", ..., out var dt);
        return TimeZoneInfo.ConvertTimeToUtc(dt, _localTimeZone);
    }
}
```

**NEDEN ÖNEMLİ?**
- EPG Türkiye'den (+03:00) gelebilir
- Uygulama farklı timezone'da çalışabilir
- Tüm zamanlar **UTC'de saklanmalı**
- Gösterimde kullanıcı timezone'una çevrilmeli

---

## 📊 Fuzzy Matching (Akıllı Eşleştirme)

### Problem
```
Kanal: "TRT 1 HD FHD 1080p"
EPG ID: "TRT1.tr"
→ Exact match başarısız!
```

### Çözüm: Normalize + Similarity
```csharp
// 1. Normalize
NormalizeChannelName("TRT 1 HD FHD 1080p")
→ "trt1"

NormalizeChannelName("TRT1.tr")
→ "trt1"

// 2. Similarity Check
IsSimilar("trt1", "trt1")
→ true (Jaccard similarity: 100%)

// 3. Match bulundu!
```

### Supported Patterns
```
✓ Contains: "trt1" ⊆ "trt1hd" → MATCH
✓ Reverse: "trt1hd" ⊇ "trt1" → MATCH
✓ Jaccard: similarity > 0.7 → MATCH
```

---

## 🎨 UI Deneyimi

### Playlist Eklerken
```
┌─────────────────────────────────┐
│ M3U URL:                        │
│ [http://example.com/list.m3u]  │
│                                 │
│ [Ekle]                          │
└─────────────────────────────────┘

↓ (Hemen)

┌─────────────────────────────────┐
│ ✓ 1,234 kanal yüklendi          │
│                                 │
│ EPG arka planda hazırlanıyor... │
└─────────────────────────────────┘

↓ (30 saniye sonra - Toast)

┌─────────────────────────────────┐
│ ✓ EPG hazır                     │
│ 892 kanal • 45,678 program      │
└─────────────────────────────────┘
```

### Playlist Kartı
```
┌────────────────────────────────────────┐
│ My Playlist                            │
│ 1,234 kanal • Ülke: TR                 │
│                                        │
│ 📺 ✓ 892 kanal • 45,678 program       │
│     (2 saat önce)                      │
│                                        │
│ [🔄 EPG Yenile]  [⚙️]                 │
└────────────────────────────────────────┘
```

### Player Overlay
```
┌────────────────────────────────────────┐
│ TRT 1 HD                               │
│                                        │
│ Şu an: Akşam Haberleri (19:00-20:00)  │
│ "Günün önemli gelişmeleri..."         │
│                                        │
│ Sonraki: Spor Servisi (20:00-20:30)   │
└────────────────────────────────────────┘
```

---

## 📈 Performance

### EPG Yükleme Süreleri (Tahmini)
```
Türkiye (126 kanal):        ~5 saniye
UK (939 kanal):            ~15 saniye
US (12,201 kanal):         ~45 saniye
Global (ALL_SOURCES):      ~2 dakika (son çare)
```

### Optimization Stratejileri
```
✓ Batch insert (1000 kayıt/batch)
✓ Eski programlar atılır (-1 gün)
✓ Gelecek programlar limit (14 gün)
✓ GZip decompression (dosya boyutu %90 küçük)
✓ Async parsing (non-blocking)
✓ Database indexing (StartTime, EndTime, ChannelId)
```

---

## 🐛 Debugging

### EPG Durumu Kontrol
```csharp
var status = _epgService.Status;

Console.WriteLine($"State: {status.State}");
Console.WriteLine($"Message: {status.Message}");
Console.WriteLine($"Channels: {status.ChannelCount}");
Console.WriteLine($"Programs: {status.ProgramCount}");
Console.WriteLine($"Last Update: {status.LastUpdated}");
```

### Kanal Eşleştirme Test
```csharp
// UI'dan test (Advanced Settings > EPG > Channel Matching Test)
Test: "TRT 1 HD"

Sonuç:
✓ Exact Match: TRT1.tr
Şu an: Akşam Haberleri
```

---

## 🔐 Güvenlik

### Xtream Credentials
```csharp
// TODO: Encrypt passwords
public string? XtreamPassword { get; set; }

// Şifreleme örneği:
var encrypted = DataProtection.Protect(password, entropy);
playlist.XtreamPassword = Convert.ToBase64String(encrypted);
```

---

## 🚀 Roadmap

- [ ] EPG cache (disk'e kaydet, hızlı başlangıç)
- [ ] Incremental update (sadece yeni programlar)
- [ ] EPG diff (değişen programları tespit)
- [ ] Multi-source merge (farklı kaynaklardan en iyi veriyi seç)
- [ ] User contribution (yanlış eşleştirme düzeltme)
- [ ] EPG statistics (en çok izlenen, popüler)

---

## 📝 Lisans

MIT License - Use freely!

---

## 🙏 Teşekkürler

- **noctra-epg.org** - Ücretsiz EPG servisi
- **epgshare01.online** - Kapsamlı EPG kaynağı
- **Noctra Community** - Açık kaynak ruhu

---

Made with ❤️ by Noctra Team


