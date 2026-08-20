# NoctraPlayer — Kapsamlı Performans, Donma ve Uzun Kullanım Stabilitesi Raporu

> **Repo:** `Galdr1c/NoctraPlayer`  
> **İnceleme kapsamı:** Bu konuşmadaki ilk performans raporundan en son frame/allocation seviyesindeki incelemeye kadar bulunan tüm sorunların konsolide hali.  
> **Tarih:** 10 Ağustos 2026  
> **Odak:** Android/Mobile — Live, Movies, Series, Search, navigation, background/foreground, virtualization, image pipeline, TMDB, EPG, SQLite, dispatcher, player ve allocation davranışı.

---

## 1. Yönetici özeti

NoctraPlayer'daki kasma/donma problemi tek bir hatadan kaynaklanmıyor. Kodun birçok yerinde ayrı ayrı mantıklı optimizasyonlar mevcut olsa da, bu optimizasyonlar **uygulama genelinde ortak bir workload/lifecycle yönetimi altında birleşmiyor**. Sonuçta aşağıdaki sistemler aynı anda birbirini büyütebiliyor:

- aynı anda visual tree'de tutulan ağır sayfalar,
- resume sırasında birden fazla grid'in layout recovery çalıştırması,
- navigation sırasında tekrar tekrar koleksiyon reset edilmesi,
- hızlı scroll ile gerçek anlamda iptal edilmeyen görsel indirme/decode işleri,
- Search tarafında büyük veri kümelerinin tekrar tekrar fuzzy taranması,
- scroll ile biriken TMDB enrichment işleri,
- aynı SQLite dosyasına eşzamanlı read/write baskısı,
- EPG'nin büyük toplu güncellemeleri,
- gereksiz binding/property-change fan-out,
- UI thread'e Render priority veya senkron `Invoke` ile gönderilen çok sayıda iş,
- bitmap/native memory pressure,
- background/foreground sonrasında eski işlerin yeni UI workload'uyla çakışması.

En kritik ortak failure chain şu şekildedir:

```text
Navigation / Scroll
        ↓
retained page + collection reset
        ↓
paging / image / TMDB / EPG işleri
        ↓
işlerin bir kısmı gerçek anlamda iptal edilmiyor
        ↓
DB + network + decode + dispatcher backlog
        ↓
background / foreground
        ↓
resume layout recovery
        +
eski callback'lerin tamamlanması
        +
bitmap/native memory pressure
        +
binding invalidation
        ↓
frame budget aşımı
        ↓
jank
        ↓
daha fazla backlog
        ↓
uzun freeze / kilitlenme / ANR benzeri his
```

---

# 2. Öncelik tanımları

| Seviye | Anlamı |
|---|---|
| **P0 — Kritik** | Doğrudan uzun kullanım donması, backlog, layout storm veya sürekli büyüyen workload üretebilen sorun. İlk patch grubunda çözülmeli. |
| **P1 — Yüksek** | P0 sorunlarını ciddi şekilde büyüten, belirgin frame drop / memory / DB / render baskısı üreten sorun. |
| **P2 — Orta** | Tek başına büyük donma üretmesi daha az olası; ancak yoğun kullanımda hissedilir ve ana sorunlarla birleştiğinde önem kazanır. |
| **P3 — Düşük / Sağlamlaştırma** | Mikro-allocation, mimari guardrail veya ileride sorun çıkarabilecek düşük öncelikli konu. |

---

# 3. P0 — KRİTİK SORUNLAR

## P0-01 — Ağır sayfaların tamamı aynı anda oluşturuluyor ve visual tree'de tutuluyor

**Etkilenen alan:** `MainView.axaml`, `MainView.axaml.cs`

Home, Live, Movies, Series, Search, Favorites, MyList, History, Downloads, Settings ve diğer büyük ekranlar tek host içerisinde oluşturuluyor; navigation sırasında gerçek unload/detach yerine ağırlıklı olarak `IsVisible` değiştirilerek ekran seçiliyor.

### Neden tehlikeli?

`IsVisible=false`:

- view instance'ını yok etmez,
- binding ağını otomatik olarak sökmez,
- child kontrolleri ve grid'leri tamamen dispose etmez,
- event subscription'ların ve model referanslarının yaşamını sonlandırmaz,
- Image.Source / kart / collection referanslarının yaşam süresini uzatabilir.

Bu nedenle kullanıcı:

```text
Home → Live → Movies → Series → Search
```

gezdiğinde geride ciddi bir retained object graph kalabilir.

### Belirtiyle bağlantısı

- menüler arası geçiş sonrası giderek artan kasma,
- uzun kullanımda memory baskısı,
- resume sırasında görünmeyen sayfaların da iş yapması,
- gizli Search/Live/Movies/Series yüzeylerinin collection değişikliklerine tepki vermesi.

### Önerilen çözüm

- Tek bir `ContentControl` / aktif page host kullan.
- Ağır view'ları yalnız gerektiğinde oluştur.
- ViewModel state korunabilir; view instance'ı korunmak zorunda değil.
- İstenirse yalnız son kullanılan 1 ekran için küçük bir view cache uygulanabilir.
- Alternatif geçiş çözümü olarak her ağır view'a gerçek bir `IsActive` lifecycle ekle ve inactive olduğunda subscription/image/enrichment/refresh işlerini askıya al.

---

## P0-02 — Inactive view'lar gerçekten “pasif” hale gelmiyor

**Etkilenen alan:** Live/Movies/Series/Search view'ları, custom grid/feed kontrolleri

P0-01'in ayrı bir sonucu olarak, görünmeyen view'lar aynı ViewModel ve collection'lara bağlı kalabiliyor. Bu yalnız bellek değil, **arka planda iş üretimi** problemidir.

### Risk

Inactive bir Search veya grid:

- collection değişikliklerini görebilir,
- row rebuild tetikleyebilir,
- lifecycle event alabilir,
- image binding'lerini tutabilir,
- metadata/property change dalgalarını işleyebilir.

### Çözüm

Aktiflik visual visibility'den bağımsız modellenmeli:

```text
ViewAttached
ViewVisible
ViewActive
AppForeground
```

durumları ayrılmalı.

Yalnız `ViewActive && AppForeground` iken pahalı UI işlemleri yapılmalı.

---

## P0-03 — Resume sinyali aktif ekranla sınırlandırılmadan global yayılıyor

**Etkilenen alan:** `MainActivity`, `MobileAppLifecycle`, `MobileVirtualizingCardGrid`

Android foreground'a döndüğünde global resume sinyali yayınlanıyor. Retained visual tree sebebiyle görünmeyen grid'ler de bu sinyali alabiliyor.

### Sonuç

Tek bir resume:

```text
NotifyResumed()
    ↓
Live grid
Movies grid
Series grid
Search içindeki grid/feed'ler
...
```

şeklinde fan-out oluşturabilir.

### Çözüm

- Resume recovery yalnız aktif page'e yönlendirilmeli.
- Global broadcast yerine active-surface aware lifecycle kullanılmalı.
- Gizli grid'lerde recovery yapılmamalı.

---

## P0-04 — `MobileVirtualizingCardGrid.RefreshAfterResume()` fazla agresif retry yapıyor

**Etkilenen alan:** `MobileVirtualizingCardGrid`

Resume recovery yaklaşık:

- 8 denemeye kadar,
- ~50 ms aralıklarla,
- `DispatcherPriority.Render` seviyesinde

çalışabiliyor.

### Sorun

Gizli bir view'da `Bounds.Width` 0 veya stabil olmayan değerde kalırsa grid stabil kabul edilmeyip tekrar tekrar recovery deneyebilir.

### Etki

Birden fazla retained grid ile:

```text
Grid A → 8
Grid B → 8
Grid C → 8
...
```

Render-priority callback üretilebilir.

### Çözüm

- `!IsEffectivelyVisible` ise recovery'yi başlatma.
- Retry yerine page activation / size-changed tabanlı recovery kullan.
- Zorunlu retry varsa 2–3 ile sınırla.
- Recovery generation/version ile korunmalı.

---

## P0-05 — `InvalidateLayoutChain()` parent zincirini gereksiz yere yeniden ölçüyor

**Etkilenen alan:** `MobileVirtualizingCardGrid`

Grid recovery sırasında yalnız grid değil, parent zinciri boyunca:

- `InvalidateMeasure()`
- `InvalidateArrange()`

çağrıları yapılabiliyor.

### Neden kritik?

UI thread zaten scroll sırasında:

- input,
- measure,
- arrange,
- composition,
- recycling,
- image updates

yaparken root'a kadar layout invalidation büyük bir layout storm oluşturabilir.

### Çözüm

- İlk olarak yalnız grid/presenter invalidate edilmeli.
- Root'a kadar zincir invalidation kaldırılmalı.
- Layout gerçekten bozuksa scoped recovery uygulanmalı.

---

## P0-06 — Navigation sırasında aynı collection'lar birden fazla kez reset ediliyor

**Etkilenen alan:** `MainViewModel`, `PrepareContentSurfaceForNavigation`, `ResetIncrementalState`, `ApplyFiltersAsync`

Navigation akışında aynı koleksiyon:

1. navigation hazırlığında reset,
2. yeni instance ataması,
3. immediate filter scheduling,
4. filter içinde tekrar reset

görebiliyor.

### Örnek etki

```text
Navigation
  ↓
Reset #1
  ↓
FilteredChannels instance değişir
  ↓
ScheduleImmediateFilter
  ↓
ApplyFilters
  ↓
Reset #2
```

### Neden kritik?

Virtualized listede incremental `AddRange` ucuzken `ItemsSource` arkasındaki collection instance'ını sürekli değiştirmek:

- presenter'ları,
- recycled container'ları,
- binding ağını,
- row collection'larını

yeniden değerlendirmeye zorlar.

### Çözüm

- Navigation yalnız state/generation değiştirmeli.
- Collection mümkün olduğunca instance olarak korunmalı.
- `Clear + incremental load` veya identity-aware diff kullanılmalı.
- `ResetIncrementalState` ve `ApplyFiltersAsync` ownership'i netleştirilmeli.

### Uygulama durumu — 2026-08-11: Tamamlandı

- `Channels`, `FilteredChannels` ve `SeriesViewItems` collection identity'si navigation ve
  pagination boyunca korunuyor; hedef yüzey navigation başlangıcında yalnız bir kez yerinde
  temizleniyor.
- Navigation reset ownership'i view + generation + filter request ile eşleştirildi. İptal
  edilen veya supersede edilen istekler yeni owner'ı tamamlayamıyor; eşleşen prepared owner
  varken duplicate-filter suppression uygulanmıyor.
- Deferred Series refresh, kısa Search iptali, eşzamanlı filter scheduling ve stale completion
  yollarındaki reset/loading sızıntıları kapatıldı. İlk sayfa tek batch `Add`, sonraki sayfa
  ilave `Reset` olmadan append ediliyor.
- Otomatik kanıt: odaklı fixture `9/9`; 10 tekrar `90/90`; ilgili navigation/cancellation/
  pagination regresyon grubu `85/85`. Tam takımda `1983` testin `1976` tanesi geçti; önceki
  bağımsız 6 hata değişmedi, ayrıca tek seferlik bağımsız DownloadContentKey hatası izole
  tekrarında geçti.
- Build kanıtı: Core `0 warning / 0 error`, Mobile `1 mevcut warning / 0 error`, Android
  `72 mevcut warning / 0 error`. İmzalı APK SHA-256:
  `4AA52F116426D9D67451468C1F7AAE9CF6697F108F7D5CA718A543AF21D2309E`.
- Android kabulü: veri silmeden `adb install -r`; `firstInstallTime` değişmedi
  (`2026-08-10 17:51:36`). 50 tam `Live -> Movies -> Series -> Live` turu, 10 background/
  resume turu ve 15 liste kaydırması tamamlandı. PID `13550` sabit kaldı; ANR/crash/OOM
  eşleşmesi `0`; son ekran doğru `Canlı TV` içeriğini gösterdi.
- Bellek gözlemi: başlangıç PSS `585161 KB`, ilk 50 tur sonrası `608401 KB`, yaşam döngüsü ve
  kaydırma sonrası `597210 KB`. Test süresince monoton büyüme veya kilitlenme görülmedi.

---

## P0-07 — `RemoteImage.CancelPendingLoad()` gerçek HTTP/decode işini iptal etmiyor

**Etkilenen alan:** `MobileRemoteImage`

