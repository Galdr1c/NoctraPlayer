# Xtream İlk Import Performansı: Ölçüm ve Optimizasyon Tasarımı

## Amaç

Huawei DBY-W09 (Android 12 / API 31, yaklaşık 5.6 GB RAM) üzerinde ilk Xtream profil yüklemesindeki donmayı ölçülebilir ve tekrarlanabilir hale getirmek, kök nedeni kanıtlamak ve yalnızca kanıtlanan darboğazı düzeltmek.

Bu çalışma SQLite'ı değiştirmeyi veya performans hipotezlerini ölçmeden uygulamayı kapsamaz. İlk teslimat benchmark altyapısı ve baseline raporudur. İkinci teslimat, baseline verisinin işaret ettiği Xtream import patch'idir. M3U aynı ölçüm altyapısıyla daha sonra ayrı bir çalışma olarak ele alınır.

## Mevcut Koddan Doğrulananlar

- Xtream yanıtları `ResponseHeadersRead` ve `DeserializeAsyncEnumerable` ile okunuyor ve 500 kayıtta bir callback'e aktarılıyor.
- Her Xtream batch'i bugün `ReplaceDummyWithRealChannelsAsync` üzerinden dummy silme, URL bazlı silme, batch organizasyonu, prepared bulk insert ve playlist genelinde `COUNT(*)` güncellemesi yapıyor.
- UI, seçili kategori için batch sırasında tekrar tekrar reload tetikleyebiliyor.
- M3U parser streaming çalışıyor ve prepared bulk insert kullanıyor; ancak import boyunca büyüyen string `HashSet` tutuyor ve her 500 kaydı ayrı organize/transaction akışından geçiriyor.
- Profil yükleme generation/cancellation scope'una sahip. Buna rağmen provider callback'leri, DB yazarı ve UI reload noktalarının tamamında aynı token zincirinin sona kadar korunduğu ölçülmüş değil.
- İlk M3U playlist'i tamamlanana kadar inactive tutuluyor. Xtream boş playlist'i daha erken oluşturuyor; fakat erişilebilir ilk gerçek sayfanın süresi ölçülmüyor.
- Seri agregasyonu bazı akışlarda background çalışıyor. Importla aynı anda CPU/SQLite çekişmesi oluşturup oluşturmadığı bilinmiyor.

Bu maddeler kök neden değildir; benchmarkta sınanacak hipotezlerdir.

## İşin Bölünmesi

1. **Ölçüm harness'i ve baseline:** sentetik Xtream sunucusu, cihaz runner'ı, uygulama içi probe'lar ve rapor.
2. **Xtream import patch'i:** baseline tarafından doğrulanan darboğazlara yönelik bounded pipeline, tek writer, throttling ve recovery.
3. **M3U baseline ve patch:** aynı matrisle ayrı değerlendirme.
4. **Uzun kullanım/UI sanallaştırması:** ilk import sorunu çözüldükten sonra ayrı çalışma.

## Sentetik Xtream Kaynağı

Yerel bir test sunucusu Xtream `player_api.php` davranışını taklit eder. Tablet sunucuya `adb reverse` ile bağlanır. Ayrı application id kullanılmaz; mevcut `studio.kynora.noctra` debug kurulumu kullanılır ve kullanıcı onayı doğrultusunda koşular arasında uygulama verisi temizlenebilir.

Veri setleri:

| Set | Toplam içerik | Live | VOD | Series |
|---|---:|---:|---:|---:|
| S | 10.000 | 4.000 | 3.000 | 3.000 |
| M | 50.000 | 20.000 | 15.000 | 15.000 |
| L | 100.000 | 40.000 | 30.000 | 30.000 |
| XL | 250.000 | 100.000 | 75.000 | 75.000 |

İçerikler 100 kategoriye dengeli dağıtılır. ID, stream URL, başlık ve kategori değerleri deterministiktir. Duplicate ve kategori taşıma varyantları ayrıca küçük bir doğruluk setinde tutulur; ana performans seti her koşuda aynı veriyi üretir. Poster/EPG yanıtları ilk sayfa ölçümü boyunca devre dışıdır.

Her set en az beş soğuk koşu ile çalıştırılır; böylece median ve p95 değerleri tekil bir koşuya dayanmaz. Her koşudan önce uygulama verisi temizlenir, sentetik sunucu sıfırlanır ve cihazın termal/bellek başlangıç durumu kaydedilir. Aşırı termal throttling tespit edilen koşu geçersiz sayılır ve tekrarlanır.

## Ölçüm Mimarisi

### Uygulama içi probe'lar

