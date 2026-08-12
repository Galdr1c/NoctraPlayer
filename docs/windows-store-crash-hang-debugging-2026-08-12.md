# Windows Store Crash / Hang Debugging Rehberi

> Tarih: 2026-08-12 — Hedef: Partner Center Health raporundaki `Uncategorized` hang/crash
> kayıtlarını anlamlı stack trace'lere dönüştürmek ve masaüstü shutdown path'ini doğrulamak.

## 1. Durum: Partner Center'da ne görüyoruz?

- Health raporundaki tüm failure'lar **`1.0.0.0` package version** altında görünüyor.
  Güncel repodaki varsayılan sürüm `1.2.0.0`. **Önce Store'daki aktif sürümü doğrula:**
  Store'da hâlâ 1.0.0.0 varsa sorun güncel; 1.2.x varsa telemetry eski build'e ait.
- **%10 Hang rate**, yalnızca isteğe bağlı tanılama paylaşan günlük benzersiz aktif
  cihazlar üzerinden hesaplanır. 2 hang küçük örneklemde oranı ciddi şişirebilir;
  "tüm kullanıcıların %10'u donuyor" anlamına gelmez. Ama %0'a yakın olması gereken
  bir metrikte 2 gerçek failure görmezden gelinmemeli.
- Algeria 3 / Indonesia 1 dağılımı 4 event için anlamlı bir coğrafi/GPU paterni değildir.

## 2. Kodda yapılan fix'ler (shutdown hang şüphelileri)

Partner Center hang'lerinin masaüstünde en olası nedeni shutdown sırasında UI
thread'inin senkron DB beklemeleriydi. Aşağıdakiler düzeltildi:

| Yer | Eski | Yeni |
|---|---|---|
| `MainWindow.OnClosed` | `FlushWatchHistoryAsync().GetAwaiter().GetResult()` | 1.5s **bounded wait** + süre logu |
| `App.axaml.cs` `desktop.Exit` | `Task.Run(...).Wait()` (ClearHistoryOnExit, tüm profiller) | Kaldırıldı; temizlik startup warmup'a taşındı (Step 3.5) |
| `PlayerViewModel.Dispose` | `SaveAsync().GetAwaiter().GetResult()` (altyazı ayarı) | 1.5s bounded wait |
| Crash handler (masaüstü + mobil) | Terminating exception'da mailto/UI açma | Yalnızca `crash.log`'a yaz, WER'e bırak |
| `App.Initialize` | Senkron `db.Database.EnsureCreated()` | Async warmup'a taşındı (`EnsureCreatedAsync`) |
| `VideoOverlayViewModel.Dispose` | Player event'leri unsubscribe edilmiyordu | `PlayingChanged`/`PositionChanged` eklendi |

Not: `ClearHistoryOnExit` davranışı değişti — geçmiş artık kapanışta değil, bir sonraki
açılışta (pencere gösterilmeden önce) temizlenir. Kullanıcıya görünür etkisi yoktur
(geçmiş zaten hiç gösterilmeden silinir).

## 3. Release sembolleri → Partner Center

"Uncategorized" kaydının asıl nedeni stack trace olmaması. Release PDB'leri yüklenince
sonraki failure'lar anlamlı metod adlarına çevrilir (yansıması birkaç gün sürebilir).

1. Store paketini normal akışla üret:

   ```powershell
   .\build\package-store.ps1 -Editions Free,Premium -VersionPrefix 1.2.1
   ```

   Script artık her edition için `Noctra.Avalonia/bin/.../Release` çıktısından
   otomatik olarak `artifacts/store/Noctra.<Edition>_<version>_<Platform>_Symbols.zip`
   üretir (içinde `.pdb`, `.dll`, `.exe`).

   > ℹ️ Bu ZIP **Partner Center'a yüklenmek zorunda değil** (semboller
   > `.msixupload` içindeki `.appxsym` ile zaten gidiyor). Faydası: **lokal
   > dump analizi** — ProcDump ile alınan `.dmp` dosyalarını WinDbg/Visual
   > Studio'da açarken bu PDB'leri kullanırsın. Yedek olarak sakla.