Bu en kritik image pipeline hatasıdır.

Kart offscreen olduğunda `_loadCts.Cancel()` çağrılıyor ancak token yalnız:

```text
loadTask.WaitAsync(cancellationToken)
```

bekleyişini iptal ediyor.

Underlying:

- `DownloadBitmapAsync`
- `DownloadGate.WaitAsync`
- HTTP `SendAsync`
- stream copy
- decode admission

gerçek card-lifecycle cancellation token'ını almıyor.

### Sonuç

```text
Poster A görünür → request başladı
scroll → A offscreen
waiter cancel
AMA request yaşamaya devam eder
```

Hızlı scroll ile yüzlerce eski artwork işi arkada tamamlanabilir.

### Belirtiyle bağlantısı

Bu, doğrudan:

> “ilk başta seri, çok kaydırınca giderek ağırlaşıyor”

davranışı üretir.

### Çözüm

Gerçek cancellation zinciri:

```text
consumer token
→ request coordinator
→ queue wait
→ SendAsync
→ CopyToAsync
→ decode gate
→ decode
```

boyunca taşınmalı.

---

## P0-08 — Image pipeline'da farklı URL'ler için sınırlı concurrency var ama sınırlı backlog yok

**Etkilenen alan:** `MobileRemoteImage`, `InFlightLoads`, download/decode semaphore'ları

`DownloadGate=6` ve decode gate gibi concurrency limitleri aynı anda çalışan işi sınırlar; ancak kaç farklı URL'nin kuyrukta bekleyebileceğini yeterince sınırlamaz.

### Sorun

```text
6 running
+ yüzlerce bekleyen distinct URL
```

oluşabilir.

Concurrency limiter backlog limiter değildir.

### Çözüm

- Bounded priority queue.
- Viewport-visible işler high priority.
- Offscreen işler queue'dan çıkarılmalı.
- Son consumer ayrılırsa underlying request iptal edilmeli.
- Pending iş sayısı telemetry ile izlenmeli.

---

## P0-09 — Search fuzzy engine aynı güncellemede veri kümesini birden fazla kez tamamen tarıyor

**Etkilenen alan:** `MainViewModel.UpdateSearchBuckets`, search scoring helpers

Search yalnız `Contains` yapmıyor. Aynı update sırasında ayrı ayrı:

- Live results,
- VOD results,
- Series results,
- Similar Live,
- Similar VOD,
- Similar Series,
- suggestion

hesapları için aynı büyük veri kümeleri yeniden taranabiliyor.

Scoring sırasında:

- normalization,
- `Regex.Replace`,
- token karşılaştırmaları,
- Damerau/Levenshtein

çalışıyor.

### Büyük playlist etkisi

10.000–50.000 item'da bu, doğrudan CPU hotspot olabilir.

### Çözüm

- Normalize edilmiş arama alanlarını önceden cachele.
- Tek pass üzerinden aday üret.
- Fuzzy scoring'i dar aday setinde çalıştır.
- Top-K için tüm listeyi sort etmek yerine bounded heap/seçim algoritması kullan.
- Background worker + cancellation + generation commit uygulanmalı.

---

## P0-10 — Search paging geldikçe tüm fuzzy search tekrar hesaplanıyor

**Etkilenen alan:** `LoadMoreChannelsAsync`, Search pipeline

Yeni page eklendikten sonra Search aktifse `UpdateSearchBuckets()` tekrar çalıştırılıyor.

### Sonuç

```text
Page 1 → full search
Page 2 → full search
Page 3 → full search
Page 4 → full search
```

Yeni page yalnız kendi item'larını değerlendirmek yerine önceki dataset de tekrar taranıyor.

### Çözüm

- Incremental search ranking.
- Sadece yeni page'i score et.
- Mevcut Top-K ile merge et.
- Query değişmediyse eski skorları tekrar hesaplama.

### P0-09 / P0-10 uygulama durumu — 2026-08-13: Tamamlandı

- Query-scope sahibi `IncrementalSearchRankingSession` eklendi. Kanal, film ve dizi alanları
  bir kez normalize ediliyor; aynı query ve playlist içinde önceki sayfalar yeniden score
  edilmiyor. Ana sonuçlar `96`, benzer sonuçlar `18` öğelik bounded bucket'larda tutuluyor;
  tüm aday listesini tekrar tekrar sıralayan eski yaklaşım kaldırıldı.
- Fuzzy eşleme ArrayPool tabanlı Damerau-Levenshtein ile allocation kontrollü hale getirildi.
  Item, episode, fuzzy loop ve bounded merge boyunca gerçek `CancellationToken` taşınıyor.
  Hazırlama/merge işlemleri scratch bucket üzerinde yapılıp yalnız başarılı terminal durumda
  atomik olarak yayınlanıyor; iptal edilen page numarası ilerlemiyor ve tekrar denenebiliyor.
- Query, navigation/view generation, playlist/profile ve Series dataset version commit öncesi
  yeniden doğrulanıyor. Aynı owner içindeki işlem ve UI commit sıralandı; eski snapshot'ın yeni
  sonucu ezmesi, stale Series datasının yayınlanması ve stale-signature/owner yarışları kapatıldı.
- Sıralama davranışı eski uygulamayla eş tutuldu: desteklenen image URL doğrulaması korunuyor,
  Series için yalnız `CoverUrl` kullanılıyor; sonradan gelen kanal/dizi görsel enrichment'i
  mevcut bounded bucket'ların image tie-break sırasını yeniden değerlendiriyor.
- Telemetri eklendi: `search.rank.sessions.started.count`,
  `search.rank.sessions.cancelled.count`, `search.rank.items.evaluated.count`,
  `search.rank.items.reused.count`, `search.rank.stale_commit_rejected.count` ve
  `search.rank.commit.count`.
- Otomatik doğrulama: geniş Search/navigation/content-query paketi `88/88`; yarış koşulu odaklı
  paket 10 tekrarda `440/440`. Tam paket `2022/2042`; kalan 20 hata Search değişiklik alanı
  dışındaki mevcut lisans/promosyon, download ve mobil seçim testlerindeydi. P0-09/P0-10 testi
  başarısız olmadı. Bağımsız son kod incelemesinde Critical/Important bulgu kalmadı.
- Android arm64 Debug kabul APK'sı: `studio.kynora.noctra-Signed.apk`, `180974023` byte,
  SHA-256 `798E4C319EAA8FA287125912744A005E77C21D1B01A92576091D48BFD575C3AF`.
  Paket DBY_W09 cihazına veri silmeden kuruldu; `firstInstallTime`
  `2026-08-10 17:51:36` olarak değişmeden kaldı.
- Son APK canlı kabulü: 15 hızlı query değişimi, 40 çift yönlü Search kaydırması, 10
  background/resume turu ve Live/Movies/Series/More/Search arasında 52 geçiş tamamlandı. PID
  `29189` sabit kaldı; ANR/crash/OOM eşleşmesi `0`; son PSS `609546 KB`, RSS `677292 KB`.
- Son APK canlı telemetrisi: ilk geniş query `9851` öğe değerlendirdi; aynı query'nin sonraki
  sayfaları `30, 30, 30, 30, 30, 30, 21, 2` yeni öğe değerlendirdi. Query replacement sonrası
  session sayısı `2`, expected cancellation `1`, stale commit `0` oldu. Ham kanıt:
  `artifacts/p0-09-p0-10-live/p0_09_p0_10_final.jsonl` (SHA-256
  `01E8A754ED678F8A920F57FB4E61BEAE03D79DD2E8C386AF6BA4118A7680040E`). Android `gfxinfo`
  Avalonia/Skia yüzeyinde frame yakalamadığı (`Total frames rendered: 0`) için yanıltıcı bir
  jank yüzdesi raporlanmadı.

---

## P0-11 — TMDB enrichment işleri scroll ile birikiyor ve navigation lifetime'a bağlı değil

**Etkilenen alan:** Movies/Series visual enrichment, `MainViewModel`

Her paging page'i metadata/artwork enrichment başlatabiliyor. İşler `fire-and-forget` yapısında ve navigation generation'a sıkı bağlı değil.

### Failure chain

```text
Movies page 1 → enrichment
page 2 → enrichment
page 3 → enrichment
Series'e geç
→ eski Movies işleri devam
Search'e geç
→ eski callback'ler hâlâ DB/UI'ya gelir
```

### Çözüm

- Merkezi bounded enrichment queue.
- `NavigationGeneration` / `ViewGeneration`.
- Page inactive olduğunda queued işler cancel/drop.
- Sonuç commit öncesi generation kontrolü.

---

## P0-12 — TMDB `MAX_CONCURRENT=3` global değil; her batch kendi semaphore'unu oluşturuyor

**Etkilenen alan:** `TmdbSyncService.EnrichSeriesBatchAsync`

`SemaphoreSlim(3)` her batch çağrısında yeniden oluşturuluyorsa gerçek limit uygulama genelinde 3 değildir.

### Örnek

```text
Batch 1 → 3
Batch 2 → 3
Batch 3 → 3
Batch 4 → 3
```

Teorik eşzamanlılık 12'ye çıkabilir.

### Çözüm

- Semaphore servis seviyesinde singleton/global olmalı,
- veya daha iyisi tek bounded metadata worker queue kullanılmalı.

### Uygulama durumu — 2026-08-11: Tamamlandı

- `ITmdbEnrichmentScheduler` ve singleton `TmdbEnrichmentScheduler` eklendi. Uygulama genelinde
  tek kuyruk kullanılıyor; varsayılan aktif iş sınırı `3`, bekleyen iş kapasitesi `64` ve taşma
  politikası `drop-oldest`.
- Movies/Series/Search görünür içerik zenginleştirmesi ile `TmdbSyncService` aynı scheduler'a
  bağlandı. Eski batch-local semaphore ve sayfa başına `Task.Run` fan-out kaldırıldı.
- İş sahipliği immutable view generation + aktif view + playlist + cancellation token ile
  sınırlandı. Navigation, playlist/profile değişimi ve uygulama kapanışı eski kapsamı iptal
  ediyor; HTTP, EF Core, kayıt ve UI commit öncesinde kapsam yeniden doğrulanıyor.
- Caller cancellation artık `MetadataService` içindeki fallback, detail, genre-cache ve search
  yollarında genel TMDB hatası olarak yutulmuyor. Scheduler admission/dispose, dequeue/cancel ve
  aynı-key replacement yarışları kilit dışında terminal completion/registration cleanup ile
  kapatıldı.
- Otomatik doğrulama: odaklı scheduler/TMDB/scope/cancellation paketi `19/19`; 10 tekrarda
  `190/190`; navigation/search/profile/DI regresyon paketi `125/125`. Tam paket `1995/2002`;
  kalan 7 hata TMDB değişiklik alanı dışındaki önceden bilinen download, promo-code, mobile
  selection ve store-timer testlerindeydi. TMDB/zamanlayıcı testi başarısız olmadı.
- Derleme: `Noctra.Core` ve `Noctra.Mobile` `0` hata / `0` uyarı; Android `0` hata / mevcut
  `72` uyarı. Bağımsız son kod incelemesi Critical/Important bulgu olmadığını ve değişikliğin
  birleştirmeye hazır olduğunu doğruladı.
- APK: `studio.kynora.noctra-Signed.apk`, `353711198` byte, SHA-256
  `415D8BE76E3AA7C59B23367F3CAFAB7C8963AD568046B98115AD5582E27A86D3`.
- Android kabulü: DBY_W09 cihazına veri silmeden `adb install -r`; `firstInstallTime`
  `2026-08-10 17:51:36` olarak değişmeden kaldı. 50 Movies/Series/Search/Live geçişi ve yoğun
  çift yönlü kaydırma, 10 background/resume turu ve gerçek M3U kuyruk yükü altında 30 ek geçiş
  tamamlandı. PID `18937` sabit kaldı; ANR/crash/OOM eşleşmesi `0`.
- Canlı M3U telemetrisi: `35` schedule, `23` start, `11` complete, navigation kaynaklı `24`
  expected cancellation; `tmdb.queue.active.high_water=3` ve
  `tmdb.queue.pending.high_water=6`. Böylece `active <= 3` ve `pending <= 64` kabul sınırları
  gerçek cihazda doğrulandı.

---

# 4. P1 — YÜKSEK ÖNCELİKLİ SORUNLAR

## P1-01 — Başlamış Search fuzzy hesaplaması cooperative cancellation desteklemiyor