Core tarafında no-op çalışabilen bir performans probe sözleşmesi bulunur. Android debug ölçümünde bu probe monotonic timestamp ile JSON-lines olayları üretir. Ölçüm kapalıyken import kontrol akışını değiştirmez.

Temel işaretler:

- `profile.tap`
- `profile.shell_visible`
- `profile.first_frame`
- `content.first_30_available`
- `content.first_30_rendered`
- `xtream.categories_received`
- `xtream.batch_received`
- `xtream.batch_enqueued`
- `sqlite.batch_begin` / `sqlite.batch_commit`
- `import.first_rows_committed`
- `import.completed`
- `series.index_begin` / `series.index_completed`
- `profile.scope_cancelled`
- `import.recovery_begin` / `import.recovery_completed`

İlk 30 içerik için veri koleksiyonuna eklenme ve kartların UI frame'inde gerçekleştirilmesi ayrı ölçülür. Böylece sorgu gecikmesi ile render gecikmesi birbirine karışmaz.

### Android ve runtime ölçümleri

- Perfetto trace: UI thread scheduling, frame timeline ve uzun main-thread slice'ları.
- Android frame probe: Choreographer aralıkları ve en uzun frame gecikmesi.
- SQLite command interceptor: komut süresi, çağıran thread, okuma/yazma ayrımı; ana threadde 50 ms üzerindeki komutlar açıkça işaretlenir.
- `System.Runtime` meter/event sayaçları: managed heap, GC collection sayıları, GC pause toplamı ve tekil/p95 pause süreleri.
- `adb shell dumpsys meminfo studio.kynora.noctra`: native heap, graphics/EGL/GL mtrack ve process toplamı.
- `run-as studio.kynora.noctra`: SQLite DB, WAL ve SHM boyutları.
- Writer sayaçları: parse, queue bekleme, SQLite yazma ve commit süreleri; başarılı satır sayısı.

Örnekleme ve trace overhead'i ayrı bir kontrol koşusunda ölçülür; toplam süreyi yüzde 5'ten fazla etkileyen probe sadeleştirilir.

## Otomasyon Akışı

ADB runner aşağıdaki adımları tek komutla yürütür:

1. Sentetik sunucuyu seçilen veri boyutuyla başlatır ve `adb reverse` kurar.
2. Gerekli Debug APK'yı mevcut package id üzerine kurar.
3. Uygulama verisini temizler ve Xtream test profilini oluşturur.
4. Perfetto, logcat, bellek ve DB boyutu örneklemesini başlatır.
5. Profili UI üzerinden seçer; uygulama içi `profile.tap` ortak zaman ekseninin başlangıcıdır.
6. İlk frame, ilk 30 kart, import ve seri indeksleme tamamlanma koşullarını olay bazlı bekler.
7. Trace, JSONL, meminfo, DB boyutları ve sunucu sayaçlarını workspace'e alır.
8. Sonuçları tek bir JSON ve CSV özeti ile Markdown raporuna dönüştürür.

Keyfi `sleep` süreleri başarı koşulu olarak kullanılmaz. Runner her aşamada olay veya timeout bekler ve timeout'u ölçüm sonucu olarak raporlar.

## Failure ve Recovery Senaryoları

### Process kill

10K doğrulama koşusundan sonra 50K ve 250K setlerinde import yaklaşık yüzde 30 ve yüzde 70 seviyesindeyken process öldürülür. Uygulama tekrar açıldığında:

- profil shell'i erişilebilir olmalı,
- commit edilmiş batch'ler bozulmamalı,
- import checkpoint'ten devam etmeli veya idempotent biçimde yeniden başlamalı,
- aynı profile ait birden fazla running job/staging playlist kalmamalı.

### Profil değiştirme

Import yaklaşık yüzde 10 ve yüzde 50 seviyesindeyken ikinci profile geçilir. Eski scope'un network reader, bounded queue, SQLite writer, progress callback ve UI reload işlemleri iptal edilmelidir. Trace'te `profile.scope_cancelled` sonrasında eski profile yeni DB/UI olayı yazılması hata sayılır.

### Düşük disk

Fiziksel cihaz diski doldurulmaz. Writer'ın belirlenmiş batch sınırında SQLite `FULL` eşdeğeri hata üretmesini sağlayan debug-only fault injection kullanılır. Aktif playlist korunur, başarısız import job işaretlenir, geçici veri tekilleştirilir/temizlenir ve sonraki açılışta profil erişilebilir olur.

## Kanıt Sonrası Xtream Pipeline Hedefi