2. Sembol yükleme: **ayrıca bir şey yapmana gerek yok — doğrulandı.**

   `.msixupload` dosyası (Store'a yüklenen format) **`.appxsym`** adlı sembol
   arşivini **içinde zaten taşır** (WAP build bunu otomatik üretir). Kontrol
   edildi: `Noctra.Free_1.2.0.0_x64.msixupload` ve Premium karşılığı içinde
   `Noctra.pdb` dahil 4 PDB bulunan `.appxsym` barındırıyor.

   Partner Center'a **paketi (`.msixupload`) normal şekilde yüklemen yeterli** —
   semboller otomatik alınır. Ayrı bir sembol yükleme adımı yok.

   > ℹ️ Eski dokümanlarda geçen **"Upload symbols" düğmesi** (Health > Failures
   > tablosunun üstü) **yeni Insights > Health arayüzünde kaldırılmıştır** —
   > bu yüzden bulunamıyor. O düğme, sembolleri paketten ayrı göndermek
   > isteyen eski akış içindi. Modern akışta semboller paketle gider.

   > ⚠️ **Önemli sınırlar:**
   > - Semboller yalnızca **eşleşen sürümdeki yeni failure'ları** çözer;
   >   geçmiş `1.0.0.0` kayıtları geriye dönük çözülmez.
   > - 1.2.0 yayınlandıktan sonra yeni failure'ların stack göstermesi birkaç
   >   gün sürebilir.
   > - Health verisi yalnızca **tanılama verisi paylaşmayı kabul eden
   >   cihazlardan** gelir.

3. Sonraki failure'larda şu eşleşmeleri arayın:

   - `MainWindow.OnClosed` / `PlayerViewModel.FlushWatchHistoryAsync` / `Microsoft.Data.Sqlite` → shutdown DB hang
   - `libvlc.dll` / `libavcodec` / `d3d11.dll` / `igd...` → VLC / decoder / GPU path
   - `Avalonia` / `Noctra.*ViewModel` → UI/event tarafı

> **Not:** Symbol zip adımı (`New-SymbolZip`) kasıtlı olarak **non-fatal**'dır —
> paket build'ini asla engellemez. Sorun olursa `artifacts/store/symbol-zip-debug.log`
> dosyasına `[trace]`/hata detayı yazılır ve uyarıyla devam edilir. Her build ~2 satır
> ekler (append-only).

### 3.1 PowerShell 5.1 encoding uyarısı (kritik)

Bu repo'daki `.ps1` script'leri (örn. `build/package-store.ps1`) **Windows PowerShell
5.1** ile çalıştırılır. PS 5.1, BOM'suz dosyayı **ANSI (cp1254)** okur — UTF-8
karakterler yanlış çözülür ve **tehlikeli baytlar string literal'lerdeki tırnakları
erken kapatabilir**:

- Em-dash `—` (UTF-8 `E2 80 94`): son bayt `0x94`, cp1254'te `"` (tırnak) — string'i
  erken kapatır, kod yapısını sessizce bozar, derleme hatası vermeden çalışma zamanında
  garip hatalar üretir (ör. `Compress-Archive -Path` → "Path null or empty").
- 🔐 gibi bazı emoji'ler de UTF-8'de `0x94` baytı içerir — aynı etki.

**Kural:** `.ps1` dosyalarında string literal'lere non-ASCII karakter yazmayın
(Türkçe metin/emoji/em-dash), veya dosyayı **UTF-8 BOM ile** kaydedin (PS 5.1 BOM'lu
dosyayı UTF-8 okur). Her iki script'e de BOM eklenmiştir — ama yeni içerik eklerken
bu kuralı hatırlayın; BOM kaybolursa (örn. başka bir editörde kaydederken) aynı
sorun geri gelir.

## 4. ProcDump ile lokal yakalama

ProcDump (Sysinternals) — `https://learn.microsoft.com/en-us/sysinternals/downloads/procdump`

Hang yakalama (`-h`: pencere Windows mesajlarına ≥5 sn cevap vermezse dump):

```powershell
procdump -accepteula -ma -h Noctra.exe C:\NoctraDumps
```

Crash yakalama (`-e`: unhandled exception):

```powershell
procdump -accepteula -ma -e Noctra.exe C:\NoctraDumps
```

**Shutdown test senaryosu:**

```text
video aç → 5-10 dakika izle → kanal/film değiştir → player'dan çık
→ tekrar video aç → uygulamayı X tuşuyla kapat
```

Kapanma takılırsa `-h` .dmp üretir; dump'ı WinDbg/Visual Studio ile açıp hang thread'inin
stack'ine bakın (beklenen aday: SQLite/DbContext kilidi).

## 5. Kapanma süresi doğrulaması (bounded wait kontrolü)

`OnClosed` flush'ının gerçekten hızlı döndüğünü (`~ms`, `~1500ms` değil) doğrulamak için
kapanış sırasında süre loglanır:

- Log dosyası: `Belgeler\Noctra\startup_debug.log`
- Normal durum: `Watch history flush on close took 5 ms.`
- Şüpheli durum: `Watch history flush on close took 1487 ms.` veya
  `Watch history flush exceeded the 1.5s shutdown budget...`

**Beklenti:** süre birkaç ms olmalı. Neden tam 1.5s yakılmaz: `OnClosed`'daki orijinal
`GetAwaiter().GetResult()` hiçbir zaman kilitlenmiyordu (telemetride 2 hang, %100 değil);
bu da flush continuation'ının UI thread'ine ihtiyaç duymadan tamamlandığını gösterir —
`Wait(1500)` yalnızca DB yazımının kendisi yavaşsa bekler ve 1.5s'te sınırlar.

Eğer testte süre sürekli ~1500ms görünürse: `MainWindow.OnClosed`'daki flush'ı
beklemesiz hale getirin (`_ = _playerViewModel.FlushWatchHistoryAsync(force: true)`),
çünkü geçmiş zaten playback sırasında 5s tick + stop'ta kaydediliyor; kapanıştaki flush
yalnızca son ≤5 saniyelik pozisyonu korur.

## 6. Crash log dosyaları

- **Masaüstü:** `Belgeler\Noctra\Logs\crash.log` — append-only, açılışta silinmez.
  (`startup_debug.log` her açılışta temizlenir; o yüzden crash log ayrı dosyadadır.)
- **Mobil:** `Logs\crash.log` (uygulamanın user-data dizini altında) — aynı konvansiyon.

Terminating exception artık UI/mailto **açmaz**; yalnızca bu dosyaya yazar ve süreci
WER/OS'a bırakır.

## 7. Yeni sürüm yayınladıktan sonra

Partner Center Health raporu **version filter** sunar: yeni sürümün (1.2.x) eski
sürüme (1.0.0.0) göre crash/hang oranını karşılaştır. Semboller yüklendikten sonra
yeni failure'lar için stack alınabiliyorsa VLC/GPU tarafına mı yoksa shutdown/UI
tarafına mı gidileceğine veriyle karar verin — "3 crash oldu" diye VLC hardware
acceleration'ı kapatmak doğru olmaz.

## 8. Hızlı kontrol listesi

- [ ] Store'da aktif sürüm doğrulandı (1.0.0.0 mı, 1.2.x mi?)
- [ ] Yeni Store build üretildi ve `*_Symbols.zip` Partner Center'a yüklendi
- [ ] ProcDump `-h` + `-e` ile lokal dump capture hazır
- [ ] Kapanma testi yapıldı; `startup_debug.log`'da flush süresi ms düzeyinde
- [ ] `crash.log` dosyalarının konumu doğrulandı (`Belgeler\Noctra\Logs\crash.log`)