Debounce eski request'i iptal edebiliyor ancak ağır ranking başladıktan sonra iç döngüler düzenli `CancellationToken` kontrolü yapmıyor.

### Çözüm

- Scoring loop'larına token taşı.
- Belirli item/token aralıklarında cancellation check.
- Sonuç commit öncesi query generation doğrula.

### Uygulama durumu — 2026-08-13: Tamamlandı (P0-09 / P0-10 ile birlikte)

`CancellationToken` item, episode, fuzzy distance ve bounded merge döngülerinin tamamına
taşındı. İptal edilen batch scratch state'i yayınlamıyor; page ve query owner tekrar denenebilir
kalıyor. Query/navigation/view/playlist/dataset generation kontrolleri UI commit öncesi yeniden
yapılıyor. Ayrıntılı test ve canlı cihaz kanıtı P0-09/P0-10 uygulama durumu bölümündedir.

---

## P1-02 — Search tek hesaplamada altı ayrı sonuç koleksiyonunu `Reset` edebiliyor

**Etkilenen alan:** `SetItems`, `BatchObservableCollection.ReplaceAll`

Normal/Similar Live-Series-VOD koleksiyonları `ReplaceAll` ile `Reset` event üretebiliyor.

### Etki

- Virtualized row/container rebuild,
- binding invalidation,
- Search section rebuild,
- allocation churn.

### Çözüm

Downloads tarafındaki `Set...IfChanged` yaklaşımına benzer:

- identity-aware diff,
- aynı sıra/ID ise hiç dokunmama,
- incremental insert/remove.

### P1-02 uygulama durumu — 2026-08-13: Tamamlandı

Altı Search sonuç koleksiyonunun normal commit, kısa sorgu ve profil temizleme yolları
`ReplaceAll/Clear` yerine `(PlaylistId, Id)` kimliğiyle çalışan artımlı senkronizasyona geçirildi.
Aynı kimlik ve aynı nesne sırası hiçbir collection olayı üretmiyor; ekleme, taşıma,
nesne yenileme ve aralık silme yalnız değişen indeksleri bildiriyor. Böylece mevcut kart
nesneleri ve sanallaştırılmış container'lar korunuyor.

Mobil ve masaüstü section feed'leri de `Move/Replace/Remove` olaylarında tüm satır akışını
yeniden kurmak yerine yalnız etkilenen bölümün satırlarını senkronize ediyor. Aynı diff'in
art arda ürettiği collection olayları bölüm başına tek UI işi olarak birleştiriliyor; ilgisiz
bölümlerin header ve row referansları değişmiyor. Geometri/section yapısı uyumsuzsa mevcut
tam-rebuild yolu güvenli fallback olarak korunuyor.

Kabul kanıtı:

- P1-02 odak paketi: **66/66 başarılı**.
- Nihai kararlılık tekrarı: **10/10 tur, toplam 660/660 başarılı**.
- Gerçek `MainViewModel` ikinci Search sayfası testi mevcut kart referansını korudu,
  Series koleksiyonuna gereksiz olay göndermedi ve Search koleksiyonlarında `Reset`
  üretmedi.
- Kimlik eşitliği, no-op, append, middle insert, move, replacement, range remove,
  section-local row koruma, boş↔dolu durumda izleyen grup header'ını hedefli yenileme
  ve contract testleri başarılı.
- `Noctra.Mobile` gerçek `net10.0` hedefiyle derlendi: **0 hata**; görülen 21 uyarı
  mevcut nullable/deprecated kod uyarılarıdır ve P1-02 kaynaklı değildir.
- Bağımsız production re-review sonrası Critical/Important bulgu kalmadı; reviewer
  odak paketi **35/35** geçti ve değişiklik merge-ready bulundu.

Android canlı kabul:

- Nihai arm64 APK **0 hata** ile üretildi ve `adb install --user 0 -r -d` ile eski
  uygulama verisi silinmeden kuruldu; `firstInstallTime` değişmedi ve profiller korundu.
- Gerçek `bein` ve geniş `be` sorgularında sonuç kartları yüklendi. Similar-Series-only
  geçişinde `Benzer Sonuçlar` başlığı tam **1** kez göründü; normal sonuçlara dönüşte
  stale/tekrarlı grup başlığı kalmadı.
- Search sonuçlarında **30** kontrollü kaydırma ve ardından **8/8** Home/launcher
  arka plan–ön plan turu aynı Android PID (`6679`) ile tamamlandı.
- PSS: başlangıç `381469 KB`, yüklü sonuçlarda `619541 KB`, kaydırma sonrası
  `586708 KB`, lifecycle sonrası `547175 KB`; monoton büyüme görülmedi.
- Temiz logcat penceresinde ANR/crash/OOM/fatal-process eşleşmesi **0**; yeni crash/ANR
  exit-info kaydı oluşmadı.
- Tam `Noctra.Tests` koşusu: **2103/2112 başarılı**. Kalan 9 hata download,
  promo-code, mevcut mobil selection contract'ı ve tek metadata timeout testindeydi;
  P1-02 odak/contract testi başarısız olmadı.
- APK SHA-256:
  `020E17385110538CBBE0945BB2A0EC9ABBC7E7C39ED08EE34588FA864AB6EEED`.
- Ayrıntılı kanıt: `artifacts/p1-02-live-20260813/acceptance-summary.md`.

Tasarım ve uygulama planı:
`docs/superpowers/specs/2026-08-13-search-collection-diff-design.md` ve
`docs/superpowers/plans/2026-08-13-search-collection-diff-plan.md`.

---

## P1-03 — Gizli Search ekranı collection değişikliklerini işlemeye devam edebiliyor

P0-01/02'nin Search'a özel maliyeti:

- section feed rebuild,
- collection subscriptions,
- search result changes,
- TMDB visual enrichment

görünmeyen ekranda bile iş üretebilir.

### Çözüm

Inactive Search:

- subscription'ları suspend,
- dirty flag koy,
- tekrar açıldığında tek refresh yap.

---

## P1-04 — Tek VOD enrichment network → DB read/write → UI dispatcher zinciri oluşturuyor

Poster/metadata eksik bir VOD için:

1. metadata fetch,
2. DbContext,
3. DB lookup,
4. metadata değişiklikleri,
5. `SaveChanges`,
6. UI dispatcher,
7. model property updates

oluşabiliyor.

30 item × birçok page ile çok büyük iş hacmi oluşur.

### Çözüm

- Batch DB write.
- Metadata result cache.
- Tek context/batch işlem.
- UI commit'leri batch/coalesced.

---

## P1-05 — Series için iki farklı enrichment pipeline aynı page'de çalışabiliyor

Series page sonrası hem:

- batch metadata enrichment,
- visible visual enrichment

başlatılabiliyor.

Dedup mekanizmaları birbirinden bağımsız.

### Çözüm

Tek `SeriesMetadataCoordinator` altında:

- title/poster/details ihtiyaçları birleştirilmeli,
- tek key,
- tek queue,
- tek cancellation policy.

---

## P1-06 — VOD için başarısız poster/metadata aramasında negative-cache yok

Series tarafında poster bulunamayan item'lar tekrar sorgulanmamak üzere tutulurken VOD tarafında başarısız istek sonrası pending key siliniyor.

### Sonuç

Aynı VOD:

- Movies,
- Search,
- Similar Search,
- tekrar realize

olduğunda tekrar tekrar metadata araması yapabilir.

### Çözüm

TTL'li negative cache:

```text
NoPoster / MetadataNotFound
TTL = ör. 6–24 saat
```

---

## P1-07 — Offscreen/cancelled görsel işleri tamamlandığında cache'i kirletebiliyor

Waiter iptal olsa bile underlying download tamamlanıp bitmap cache'e eklenebilir.

### Sonuç

- Kullanıcının artık görmediği posterler cache'e dolar.
- Yararlı visible bitmap'ler LRU'dan erken çıkar.
- Yukarı dönüldüğünde tekrar download/decode gerekir.

### Çözüm

- Son consumer yoksa sonucu cache'e commit etme,
- veya cache commit policy'sini request priority/visibility ile bağla.

---

## P1-08 — Live kartındaki 36×36 logo varsayılan ~384 px decode ediliyor

**Etkilenen alan:** `MobileLiveTvCard.axaml`, `MobileRemoteImage`

`DecodePixelWidth` verilmediği için küçük Live logo bile global default decode width kullanıyor.

### Yaklaşık maliyet

```text
36 × 36   ≈ 1.296 piksel
384 × 384 ≈ 147.456 piksel
```

Kare görselde yaklaşık 114× piksel alanı farkı.

### Çözüm

Live logo için cihaz yoğunluğunu gözeten yaklaşık:

```text
48–96 physical px
```

bandında decode kullan.

---

## P1-09 — Movies/Series posterleri de gerçek kart boyutundan çok büyük decode ediliyor

Mobil poster kartı yaklaşık 150–200 px civarındayken 384 px decode yaklaşık 4–6× fazla piksel alanı üretebilir.

### Etki

- daha pahalı decode,
- daha büyük native bitmap,
- daha büyük GPU texture upload,
- daha küçük effective cache capacity.

### Çözüm

Kart türüne göre decode profili:

```text
LiveLogo
PosterSmall
PosterLarge
Backdrop
Avatar
```

### Uygulama durumu (2026-08-13) — Tamamlandı

- `MobileImageDecodePolicy` ile kart rolü ve gerçek render yoğunluğuna göre bucket seçimi
  eklendi: `LiveLogo=64–96 px`, `PosterSmall=160–384 px`, `Backdrop=256–768 px`.
  Varsayılan/ayrıntı görsellerinin açık `DecodePixelWidth` davranışı korunuyor.
- `MobileLiveTvCard`, `MobileVodCard`, `MobileSeriesCard` ve
  `MobileContinueWatchingCard` ilgili profile bağlandı. DBY_W09 cihazında 36 DIP Live logosu
  `36 × 2,5 = 90` fiziksel piksel hedefinden `96 px` bucket'a çözülüyor; eski `384 px`
  decode'a göre yaklaşık `%94` daha az decode piksel alanı kullanıyor.
- `RemoteImage`, URL + çözülmüş bucket kimliğini birlikte izliyor. URL değişimi, recycle,
  detach, surface pasifleştirme, boyut değişimi ve `TopLevel.RenderScaling` değişimi yarışları
  stale commit üretmeden ele alınıyor; aynı etkili URL/bucket için gereksiz iptal/yeniden indirme
  yapılmıyor.
- Android canlı A/B testi ilk uygulamada bir self-sizing döngüsü yakaladı: kaynak henüz yokken
  poster `RemoteImage.Bounds.Width=0`, `RenderScaling=2,5` ve çözülmüş genişlik `0` kalıyordu.
  Önceki APK aynı veriyle posterleri yüklerken ilk profil APK'sı gri kalıyordu. Resolver artık
  ilk ölçülmüş visual ancestor genişliğini kullanıyor; tüm genişlikler başlangıçta sıfırsa
  `LayoutUpdated` yalnız henüz aktif istek yokken yeniden değerlendiriyor. Geçici ölçüm kodu
  final kaynaktan kaldırıldı. Final APK'da Movies, Series ve Search posterleri ile Live logoları
  dolu ve net olarak doğrulandı.
- Otomatik kanıt: image/Android odaklı paket `63/63`; 10 tekrar `630/630`. Tam takımda
  `2079` testin `2060` tanesi geçti. Kalan `19` hata image değişiklik alanı dışındaki mevcut
  lisans/promosyon, download ve mobil seçim testleridir; P1-08/P1-09 testi başarısız olmadı.
- Bağımsız son kod incelemesinde Critical/Important bulgu kalmadı; reviewer'ın kendi odaklı
  paketi `37/37` geçti ve değişiklik birleştirmeye hazır bulundu.
- Android arm64 Debug publish `0` hata ile tamamlandı; bilinen `NU1608`, `XA0141` ve platform
  uyarıları devam ediyor. İmzalı APK `181002695` byte, SHA-256
  `662D5A8EF5F39A8A5D45096A9FEBEF8BC8A3C26ACD529A6C319DB120D4E73FCA`.
- APK veri silmeden `adb install --user 0 -r -d` ile kuruldu; `firstInstallTime`
  `2026-08-10 17:51:36` olarak korundu. Final APK üzerinde Movies/Series/Live/Search'te toplam
  `80` çift yönlü kaydırma, `30` sayfa geçişi ve `10` gerçek launcher background/resume turu
  tamamlandı. PID `15373` her kontrolde sabit; ANR/crash/OOM eşleşmesi `0`.