Baseline doğrulamadıkça production akışı değiştirilmez. Beklenen patch sınırı şöyledir:

- JSON/network producer ile SQLite consumer arasında kapasitesi sınırlı bir `Channel<T>`.
- Profil/import başına tek SQLite writer ve bütün yazımlar için aynı cancellation zinciri.
- Her batch'te tam `COUNT(*)` yerine writer içi artan sayaç; progress ve DB metadata güncellemesi en fazla saniyede bir.
- Provider kimliği/stream fingerprint'i üzerinde unique index ve idempotent UPSERT; batch başına geniş `DELETE` döngüsü yok.
- İlk gerçek commit sonrasında playlist'in okunabilir hale gelmesi; profile shell ve ilk sayfa tam importu beklemez.
- UI reload'larının ilk görünür batch, kategori tamamlanması ve throttled aralıklarla sınırlandırılması.
- Poster, EPG ve metadata enrichment'in ilk sayfadan önce başlamaması.
- Seri indekslemenin import tamamlanma şartı olmaması; ölçüm sonucuna göre writer ile incremental veya import sonrasında düşük öncelikli olarak çalışması.
- Running import ve staging kayıtları için startup recovery; profil/kaynak başına tek geçerli oturum.

Unique index/UPSERT anahtarı gerçek sentetik duplicate testleri ve mevcut kullanıcı verisi üzerinde doğrulanmadan seçilmez. `StreamUrl` tek başına yeterli çıkmazsa normalize provider ID/fingerprint persist edilir.

## Kabul Bütçeleri

| Metrik | Bütçe |
|---|---:|
| Profil tıklaması → shell ilk frame p95 | ≤ 500 ms |
| Profil tıklaması → ilk 30 gerçek kart render p95 | ≤ 1.000 ms |
| En uzun main-thread bloklanması | < 100 ms |
| Main thread SQLite komutu | ≤ 50 ms |
| 250K managed bellek artışı | ≤ 256 MB |
| Poster kapalı native/bitmap artışı | ≤ 64 MB |
| GC pause p95 | < 50 ms |
| En uzun GC pause | < 100 ms |
| Median SQLite yazım hızı | ≥ 2.000 satır/s |
| Recovery sonrası shell | ≤ 1.000 ms |
| Profil başına terk edilmiş staging/running import | 0 |

Seri indeksleme süresi, DB/WAL/SHM mutlak boyutları, içerik başına byte ve toplam import süresi her koşuda raporlanır. Bunlar baseline görülmeden yapay bir eşikle maskelenmez; ancak shell ve ilk sayfayı bloklamaları kabul edilmez.

## Doğruluk ve Regresyon Testleri

- 500 kayıt sınırında batch parçalama ve son eksik batch.
- Duplicate içeriğin tek satır kalması ve metadata güncellemesi.
- Aynı başlığın farklı provider ID ile korunması.
- Kategori taşıma durumunda eski ve yeni satırın tekilleşmesi.
- Progress yazımının throttle edilmesi.
- Batch writer'ın tek eşzamanlı writer olması.
- Cancellation sonrası yeni yazım/UI callback olmaması.
- Kill sonrası tek import/staging ile recovery.
- SQLite `FULL` sonrası aktif verinin korunması.
- İlk committen sonra profile query yapılabilmesi.
- Seri indekslemenin ilk sayfa barrier'ına dahil olmaması.

Production değişiklikleri her davranış için önce kırmızı test, ardından minimal patch ile uygulanır. 10K sanity koşusu geçmeden daha büyük cihaz koşularına geçilmez.

## Teslimatlar

1. Sentetik Xtream sunucusu ve veri üreticisi.
2. Huawei tablet için ADB/Perfetto benchmark runner'ı.
3. Uygulama içi düşük-overhead ölçüm probe'ları ve güvenli fault injection.
4. 10K/50K/100K/250K baseline JSON, CSV ve Markdown raporu.
5. Kök neden sıralaması; her maddeyi destekleyen trace/metric referansı.
6. Kanıtlanan darboğaza yönelik Xtream patch'i.
7. Aynı matrisle patch sonrası karşılaştırmalı rapor.

## Kapsam Dışı

- M3U optimizasyonunu Xtream patch'ine karıştırmak.
- SQLite'ı başka veritabanıyla değiştirmek.
- Poster cache veya uzun kullanım sanallaştırmasını ilk import patch'ine dahil etmek.
- Gerçek cihaz depolamasını doldurarak düşük disk testi yapmak.
- Ölçüm olmadan batch boyutu, queue kapasitesi veya checkpoint politikasını sabit kabul etmek.