- Bellek kanıtı: görseller yüklü Movies başlangıcında PSS `632391 KB`, RSS `702192 KB`,
  Graphics `95856 KB`; final stres ve 10 resume sonrasında PSS `583590 KB`, RSS `651652 KB`,
  Graphics `48516 KB`. Monoton büyüme veya foreground dönüşünde birikme görülmedi.

---

## P1-10 — Image response tamamen `MemoryStream`e kopyalanıp sonra decode ediliyor

Aynı anda bellekte:

- network buffer,
- compressed image byte array,
- decoded bitmap,
- native render resource

bulunabilir.

### Etki

Büyük JPEG/PNG'lerde:

- Gen0/Gen1/LOH baskısı,
- memory fragmentation,
- peak memory artışı.

### Çözüm

- Mümkünse bounded stream decode.
- Content-Length kontrolü.
- Maksimum response boyutu.
- Pooling veya temp buffer stratejisi.

---

## P1-11 — RemoteImage için response boyutu üst sınırı yok

Kötü provider artwork URL'si çok büyük bir JPEG/PNG döndürürse uygulama bunu indirmeye ve decode etmeye çalışabilir.

### Çözüm

- Header/content-length sınırı.
- Streaming sırasında maksimum byte sayısı.
- Decode dimension guard.
- Aşırı büyük artwork için fail-fast.

---

## P1-12 — 64 MB image cache gerçek bitmap/native-memory üst sınırı değil

`ByteBudgetLruCache` cache referansını ve tahmini byte budget'ı yönetiyor; ancak bitmap başka `Image.Source` referanslarıyla yaşamaya devam edebilir.

### Risk

```text
Cache 64 MB
+
visible Image.Source bitmapleri
+
evicted ama hâlâ referanslı bitmapler
+
in-flight decode
+
native/Skia/GPU kaynakları
```

gerçek tüketimi çok daha yüksek yapabilir.

### Çözüm

Deterministic ownership:

- cache lease,
- consumer ref-count,
- son consumer bırakınca dispose,
- native memory telemetry.

---

## P1-13 — Recycled/URL değiştiren kart eski `Source` bitmap'ini yeni görsel gelene kadar tutabiliyor

### Etki

- yanlış poster kısa süre görünebilir,
- eski bitmap gereğinden uzun strong-reference altında kalır,
- yavaş bağlantıda retention süresi büyür.

### Çözüm

URL generation değiştiği anda:

- placeholder/clear policy,
- stale source bırakma,
- aynı cache key ise flicker engelleyen istisna.

---

## P1-14 — Detach/inactive durumda image source bırakma politikası yeterince sıkı değil

`OnDetachedFromVisualTree` pending waiter'ı iptal etse de bitmap source ownership'i ayrıntılı şekilde serbest bırakılmıyor.

### Çözüm

View/card inactivity ve image cache ownership ayrı tutulmalı.

### P1-07 / P1-12 / P1-13 / P1-14 uygulama durumu — 2026-08-13: Tamamlandı

- `SharedImageResource<T>` ile producer, cache ve görünür consumer sahipliği tek bir
  ref-count yaşam döngüsünde birleştirildi. Cache eviction/clear, `Image.Source` değişimi ve
  son consumer bırakımı aynı native bitmap'i tam bir kez dispose ediyor. Owned native byte,
  peak byte, resource ve consumer lease sayaçları telemetriye eklendi.
- `ByteBudgetLruCache` eviction callback'i ve kilit altında atomik projected lookup kazandı;
  cache hit sırasında consumer lease alınması ile eşzamanlı eviction arasındaki pencere
  kapatıldı. Callback'ler cache kilidi dışında çalışıyor.
- İndirilen/decode edilen bitmap artık loader içinde doğrudan cache'e yazılmıyor. Yalnız
  generation, URL, decode bucket, foreground/surface ve görünürlük kontrollerini geçen UI
  terminal consumer sonucu cache'e publish edebiliyor. Son consumer kalmamışsa geç gelen
  sonuç cache'i kirletmeden bırakılıyor.
- Shared loader tamamlandıktan sonra cache publish/UI projection bitene kadar aynı key entry
  join edilebilir kalıyor; aynı poster için ikinci download penceresi kapatıldı. Son-consumer
  cancellation ile loader completion arasındaki `CancellationTokenSource.Cancel/Dispose`
  yarışı ayrı terminal-state bariyeriyle exact-once hale getirildi.
- URL/effective cache key değiştiğinde eski `Source` ve lease hemen bırakılıyor; aynı effective
  key için flicker üretmeyen preserve istisnası korunuyor. Detach, surface inactive, geçersiz
  ölçü ve boş URL yolları kaynağı serbest bırakıyor. Tüm kuyruğa alınmış source apply/clear
  işlemleri monoton mutation generation doğruluyor; eski UI clear callback'i daha yeni source'u
  veya same-key preserve kararını silemiyor.
- Otomatik kanıt: final image/ownership paketi `94/94`; 10 tekrar `940/940`. Tam takım
  `2080/2099`; kalan `19` hata image değişiklik alanı dışındaki mevcut lisans/promosyon,
  download, mobil seçim ve tek metadata timeout testindeydi. P1-07/P1-12/P1-13/P1-14 testi
  başarısız olmadı. `git diff --check` temizdi.
- Bağımsız son kod incelemesinde Critical/Important bulgu kalmadı. Reviewer; same-key source
  mutation, completed-entry join ve cancellation/dispose bariyerlerini yeniden inceledi,
  kendi genişletilmiş paketini `35/35` geçirip değişikliği merge-ready olarak onayladı.
- Android arm64 Debug build `0` hata ile tamamlandı; bilinen `NU1608`, `XA0141`, nullable ve
  platform uyarıları devam ediyor. İmzalı APK `180575705` byte, SHA-256
  `B9E4D5C7A2E9B85D4DE0BD750EA842D27C7FC6A11232DA4A04D0D241EF62E0EB`.
- APK DBY_W09 cihazına veri silmeden `adb install --user 0 -r -d` ile kuruldu;
  `firstInstallTime` `2026-08-10 17:51:36` olarak korundu, `lastUpdateTime`
  `2026-08-13 15:21:06` oldu.
- Final APK'da gerçek logolu/posterli Live, Movies, Series ve Search sonuç yüzeyleri arasında
  geçiş ve toplam `73` kontrollü çift yönlü scroll gesture uygulandı. Doğrulanmış `8`
  launcher background/resume turunda ve testin tamamında PID `31363` sabit kaldı;
  ANR/crash/OOM eşleşmesi `0`, test penceresine ait yeni process exit kaydı `0` oldu.
- Bellek kanıtı: içerik ısınma/kaydırma zirvesinde PSS `749247 KB`, Graphics `128016 KB` idi.
  Inactive/background source bırakımı sonrası Graphics `63696 KB`; son foreground settle'da
  PSS `662287 KB`, Graphics `48516 KB` oldu. Görsel kaynaklarda monoton retention görülmedi.
  Native Heap son settle'da `316476 KB` kaldı; bu aşama bitmap/source ownership sorununu
  kapatıyor fakat uygulamanın yüksek native taban tüketimi sonraki memory/DB/render maddeleri
  için izlenmeye devam etmeli.
- Ham ekran/UI kanıtları:
  `artifacts/p1-07-p1-14-live-20260813/`.

---

## P1-15 — Bitmap tamamlanınca UI update `DispatcherPriority.Render` ile post ediliyor

Backlog oluştuğunda onlarca image completion scroll'un en hassas render zamanına girebilir.

### Çözüm

- stale/visibility check,
- coalesced UI apply,
- gerektiğinde daha düşük priority,
- frame başına limitli image commit.

---

## P1-16 — Her `RemoteImage` 180 ms opacity transition oluşturuyor

Network'ten gelen her poster aynı anda fade animasyonu başlatabilir.

### Özellikle kötü durum

- hızlı scroll,
- cache miss,
- birçok completion,
- Render-priority source assignment.

### Çözüm

- Cache hit → anında göster.
- Recycled fast-scroll → animasyon kapalı.
- İlk network load → isteğe bağlı fade.

---

## P1-17 — Android memory pressure (`OnTrimMemory`) ile image/cache küçültme stratejisi yok

### Risk

Android process memory baskısı bildirdiğinde Noctra:

- poster cache,
- metadata cache,
- inactive artwork

için adaptif trim yapmıyor.

### Çözüm

`OnTrimMemory` / lifecycle seviyelerine göre:

- UI hidden → daha agresif trim,
- moderate/critical → cache clear/trim,
- foreground → normal budget.

---

## P1-18 — DB sorguları `Task.Run` ile ThreadPool'a bırakılıyor ama uygulama-geneli concurrency sınırı yok

**Etkilenen alan:** `ContentQueryService`

Aynı anda:

- paging,
- search,
- history,
- EPG,
- metadata,
- download,
- playlist refresh

aynı SQLite dosyasına yönelebilir.

### Risk

- ThreadPool backlog,
- SQLite contention,
- eski query'lerin yenilerin önünü kesmesi.

### Çözüm

Merkezi scheduler:

```text
DB read lane: 2–3
DB write lane: 1
interactive query priority > background
```

### Uygulama durumu — 13 Ağustos 2026: TAMAMLANDI

- Process genelinde tek `IDatabaseWorkScheduler` oluşturuldu; DI ve `MainViewModel`
  uyumluluk yolu aynı process-owned örneği kullanıyor.
- Kuyruk kapasitesi sınırlandı. Okuma lane'i en fazla `3`, yazma lane'i en fazla `1`
  eşzamanlı iş çalıştırıyor.
- Interactive/background öncelik kuyrukları eklendi; bounded interactive burst ile
  background işlerin starvation'a uğramaması sağlandı.
- `ContentQueryService` içindeki sorgu başına `Task.Run` kaldırıldı. Paging, Search,
  History, seri listesi ve grup metadata sorguları interactive read lane üzerinden
  çalışıyor; scheduler token'ı EF/provider çağrısına kadar taşınıyor.
- Bekleyen iptal, aktif iptal, hata yayılımı, kapasite reddi, çift/concurrent dispose,
  Schedule/Cancel/Dispose yarışları ve exception fırlatan cancellation callback'i için
  exact-terminal garantisi eklendi.
- Masaüstü çıkışında scheduler yeni admission'ı kapatıp bekleyen/aktif DB işlerini
  provider disposal'dan önce non-blocking olarak iptal ediyor.
- Telemetry: `db.queue.pending.high_water`, `db.queue.read.active.high_water`,
  `db.queue.write.active.high_water`, scheduled/completed/cancelled/failed/rejected ve
  shutdown-callback-failure sayaçları eklendi.

Doğrulama:

- Odaklı scheduler + ContentQuery + DI + shutdown paketi: `16/16` geçti.
- Yarış stresi: `30/30` tekrar, sıfır hata.
- Bağımsız production review: kalan Critical/Important yok; kod merge-ready.
- Tüm regresyon: `2113/2120` geçti. Kalan `7` hata P1-18 dosyalarının dışında,
  önceden bilinen promo-code, mobile selection, download ve entitlement testleri.
- `Noctra.Mobile` net10 derlemesi: `0` hata (`21` mevcut uyarı).
- Android arm64 APK derlemesi: `0` hata (`92` mevcut paket/nullable/platform uyarısı).
- APK mevcut veri silinmeden `adb install -r` ile kuruldu; `firstInstallTime`
  `2026-08-10 17:51:36` olarak korundu.
- Gerçek katalogla `36` hızlı Live/Movies/Series geçişi, `25` paging scroll ve `20`
  background/foreground döngüsü tamamlandı; Android PID `12816` boyunca değişmedi.
- Son bellek örneği: PSS `587165 KB`, native heap `311744 KB`, graphics `64596 KB`.
- Temiz logcat taramasında ANR/crash/OOM/fatal-process: `0`; exit-info'da yeni ANR/crash
  kaydı yok, yalnız beklenen paket kurulumuna ait `USER REQUESTED` stop mevcut.

Sonuç: P1-18 kapatıldı. DB/EPG aşamasındaki sıradaki açık madde **P1-19 — SQLite
PRAGMA connection-open standardizasyonu**.

---

## P1-19 — SQLite PRAGMA tuning'in bir kısmı yalnız schema-fixup connection'ında uygulanıyor olabilir

`cache_size`, `temp_store`, `synchronous` vb. bazı ayarlar connection/session scope davranabilir. Uygulama ise `IDbContextFactory` ile yeni connection/context üretir.

### Risk

Kodda tuning var görünür ancak gerçek paging/TMDB/EPG connection'ları varsayılan ayarlarda çalışabilir.

### Çözüm

- Connection-open interceptor.
- Her yeni connection için gerekli PRAGMA seti.
- PRAGMA'ları gerçekten aktif connection'da telemetry ile doğrula.
---
### Uygulama durumu — 13 Ağustos 2026: TAMAMLANDI

- `SqliteConnectionPragmaInterceptor`, EF Core `AddDbContextFactory<AppDbContext>` yoluna singleton olarak bağlandı. Senkron ve asenkron her connection-open sonrasında bağlantı-özel PRAGMA’lar uygulanıyor.
- Masaüstü ve Android cache/mmap profilleri DI üzerinden seçiliyor; WAL yalnız schema-fixup başlangıç yolunda bırakıldı.
- Apply/verification exception veya cancellation ile kesilirse bağlantı kapatılıyor ve özgün exception/token korunuyor.
- Odaklı paket `19/19`, 10 tekrar `190/190`; geniş DB paketi `33/33`. Tam regresyon `2123/2130`; kalan 7 hata P1-19 dışındaki bilinen testlerdir.
- Android canlı kanıtında 48 connection-open uygulaması, 20 sayfa geçişi, 40 scroll ve 10/10 background/foreground turu tamamlandı; PID sabit ve ANR/crash/OOM `0`.

Sonuç: P1-19 kapatıldı. Sıradaki madde **P1-20 — görünür EPG refresh kapsamını gerçek viewport ile sınırlama**.

---

## P1-20 — EPG “visible refresh” gerçekte tüm loaded `Channels` koleksiyonunu işliyor

Kod yorumunda visible program title refresh denmesine rağmen tüm loaded channels enrich edilebiliyor.

### Etki

Live'da scroll ettikçe collection büyür ve 5 dakikalık refresh maliyeti de büyür.

### Çözüm

Gerçek viewport visible set:

```text
visible channel IDs
+
küçük overscan
```

ile refresh.

### Uygulama durumu — 20 Ağustos 2026: UYGULANDI

- `MobileVirtualizingCardGrid` ve `DesktopVirtualizingCardGrid`, `VirtualizingStackPanel` tarafından
  gerçekleşen satırlardaki kaynak öğeleri döndürüyor. Böylece görünür alan ve virtualization cache
  overscan'ı ViewModel'e taşınıyor; tüm loaded koleksiyon yeniden taranmıyor.
- Mobile/desktop Live view attach, scroll, `ActiveView` ve `FilteredChannels.CollectionChanged`
  anlarında snapshot yayınlıyor; görünüm, playlist, profil, kategori ve sıralama değişimlerinde
  snapshot temizleniyor. Snapshot live channel ID ile deduplicate edilip `96` öğe ile
  sınırlandırılıyor.
- Beş dakikalık UI timer yalnız `ActiveView == Live` ve boş olmayan snapshot varsa çalışıyor. Aynı
  timer callback'lerinin üst üste binmesini atomic single-flight bayrağı engelliyor. EPG süresi
  dolduğunda memory temizliği de yalnız snapshot üzerinde yapılıyor.
- Dispatcher callback'leri ViewModel invalidation generation ve attachment/Live görünüm kontrolüyle
  korunuyor; EPG expiration sonrası in-memory temizleme de `ClearEpgAsync` dönüşünde aynı snapshot
  version'ını yeniden doğruluyor. Böylece eski playlist/profile sonucu yeni Live surface'e geri
  yazılamıyor.
- Otomatik doğrulama: P1-20 davranış/contract paketi `7/7`; 10 tekrar `70/70`; EPG enrichment,
  visible-refresh, database-filter, write-scheduling, keyset-pagination, metadata-notification,
  accessibility ve Android activity contract odak paketi `30/30`. Tam takım `2173/2179` geçti; kalan `6` hata P1-20/P1-29 dışındaki mevcut mobil seçim,
  download görünürlüğü, sezon indirme ve sleep-timer testleridir.
- Sonraki reklam commitlerinin Android erişilebilirlik regresyonu için eklenen banner peer contract
  testi `1/1` geçti; `NoneAutomationPeer` guard'ı odak pakette korunuyor.
- Build: Core `0` hata; Mobile `0` hata (yalnız mevcut 2 uyarı); Avalonia `0` hata; Android arm64
  `0` hata (mevcut AndroidX/Java binding uyarıları). P1-29 sonrası güncel APK `380858660` byte,
  SHA-256 `74E4B26584D6AD722460884E6CAF839E43DA79EBA159F965AFEB936A2FA3E59B`.
- APK DBY_W09 cihazına `adb install -r` ile veri silmeden kuruldu; `firstInstallTime`
  `2026-08-10 17:51:36` korundu (`lastUpdateTime` `2026-08-20 14:46:41`). Önceki kabulde
  `uiautomator dump` sırasında görülen `InteropAutomationPeer.GetOrCreateChildrenCore`
  `NotImplementedException` crash'i `BannerNativeControlHost` için `NoneAutomationPeer` ile
  kapatıldı; güncel APK'da iki accessibility dump PID değişmeden tamamlandı.
- İlk arka plan/ön plan guard koşusunda aynı launcher intent'inin yeni `MainActivity` örnekleri
  ürettiği, aynı süreçte dokuz pencere biriktirdiği ve Mono large-object heap doğrulamasının
  `SIGABRT` verdiği log/tombstone ile doğrulandı. `MainActivity` artık
  `LaunchMode.SingleTask`; güncel APK'da gerçek içerikli Live/Movies/Series geçişinde `20`
  navigation, `40` çift yönlü scroll ve ardından `10/10` HOME/launcher turu yapıldı. PID `7431`
  sabit kaldı, görevde tek `MainActivity` kaldı, yeni `SIGABRT`/`NotImplementedException` oluşmadı.
  P1-29 final smoke sonrası bellek örneği PSS `435814 KB`, RSS `512564 KB`, Native Heap
  `174144 KB`, Graphics `48708 KB`.
- SQLite PRAGMA uygulama telemetrisi `34` connection-open olayı kaydetti; ham telemetri
  `artifacts/p1-20-live/p1-20-final.jsonl` SHA-256
  `FF0771CF06D892CF05BE6A01CB8CAC640CFA94089C46A72A0A713C9A50DAF95E`.

Sonuç: P1-20 kod, test ve canlı cihaz kabulü kapatıldı. P1-21 ve P1-22 aşağıdaki doğrulamalarla
aynı EPG/lifecycle paketinde kapatıldı; sıradaki bağımsız EPG maddesi **P1-23**.

---

## P1-21 — EPG refresh büyük PropertyChanged dalgası üretebiliyor

Önce program title/progress temizlenip ardından yeniden doldurulması yüzlerce model mutation oluşturabilir.

### Çözüm

- EPG state'i atomic/batched uygula.
- Değer gerçekten değişmediyse setter çağırma.
- Visible item'lara öncelik ver.

### Uygulama durumu — 20 Ağustos 2026: DOĞRULANDI/KAPATILDI

- EPG sonucu tek dispatcher commit'inde uygulanıyor; setter'lar yalnızca başlık veya ilerleme
  değeri gerçekten değiştiğinde çağrılıyor.
- P1-20 görünür snapshot'ı en fazla `96` Live channel ile sınırlandığı için tek refresh'te
  PropertyChanged üretimi tüm loaded playlist'e değil, görünür karta bağlı kalıyor.
- Aynı EPG snapshot'ında `0`, değişen başlık+ilerlemede yalnız `2` bildirim beklentisini doğrulayan
  regresyon testleri eklendi; güncel EPG/lifecycle odak paketi `29/29` geçti.

Sonuç: P1-21 kapatıldı. Eşit değerlerde bildirim dalgası yok; değişen değerlerde yalnız gerekli
alanlar bildiriliyor.

---

## P1-22 — EPG UI timer foreground/active-view aware değil ve explicit single-flight guard eksik

### Risk

- uygulama background'dayken gereksiz iş,
- yavaş refresh ile yeni timer tick'in overlap etmesi,
- foreground'a dönüldüğünde eski EPG callback'lerinin resume işleriyle çakışması.

### Çözüm

```text
if !AppForeground || ActiveView != Live → skip
if refresh already running → skip
```

### Uygulama durumu — 20 Ağustos 2026: P1-20 İLE KAPATILDI

- Timer yalnız `ActiveView == Live` ve boş olmayan görünür snapshot ile çalışıyor.
- Atomic single-flight bayrağı üst üste timer callback'lerini eliyor; snapshot/version guard'ı
  eski foreground veya eski Live yüzeyinin commit'ini engelliyor.
- Android canlı kabulünde 20 navigation, 40 scroll ve 10/10 launcher background/foreground
  turu tek PID ve tek `MainActivity` ile tamamlandı.

Sonuç: P1-22 kapatıldı. Sıradaki madde **P1-23 — EPG servisinde tüm playlist channel'larını
materialize etmeden DB seviyesinde Live filtresi**.

---

## P1-23 — EPG servisinde bütün playlist channel'ları materialize edilip sonra Live filtreleniyor

### Risk

Büyük provider'da Movies/VOD dahil tüm channel entity'leri:

- DB'den okunur,
- managed object yapılır,
- sonra `.Where(Type == Live)` uygulanır.

### Çözüm

DB seviyesinde:

```sql
WHERE PlaylistId = ?
AND Type = Live
```

### Uygulama durumu — 20 Ağustos 2026: UYGULANDI/KAPATILDI

- `IPlaylistService.GetLiveChannelsAsync` eklendi; `PlaylistService` artık yalnız
  `PlaylistId` ve `ChannelType.Live` koşullarını içeren `AsNoTracking` sorguyu çalıştırıyor.
- `EpgService.LoadLiveChannelsForEpgAsync` eski `GetChannelsAsync` + managed-memory filtreleme
  yolundan çıkarıldı ve DB-filtreli yönteme yönlendirildi. VOD/Series kayıtları bu EPG eşleştirme
  çağrısında materialize edilmiyor.
- Auto-EPG playlist oluşturma ve public `RefreshEpgAsync` yolları da `GetLiveChannelsAsync` ile
  DB-filtreli hale getirildi; `Include(p => p.Channels)` kaldırıldı. Böylece tüm EPG girişleri
  VOD/Series kayıtlarını EPG mapping snapshot'ına taşımıyor.
- Eski geniş çağrıların kullanılmadığını ve yeni sözleşmenin SQL filtresini koruduğunu doğrulayan
  contract testi ile mevcut EPG zaman/loader testleri dahil güncel odak doğrulama `30/30` geçti.
- Son P1-28 APK `19F0071DE9C37DFD2793DD0E9878920E61C782202BEBDFDAA91EBA58C0669B66` ile veri
  silmeden kuruldu; accessibility dump, Live açılışı ve `5/5` launcher foreground/background
  turunda PID `17769` sabit kaldı, yeni native crash oluşmadı.

Sonuç: P1-23 kapatıldı. DB/EPG kümesinde sonraki madde **P1-24 — EPG writer batch/write burst**;
bu madde de aşağıdaki uygulama ile kapatılmıştır.

---

## P1-24 — EPG 2500 kayıtta bir büyük SQLite write burst oluşturuyor

`SaveChanges` büyük batch'lerde yapılabiliyor.

### Etki

Aynı anda TMDB/download/history write varsa SQLite writer contention.

### Çözüm

- Benchmark ile 500–1000 batch değerlendir.
- Daha önemlisi merkezi DB write lane.
- Background import düşük priority.

### Uygulama durumu — 20 Ağustos 2026: UYGULANDI/KAPATILDI

- EPG write batch boyutu `2500` yerine `500` olarak sınırlandı; her batch sonrası change tracker
  temizlenmeye devam ediyor.
- Üretim DI’sındaki ortak `DatabaseWorkScheduler` kullanılıyor. EPG yazıları
  `DatabaseWorkLane.Write + DatabaseWorkPriority.Background` ile kuyruğa alınıyor; interaktif
  SQLite işleri background import’un önüne geçebiliyor.
- Scheduler callback’ine cancellation token aktarılıyor; kuyrukta bekleyen veya çalışan EPG yazısı
  uygulama kapanışı/timeout sırasında terminal olarak iptal edilebiliyor.
- Batch boyutu, lane ve DI bağlantısını koruyan contract testi ile EPG/DB odak paketi `30/30`
  geçti. Android arm64 build `0` hata verdi.
- Son APK `33CB5D2E752264B58A7E1179D6E6414BEDEDD693D922C292E16E0980F235FB24` ile veri silmeden
  kuruldu; `firstInstallTime` `2026-08-10 17:51:36` korundu. Accessibility dump, Live açılışı
  ve `5/5` launcher foreground/background turu PID `15059` ile tamamlandı; yeni native crash yok.

Sonuç: P1-24 kapatıldı. DB/EPG kümesinde sıradaki madde **P1-28 — OFFSET pagination**.

---

## P1-25 — `HttpResponseMessage` lifecycle'ı deterministic dispose edilmiyor

**Etkilenen alan:** `MetadataService`

Response okunuyor ancak `using` / dispose yolu eksik.

### Risk

Çok sayıda TMDB request'te gereksiz resource retention ve GC baskısı.

### Çözüm

```csharp
using var response = await ...
```

### Uygulama durumu — 20 Ağustos 2026: UYGULANDI/KAPATILDI

- `MetadataService` içindeki üç `GetWithFallbackAsync` tüketicisi yanıt sahipliğini artık
  `using var response` ile deterministik olarak sonlandırıyor.
- Proxy yanıtından doğrudan TMDB fallback'ine geçişte eski yanıt kapatılıyor; başarısız doğrudan
  yanıt ve exception yolları da açık response bırakmıyor.
- Başarılı response content'inin servis çağrısı bitmeden dispose edildiğini doğrulayan gerçek
  `HttpMessageHandler` regresyon testi eklendi.

---

## P1-26 — Playlist ve metadata aynı 3 dakikalık `HttpClient.Timeout` politikasını paylaşabiliyor

3 dakika büyük playlist için mantıklı olabilir; poster/TMDB için çok uzun.

### Kötü ağ senaryosu

- navigation'dan bağımsız eski TMDB işi,
- uzun timeout,
- retry/fallback,
- çok sayıda batch

→ request lifetime çok uzar.

### Çözüm

Named/typed client:

```text
PlaylistClient → uzun timeout
MetadataClient → ~10–20 s
ImageClient → ayrı policy
LicenseClient → ayrı policy
```

### Uygulama durumu — 20 Ağustos 2026: UYGULANDI/KAPATILDI

- Ortak `HttpClient.Timeout = 3 dakika` playlist/provider indirmeleri için korunuyor; global
  timeout düşürülmedi.
- Metadata isteklerinin her proxy ve doğrudan fallback denemesi bağımsız, linked `20 saniye`
  bütçe kullanıyor. Böylece bir proxy timeout'u doğrudan denemenin süresini tüketmiyor.
- Kullanıcı/navigation cancellation'ı timeout'tan ayrılıyor ve `OperationCanceledException`
  olarak yukarı taşınmaya devam ediyor.
- Metadata lifecycle/cancellation/API-key paketi `19/19`; yeni timeout/dispose testleri `2/2`
  geçti. P1-30 sonrası tam regresyon sonucu `2183/2189`; kalan `6` hata bu değişikliklerden önce
  mevcut olan mobil seçim, download ve sleep-timer grubunda.

---

## P1-27 — Search SQL filtreleri index kullanımını zorlaştıran formda

Örnek desenler:

- `ToLower().Contains(...)`
- `Trim() == ...`

### Risk

Büyük SQLite tablosunda scan maliyeti.

### Çözüm

- normalize edilmiş ayrı kolon,
- uygun index,
- gerekiyorsa FTS5,
- `%term%` yerine uygun arama mimarisi.

---

## P1-28 — Deep paging için `OFFSET` kullanımı ilerleyen sayfalarda pahalılaşabilir

```text
OFFSET 0
OFFSET 300
OFFSET 3000
OFFSET 30000
```

giderek daha çok satır atlatabilir.

### Çözüm

Default stable order için keyset/cursor pagination:

```sql
WHERE Id < @lastId
ORDER BY Id DESC
LIMIT 30
```

### Uygulama durumu — 20 Ağustos 2026: UYGULANDI/KAPATILDI

- `ContentPageCursor(LastId)` artık `ContentPageRequest` → `ContentQueryService` →
  `PlaylistService` zincirinden taşınıyor.
- `NewestFirst` için `Id < lastId`, `OldestFirst` için `Id > lastId` predicate'i SQL sorgusuna
  ekleniyor; sıralama adı gibi cursor için doğal olmayan sort'larda mevcut `OFFSET` fallback'i
  korunuyor.
- `MainViewModel` son channel ID cursor'ını yalnızca sayfa UI'ya başarıyla commit edildikten sonra
  ilerletiyor; navigation/filter/profile reset'lerinde cursor temizleniyor. Cancellation veya stale
  generation durumunda cursor ilerlemiyor.
- Cursor sınırının strict ilerlediğini, request zincirinden taşındığını ve eski sort fallback'inin
  korunduğunu doğrulayan keyset + ContentQuery testleriyle güncel odak paketi `30/30` geçti.
- Android arm64 build `0` hata verdi. Son APK `19F0071DE9C37DFD2793DD0E9878920E61C782202BEBDFDAA91EBA58C0669B66`
  veri silmeden kuruldu; accessibility dump, Live açılışı ve `5/5` launcher foreground/background
  turu PID `17769` ile tamamlandı, yeni native crash oluşmadı.

Sonuç: P1-28 kapatıldı. Sıradaki madde **P1-29 — TMDB enrichment çift PropertyChanged**.

---

## P1-29 — TMDB sonucu aynı model property'leri için çift `PropertyChanged` üretebiliyor

`[ObservableProperty]` generated setter zaten notification üretirken daha sonra `NotifyMetadataChanged()` aynı property'leri tekrar bildirebiliyor.

### Etki

Birçok item enrichment'ta:

- duplicate binding invalidation,
- card template update,
- image source reevaluation.

### Çözüm

Tek notification ownership'i:

- generated setter yeterliyse manuel notify kaldır,
- computed properties için yalnız gerekli dependent property notify et.

### Uygulama durumu — 20 Ağustos 2026: UYGULANDI/KAPATILDI

- `Channel.NotifyMetadataChanged` artık generated setter'ların zaten bildirdiği Plot, Director,
  Cast, Rating, ContentRating ve BackdropUrl alanlarını tekrar bildirmiyor.
- TMDB commit aşaması yalnız plain `LogoUrl`, computed `CoverUrl`/`Description` ve değişmişse
  `TmdbId` için manuel bildirim gönderiyor; değişiklik bayrakları UI commit'inden önce hesaplanıyor.
- Böylece tek metadata enrichment artık aynı property için iki ayrı `PropertyChanged` dalgası
  üretmiyor. Notification sahipliğini doğrulayan Channel regresyon testi ve güncel odak paketi
  `30/30` geçti.
- Son Android APK `74E4B26584D6AD722460884E6CAF839E43DA79EBA159F965AFEB936A2FA3E59B` ile veri
  silmeden kuruldu. Önceki smoke koşulunda cihaz SIGSEGV'i görüldü; kontrollü Live açılışı sonrası
  `10/10` HOME/launcher turu PID `21374` ile temiz geçti. Son APK'da ayrıca accessibility dump ve
  `3/3` HOME/launcher turu PID `23154` ile temiz geçti. Tekil cihaz olayının logu korunuyor;
  tekrarlanabilir crash/NotImplementedException oluşmadı.

Sonuç: P1-29 kapatıldı. Sıradaki madde **P1-30 — Background worker'ların UI'ya senkron Invoke yapması**.

---

## P1-30 — Background worker'ların UI'ya senkron `Invoke` yapması backpressure oluşturabilir

UI thread yoğunken worker:

```text
Dispatcher.Invoke
→ UI'yi bekler
```

ve kendi gate/context/state'ini daha uzun tutar.

### Feedback loop

```text
UI yoğun
→ worker UI bekliyor
→ işler tamamlanamıyor
→ backlog
→ UI daha da yoğun
```

### Çözüm

- immutable result,
- async/coalesced post,
- generation check,
- worker thread'i UI bekleyerek bloklama.

### Uygulama durumu — 20 Ağustos 2026: GEREKLİ HOT-PATH KAPSAMI UYGULANDI

- Madde toplu bir `Invoke → Post` dönüşümü olarak uygulanmadı. Audit, gerçek background/hot
  çağrıları normal UI-thread ve sıralama gerektiren çağrılardan ayırdı.
- Oynatıcı `PlayingChanged`, `BufferingChanged`, `VolumeChanged` ve kalite callback'leri
  latest-only sürüm guard'lı `BeginInvoke` kullanıyor; eski kuyruk callback'i yeni state'i ezemiyor.
- Track retry ve playback health worker'ları UI sonucunu thread bloklamadan `await InvokeAsync`
  ile alıyor; her committe cancellation/playback request identity tekrar doğrulanıyor. Timer yolu
  non-blocking post ve yeniden doğrulama kullanıyor.
- ViewModel health worker'ındaki doğrudan native `Stop/Play` döngüsü kaldırıldı. Reconnect işlevi
  kaybedilmeden, en fazla dört deneme standart `PlayChannelAsync(existingRequestVersion)`
  generation pipeline'ına taşındı; exception gözleniyor ve kalan denemeler bounded biçimde sürüyor.
  Böylece eski recovery yeni medyayı durduramıyor ve native iş UI dispatcher closure'ına taşınmıyor.
- Yeni playback intent callback kabul kapısını kapatıyor; gerçek yeni `PlayAsync` başlamadan hemen
  önce açılıyor. Bu nedenle eski native event yeni intent'ten sonra ulaşsa bile Playing/Buffering,
  Position ve Quality state'ine yazamıyor.
- Live/Movies/Search sayfa append commit'i ile EPG enrichment commit'i immutable sonuç sonrası
  `await InvokeAsync` kullanıyor; cancellation/navigation ve commit guard UI üzerinde tekrar
  kontrol ediliyor.
- Normal UI akışındaki veya atomik sıralama gerektiren düşük frekanslı senkron çağrılar kanıtsız
  şekilde değiştirilmedi. Bu karar rapordaki tüm önerilerin zorunlu değil, güncel kodda ölçülebilir
  risk üretenlerin uygulanması ilkesine uygundur.
- Oynatıcı odak paketi `180/180`; event-after-intent, latest-only dispatch ve kaynak contract
  testleri geçti. Tam takım `2183/2189` geçti.
- Final Android arm64 build `0` hata verdi. `380858660` byte imzalı APK'nın SHA-256 değeri
  `B1163D3E2A70EB047FB2982C495FF68889D91871A1B2A7F2DA3B63FCED0D11ED`.
- APK `adb install --user 0 -r -d` ile veri silmeden kuruldu; `firstInstallTime`
  `2026-08-10 17:51:36` korundu, `lastUpdateTime` `2026-08-20 15:42:41` oldu.
- Persisted profil seçildi, Live açıldı ve `BEIN BOX OFFICE 1 FHD` player'ı başlatıldı.
  `MobilePlayerView` içinde `3/3` HOME/launcher turu ve player'dan Live'a geri çıkış PID `2382`
  değişmeden tamamlandı; logcat'te yeni ANR, `FATAL EXCEPTION` veya native fatal signal yok.

Sonuç: P1-30'un doğrulanan backpressure yolları kapatıldı ve **Aşama 4 — Network ve
notification tamamlandı**. Sonraki P2/P3 maddeleri otomatik yapılmayacak; her biri önce güncel kod
ve cihaz bulgularıyla gereklilik audit'inden geçirilecek.

---

# 5. P2 — ORTA ÖNCELİKLİ SORUNLAR

## P2-01 — `AddRange` sonrasında gereksiz collection `PropertyChanged` gönderiliyor

Collection zaten collection-change event üretirken ayrıca:

```text
OnPropertyChanged(nameof(FilteredChannels))
```

gibi notification'lar tekrar gönderilebiliyor.

`NotifyContentStateChanged()` da tek olayda birden fazla property notify ediyor.

### Etki

Binding engine'e sürekli küçük ek yük.

### Çözüm

Collection reference gerçekten değişmediyse collection property notify etme.

---

## P2-02 — Filter coalescer mevcut apply bittikten sonra ikinci pahalı pass başlatabiliyor

Single-flight olması iyi ancak hızlı navigation/filter değişiminde:

```text
current apply
→ pending flag
→ current tamamlanır
→ latest apply tekrar
```

oluşabilir.

### Çözüm

Superseding state:

- eski sonuç commit edilmeden düşür,
- yalnız latest generation final state'i uygulasın.

---

## P2-03 — `SettingsChanged` çok geniş subsystem refresh zinciri tetikliyor

Tek genel event:

- EPG schedule,
- channel refresh,
- group reorder,
- filter apply

gibi ilgisiz işlemleri birlikte tetikleyebiliyor.

### Çözüm

Typed change event:

```text
ThemeChanged
LanguageChanged
ContentFilterChanged
EpgSettingsChanged
PlaybackSettingsChanged
```

---

## P2-04 — Her `MobilePressableCard` parent `ScrollViewer.ScrollChanged` event'ine ayrı ayrı abone oluyor

Normal scroll sırasında pointer aktif değilken bile event N kart handler'ına fan-out yapar.

### Çözüm

- Yalnız pointer pressed süresince subscribe,
- veya tek `ScrollGestureCoordinator`.

---

## P2-05 — Scroll gesture başlangıcında kart pressed animasyonu gereksiz başlayıp geri alınabiliyor

Finger down:

1. card pressed,
2. opacity/scale transition,
3. scroll eşiği aşılır,
4. pressed cancel,
5. ters transition.

### Çözüm

Scroll tespitinde transition'sız hızlı reset veya gesture disambiguation.

---

## P2-06 — Full row rebuild çok sayıda geçici `List`, array ve `ToList` allocation oluşturuyor

`IncrementalRowCollection.Rebuild` sırasında:

- row listeleri,
- row array'leri,
- tail array,
- `ReplaceAll` materialization

oluşabiliyor.

### Etki

Duplicate reset'lerle birleştiğinde GC churn.

### Çözüm

- Incremental row update,
- pooled buffers,
- gereksiz full rebuild'i kaldır.

---

## P2-07 — `MobileSectionedCardFeed` tek section değişiminde bütün subscription ağını yeniden kurabiliyor

### Etki

Search sonuçları sık değişirken gereksiz:

- unsubscribe,
- subscribe,
- rows rebuild.

### Çözüm

Sadece değişen section'ı güncelle.

---

## P2-08 — Damerau/Levenshtein implementasyonu her row'da array kopyalıyor

Üç buffer zaten `ArrayPool` ile kullanılırken her satır sonunda:

- `Array.Copy(prev, pprev)`
- `Array.Copy(curr, prev)`

yapılması gereksiz.

### Çözüm

Buffer referans rotation:

```text
pprev ← prev
prev  ← curr
curr  ← eski pprev
```

---

## P2-09 — Failed image URL dictionary'si süreç boyunca gereksiz büyüyebilir

TTL mevcut olsa da yalnız tekrar erişilen anahtarların temizlenmesi gibi bir davranış varsa bir daha görülmeyen URL'ler kalabilir.

### Çözüm

- max entry count,
- periodic expiration sweep,
- bounded cache.

---

## P2-10 — `_personalStateGates` keyed `SemaphoreSlim` dictionary'si remove edilmiyor

Her farklı favorite/my-list state key için yeni semaphore kalabilir.

### Çözüm

- keyed lock implementation,
- ref-counted entry,
- iş bittiğinde güvenli remove,
- veya bounded keyed async lock.

---

## P2-11 — WatchHistory repair dictionary bazı exit path'lerinde temizlenmeyebilir

Cleanup başarılı repair sonunda yapılıyor ancak:

- cancellation,
- exception,
- `newChannels.Count == 0` erken dönüşü

gibi path'lerde retained veri kalabilir.

### Çözüm

Ownership'e uygun `finally` cleanup.

---

## P2-12 — Aktif download, Downloads ekranı görünmese de periyodik DB/UI işi üretir

Download progress sırasında:

- SQLite persistence,
- `DownloadsChanged`,
- dispatcher post

devam edebilir.

### Çözüm

- UI event'ini active view kontrolünden sonra post et,
- progress DB write coalescing,
- son progress state'i memory'de tutup daha seyrek persist et.

---

## P2-13 — ExoPlayer loaded-but-paused durumda 500 ms position polling devam edebilir

Player UI artık aktif değilse dahi loaded media state'i polling'i sürdürebilir.

### Etki

Her 500 ms:

- timer,
- Android main thread post,
- position/duration/buffer read,
- event.

### Çözüm

Player surface inactive + not playing ise polling'i durdur.

### Uygulama durumu — 20 Ağustos 2026: UYGULANDI/KAPATILDI

- `UpdatePositionPollingForLoadedMedia()` artık yalnızca `_hasLoadedMedia && _isPlaying`
  durumunda timer'ı çalıştırıyor. Pause, stop, ended ve error yolları 500 ms polling üretmiyor;
  yeniden playing/ready callback'i timer'ı tekrar açıyor.
- Android contract testi önce mevcut koddaki eksik koşul nedeniyle kırmızı görüldü, koşul eklendikten
  sonra yeşile döndü. Android/oynatıcı odak paketi `187/187`; tam takım `2185/2190` geçti.
- P2-13 final APK SHA-256 `F7836EFD5A11710BE4E8CA7AFEABD105C73E9BF16827DC98083C3C194194EEB`,
  boyut `381461764` byte. Veri silmeden kuruldu; `firstInstallTime` `2026-08-10 17:51:36`,
  `lastUpdateTime` `2026-08-20 15:56:50` olarak kaldı.
- Gerçek Live player açılışı, `3/3` HOME/launcher döngüsü ve player'dan Live'a dönüş PID `6243`
  değişmeden tamamlandı; yeni ANR, `FATAL EXCEPTION` veya native fatal signal görülmedi.

---

## P2-14 — Shared `HttpClient.DefaultRequestHeaders.Authorization` mutation concurrency açısından riskli

Metadata service request-specific header kullanabildiği halde shared default header da mutate edilebiliyor.

### Risk

Farklı servislerin paylaştığı singleton client üzerinde auth state yarışları / gereksiz başarısız request.

### Çözüm

Authorization'ı yalnız request message üzerinde ayarla.

---

## P2-15 — Resume sırasında update/license gibi ek işler ana recovery ile aynı anda başlayabiliyor

Tek başına kök neden değil ancak foreground anında:

- Play update,
- license/subscription refresh,
- immersive/lifecycle işleri

layout/image/EPG callback'leriyle çakışabilir.

### Çözüm

- foreground critical path'i minimal tut,
- non-urgent işleri düşük priority ile geciktir/coalesce et.

---

# 6. P3 — DÜŞÜK ÖNCELİKLİ / SAĞLAMLAŞTIRMA

## P3-01 — Android main-thread helper bazı çağrılarda her seferinde yeni `Handler` oluşturuyor

Sürekli position polling gibi hot path'te küçük allocation.

### Çözüm

Tek cached:

```csharp
Handler _mainHandler
```

kullan.

---

## P3-02 — High-frequency workload'a rağmen `PooledDbContextFactory` kullanılmıyor

Bu tek başına sorun değildir; ancak çok sık context create/dispose edilen Noctra'da benchmark edilmeye değer.

### Öncelik

Önce DB concurrency ve query mimarisi düzeltilmeli, sonra pooling ölçülmeli.

---

## P3-03 — Responsive metric/converter cache davranışı gerçek kullanım moduyla tam hizalı olmayabilir

Responsive hesaplamalarda cache anahtarı gerekli tüm layout/device parametrelerini kapsamazsa cache hit yerine tekrar hesap veya thrash oluşabilir.

### Çözüm

- cache key'i gerçek mode/density/width bileşenleriyle tanımla,
- allocation-free fast path kullan.

---

## P3-04 — MainViewModel uzun yaşayan servis event'lerine subscribe oluyor; lifetime değişirse dispose guard'ı zayıf

Android mevcut DI yapısında MainViewModel singleton ise normal Activity resume/recreation'da duplicate VM leak riski düşüktür.

Ancak ileride lifetime değiştirilirse:

```text
long-lived service
→ event delegate
→ old VM
```

retention oluşabilir.

### Çözüm

`IDisposable`/subscription token yaklaşımıyla lifecycle güvenceye alınmalı.

---

# 7. Birbiriyle çakışan / birbirini büyüten sorun kümeleri

## 7.1 Resume cluster

```text
P0-01 retained pages
+
P0-03 global resume
+
P0-04 retry
+
P0-05 layout chain invalidation
+
P1-22 foreground-insensitive EPG
+
P2-15 update/license workload
```

Bu cluster doğrudan foreground dönüşü sonrası jank/freeze üretir.

---

## 7.2 Scroll + image cluster

```text
P0-07 gerçek cancellation yok
+
P0-08 bounded backlog yok
+
P1-07 offscreen cache pollution
+
P1-08/P1-09 oversized decode
+
P1-10 MemoryStream peak memory
+
P1-11 response size guard yok
+
P1-12 bitmap ownership/native memory
+
P1-15 Render-priority apply
+
P1-16 opacity transitions
+
P2-04 card ScrollChanged fan-out
```

Bu cluster özellikle Movies/Series/Live uzun scroll probleminde en güçlü adaylardan biridir.

---

## 7.3 Search cluster

```text
P0-09 multi full-scan
+
P0-10 paging sonrası full recompute
+
P1-01 cooperative cancellation yok
+
P1-02 6 collection Reset
+
P1-03 hidden Search work
+
P1-27 SQL maliyeti
+
P2-08 edit-distance Array.Copy
+
P2-07 section rebuild
```

Büyük IPTV listesinde Search'ın rakip uygulamalardan daha çabuk yorulmasını açıklayabilir.

---

## 7.4 TMDB cluster

```text
P0-11 navigation-dışı backlog
+
P0-12 global olmayan concurrency limiter
+
P1-04 item başına network/DB/UI
+
P1-05 dual Series pipeline
+
P1-06 VOD negative-cache yok
+
P1-25 response dispose eksik
+
P1-26 3 dakika metadata timeout
+
P1-29 duplicate PropertyChanged
```

Scroll bittikten sonra bile uygulamanın arkada çalışmaya devam etmesinin başlıca kaynaklarından biri.

---

## 7.5 SQLite / EPG cluster

```text
P1-18 unbounded DB Task.Run
+
P1-19 connection PRAGMA eksikliği
+
P1-20 tüm loaded channel EPG refresh
+
P1-21 property mutation dalgası
+
P1-22 foreground/single-flight eksikliği
+
P1-23 tüm playlist'i materialize etme
+
P1-24 2500 row write burst
+
P1-28 OFFSET pagination
```

SQLite tek dosya üzerinde farklı background subsystem'lerin birbirini geciktirmesine yol açabilir.

---

# 8. Düzeltilme sırası — önerilen gerçek uygulama planı

Aşağıdaki sıra, en yüksek kullanıcı etkisi / en yüksek çarpan mantığıyla önerilmektedir.

## Aşama 1 — Kritik backlog ve lifecycle

1. **P0-07** RemoteImage gerçek cancellation.
2. **P0-08** Bounded/priority image work queue.
3. **P0-01** Ağır sayfaları active-host modeline geçir.
4. **P0-03 / P0-04 / P0-05** Resume recovery'yi active-grid scoped hale getir.
5. **P0-06** Navigation duplicate collection reset'lerini kaldır.
6. **P0-11 / P0-12** TMDB global bounded queue + navigation cancellation.

## Aşama 2 — Search ve image memory

7. **P0-09 / P0-10** Search engine'i incremental/cancellable yap.
8. **P1-08 / P1-09** Decode size'ları kart türüne göre küçült.
9. **P1-07 / P1-12 / P1-13 / P1-14** Image ownership/cache commit policy.
10. **P1-02** Search collection Reset yerine diff.

## Aşama 3 — DB/EPG

11. **P1-18** Global DB read/write scheduling.
12. **P1-19** PRAGMA connection-open standardizasyonu.
13. **P1-20 / P1-21 / P1-22** EPG yalnız visible/foreground/single-flight.
14. **P1-23** Live channels DB filter.
15. **P1-24** EPG writer batch benchmark + düşük-priority write lane.
16. **P1-28** Keyset pagination.

## Aşama 4 — Network ve notification

17. **P1-25 / P1-26** Metadata response dispose + typed HTTP clients.
18. **P1-29** Duplicate metadata notifications.
19. **P1-30** Synchronous UI Invoke kaldırma.
20. **P2-01 / P2-02 / P2-03** Notification/filter/settings churn azaltma.

## Aşama 5 — Frame/allocation polishing

21. Card ScrollChanged fan-out.
22. Press animation scroll davranışı.
23. Row rebuild allocations.
24. Sectioned feed resubscribe.
25. Levenshtein buffer rotation.
26. ExoPlayer polling.
27. `Handler` allocation.
28. Context pooling benchmark.

---

# 9. Önerilen yeni ortak mimari

## 9.1 `NoctraWorkScheduler`

```text
NoctraWorkScheduler
├─ UIInteractive
│  ├─ active paging
│  └─ active search commit
│
├─ Image
│  ├─ visible high priority
│  ├─ overscan medium
│  └─ offscreen cancel/drop
│
├─ Metadata
│  └─ global max 3
│
├─ DatabaseRead
│  └─ max 2–3
│
└─ DatabaseWrite
   └─ max 1
```

Her iş:

- `ProfileGeneration`,
- `NavigationGeneration`,
- `ViewGeneration`,
- `CancellationToken`,
- `Priority`

taşımalıdır.

---

## 9.2 `ViewGeneration`

Örnek:

```text
Movies generation 41
↓
Series açıldı
↓
Movies generation 41 cancelled/stale
```

Generation 41'e bağlı:

- paging,
- TMDB,
- artwork,
- Search augmentation,
- dispatcher callbacks

UI'ya commit edememelidir.

---

## 9.3 Image request coordinator

Gerekli özellikler:

- same-URL dedupe,
- consumer ref count,
- son consumer ayrılınca cancellation,
- bounded pending queue,
- visibility priority,
- per-card decode profile,
- size guard,
- deterministic bitmap ownership.

---

# 10. Ölçüm / telemetry eklenmesi gereken sayaçlar

## Image

```text
ActiveDownloads
PendingDownloads
InFlightLoads.Count
ActiveDecodes
CancelledWaiters
ActuallyCancelledDownloads
CacheHits
CacheMisses
CacheBytes
DecodedBitmapCount
DecodedBitmapEstimatedBytes
StaleImageCompletionCount
ImageQueueDropCount
```

## TMDB

```text
QueuedMetadataJobs
RunningMetadataJobs
CancelledMetadataJobs
StaleMetadataCommits
AverageMetadataLatency
DBWritesFromMetadata
```

## Search

```text
SearchGeneration
DatasetSize
CandidateCount
ScoredItemCount
FuzzyDistanceCount
SearchComputeMs
SearchCommitMs
SearchResetCount
```

## DB

```text
QueuedDbReads
RunningDbReads
QueuedDbWrites
RunningDbWrites
AverageReadMs
AverageWriteMs
SQLiteBusyCount
```

## Layout

```text
GridResumeRecoveryCount
ResumeRetryCount
InactiveGridRecoveryCount
FullRowRebuildCount
MeasureInvalidationCount
ArrangeInvalidationCount
```

## Runtime

```text
GC.CollectionCount(0)
GC.CollectionCount(1)
GC.CollectionCount(2)
GC.GetTotalAllocatedBytes()
ManagedHeapSize
AndroidNativeHeap
FrameTimeP50
FrameTimeP95
FrameTimeP99
FrozenFrameCount
```

---

# 11. Performans kabul kriterleri

Patch tamamlandıktan sonra yalnız “daha hızlı hissediliyor” yeterli değildir.

## Scenario A — Long scroll

- Movies'te en az 500–1000 item hızlı scroll.
- Series'te aynı test.
- Geri yukarı scroll.
- `InFlightLoads` scroll durduktan kısa süre sonra viewport seviyesine inmeli.
- Offscreen download backlog kalmamalı.
- P95 frame time sürekli yükselmemeli.

## Scenario B — Navigation torture

```text
Live → Movies → Series → Search → Live
```

50+ tekrar.

- managed heap sürekli lineer artmamalı,
- active view sayısı kontrollü kalmalı,
- collection full-reset sayısı navigation sayısıyla katlanmamalı.

## Scenario C — Background/foreground

20–50 tekrar:

- inactive grid resume recovery = 0,
- foreground dönüşünde büyük layout spike olmamalı,
- eski generation metadata/image callback'i UI'ya commit etmemeli.

## Scenario D — Search

Büyük playlist üzerinde:

- 1 harf,
- 2 harf,
- 5 harf,
- hızlı sil/yaz,
- Search içinde scroll.

Search hesaplamaları supersede edilmeli; eski query CPU tüketmeye devam etmemeli.

## Scenario E — EPG + scroll

EPG import/refresh çalışırken Live scroll:

- UI thread frame time ciddi bozulmamalı,
- DB write lane interactive read'i uzun süre engellememeli.

---

# 12. İlk raporlarda sonradan düzeltilen / şartlı hale getirilen tespitler

Bu bölüm önemlidir; eski ara yorumların yanlışlıkla “kesin bug” olarak uygulanmaması gerekir.

## 12.1 `_allSeriesCache` her Search'te full episode graph yüklemiyor

Güncel akışta lightweight series list kullanılıyor. Bu nedenle “Search kesin olarak tüm season/episode graph'ını tarıyor” iddiası mevcut kod için fazla güçlüydü.

**Geçerli kalan sorun:** series/channel alanlarında büyük fuzzy full-scan hâlâ pahalı.

---

## 12.2 MainViewModel event subscription leak'i mevcut Android lifetime'da kesin değil

`CoreMainViewModel` Android tarafında singleton ise normal Activity resume/recreation yeni VM üretmeyebilir.

**Geçerli kalan risk:** lifetime gelecekte değişirse dispose/unsubscribe guard'ı eksikliği retention yaratabilir.

---

## 12.3 Download persistence “her 1 MB'da sınırsız write” şeklinde değerlendirilmemeli

Kodda zaman tabanlı kontrol de bulunduğundan pratik frekans daha sınırlıdır.

**Geçerli kalan sorun:** Downloads ekranı inactive iken dahi periodic DB/event/dispatcher workload oluşturabilir.

---

## 12.4 Pagination throttle ana kök neden olarak değerlendirilmedi

`MobileScrollPaging` tarafında throttling/deferred çalışma bulunuyor.

**Geçerli kalan sorun:** deep SQLite `OFFSET` ve paging sonrası Search/TMDB side-effect'leri.

---

## 12.5 Overscroll/shake sistemi ana suçlu olarak görülmedi

RenderTransform temelli yaklaşım ana layout storm kadar tehlikeli görünmüyor.

**Geçerli kalan küçük maliyet:** press/scroll gesture transition davranışı ve her-card ScrollChanged subscription.

---

## 12.6 `OnMediaSelected` için açık event leak kanıtı bulunmadı

Kodun unsubscribe/subscribe koruması mevcut.

Bu konu mevcut performans kök neden listesine dahil edilmemelidir.

---

# 13. Master checklist

| ID | Öncelik | Sorun | Ana alan |
|---|---|---|---|
| P0-01 | P0 | Tüm ağır sayfalar retained | Navigation/UI |
| P0-02 | P0 | Inactive view'lar iş yapmaya devam ediyor | Lifecycle |
| P0-03 | P0 | Global resume fan-out | Lifecycle |
| P0-04 | P0 | 8× Render-priority resume retry | Layout |
| P0-05 | P0 | Parent-chain layout invalidation | Layout |
| P0-06 | P0 | Duplicate/triple collection reset | ViewModel |
| P0-07 | P0 | RemoteImage gerçek request cancel etmiyor | Images |
| P0-08 | P0 | Image backlog bounded değil | Images |
| P0-09 | P0 | Search çoklu full-scan fuzzy ranking | Search |
| P0-10 | P0 | Search paging full recompute | Search |
| P0-11 | P0 | TMDB navigation-dışı backlog | Metadata |
| P0-12 | P0 | TMDB limiter batch-local | Metadata |
| P1-01 | P1 | Search compute cooperative cancellation yok | Search |
| P1-02 | P1 | 6 Search collection Reset | Search |
| P1-03 | P1 | Hidden Search work | Search |
| P1-04 | P1 | Item başına TMDB network+DB+UI | Metadata |
| P1-05 | P1 | Series dual enrichment | Metadata |
| P1-06 | P1 | VOD negative cache yok | Metadata |
| P1-07 | P1 | Offscreen image cache pollution | Images |
| P1-08 | P1 | 36 px logo 384 px decode | Images |
| P1-09 | P1 | Poster oversized decode | Images |
| P1-10 | P1 | Full image MemoryStream buffering | Images |
| P1-11 | P1 | Image response size guard yok | Images |
| P1-12 | P1 | 64 MB cache gerçek native cap değil | Images |
| P1-13 | P1 | Recycled kart eski Source tutuyor | Images |
| P1-14 | P1 | Detach image ownership gevşek | Images |
| P1-15 | P1 | Image apply Render priority | Render |
| P1-16 | P1 | Her image 180 ms fade | Render |
| P1-17 | P1 | Android OnTrimMemory/cache trim eksik | Memory |
| P1-18 | P1 | Unbounded DB Task.Run/concurrency | SQLite |
| P1-19 | P1 | PRAGMA her connection'a uygulanmıyor olabilir | SQLite |
| P1-20 | P1 | EPG visible refresh tüm loaded channels | EPG |
| P1-21 | P1 | EPG PropertyChanged storm | EPG |
| P1-22 | P1 | EPG foreground/single-flight guard eksik | EPG |
| P1-23 | P1 | EPG tüm playlist'i materialize ediyor | EPG/DB |
| P1-24 | P1 | 2500-row EPG writer burst | EPG/DB |
| P1-25 | P1 | HttpResponseMessage dispose eksik | Network |
| P1-26 | P1 | Metadata için 3 dk timeout | Network |
| P1-27 | P1 | Search SQL index-dostu değil | SQLite/Search |
| P1-28 | P1 | Deep OFFSET pagination | SQLite |
| P1-29 | P1 | Duplicate metadata PropertyChanged | Binding |
| P1-30 | P1 | Background → UI senkron Invoke | Dispatcher |
| P2-01 | P2 | Gereksiz collection PropertyChanged | Binding |
| P2-02 | P2 | Filter coalescer ikinci pahalı pass | Filtering |
| P2-03 | P2 | SettingsChanged broad refresh | State |
| P2-04 | P2 | Her kart ScrollChanged subscriber | Scroll |
| P2-05 | P2 | Swipe başında press animation | Scroll |
| P2-06 | P2 | Full row rebuild allocation churn | Collections |
| P2-07 | P2 | Section feed full resubscribe/rebuild | Search/UI |
| P2-08 | P2 | Levenshtein Array.Copy hot path | Search/CPU |
| P2-09 | P2 | Failed image URL cache growth | Memory |
| P2-10 | P2 | PersonalState semaphore dictionary growth | Memory |
| P2-11 | P2 | WatchHistory repair retention paths | Memory |
| P2-12 | P2 | Inactive Downloads DB/UI activity | Downloads |
| P2-13 | P2 | Paused player 500 ms polling | Player |
| P2-14 | P2 | Shared HttpClient default auth mutation | Network |
| P2-15 | P2 | Resume update/license workload collision | Lifecycle |
| P3-01 | P3 | Her main-thread post'ta Handler allocation | Android |
| P3-02 | P3 | DbContext pooling yok | EF Core |
| P3-03 | P3 | Responsive metric cache/thrash riski | UI metrics |
| P3-04 | P3 | VM subscription lifetime guard zayıf | Lifecycle |

---

# 14. Nihai değerlendirme

NoctraPlayer'daki uzun kullanım performans sorunu şu dört ana eksenin birleşimidir:

1. **Lifecycle / retained UI**
2. **Backpressure / cancellation eksikliği**
3. **Büyük veri üzerinde tekrar eden hesaplama ve collection churn**
4. **Bitmap + SQLite + metadata gibi ağır kaynakların merkezi orkestrasyonunun olmaması**

En büyük mimari eksik tek cümleyle:

> **Her subsystem kendi concurrency/caching/refresh politikasına sahip, fakat uygulama genelinde ortak bir workload budget ve generation-based cancellation sistemi yok.**

Bu nedenle yalnız tek tek mikro-optimizasyon yapılması yeterli değildir. P0/P1 maddeleri birlikte ele alınırsa Live/Movies/Series/Search ekranlarının uzun kullanım davranışının ilk açılıştaki akıcılığa çok daha yakın kalması beklenir.
