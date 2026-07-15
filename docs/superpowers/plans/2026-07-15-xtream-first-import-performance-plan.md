# Xtream İlk Import Performansı Uygulama Planı

## Faz 1 — Ölçüm sözleşmesi ve sentetik kaynak

1. Core'da no-op performans probe sözleşmesi için davranış testlerini yaz ve kırmızı çalıştır.
2. Monotonic event modeli, scope/tag desteği ve düşük-overhead no-op implementasyonu ekle.
3. Sentetik Xtream katalog üreticisi için deterministik adet/dağılım/duplicate testlerini yaz.
4. `tools/Noctra.Performance` altında mock Xtream HTTP sunucusu ve katalog üreticisini ekle.
5. 10K katalog üzerinde auth, kategori ve streaming içerik endpoint'lerini doğrula.

## Faz 2 — Uygulama ve Android ölçüm noktaları

1. Profil seçimi, shell, ilk 30 veri, import batch, SQLite commit ve seri indeksleme event'leri için kırmızı guard/davranış testleri yaz.
2. Core akışlarına yalnızca ölçüm event'leri ekle; probe kapalıyken davranışın değişmediğini doğrula.
3. Android'de Choreographer/main-thread ve runtime GC/memory collector ekle.
4. SQLite command sürelerini ve thread bilgisini kaydeden interceptor ekle.
5. Debug benchmark intent/config girişini aynı package id içinde etkinleştir.

## Faz 3 — ADB runner ve baseline

1. Runner self-check: cihaz, package, `run-as`, `adb reverse`, termal durum ve disk alanı.
2. Install/clear/profile-create/profile-select/trace/export akışını olay bazlı otomasyona bağla.
3. DB/WAL/SHM ve `dumpsys meminfo` örneklemesini ekle.
4. Kill %30/%70, profil değişimi %10/%50 ve düşük-disk fault senaryolarını ekle.
5. Önce 10K sanity, ardından 10K/50K/100K/250K için en az beş soğuk koşu çalıştır.
6. JSON, CSV ve Markdown baseline raporunu üret.

## Faz 4 — Kök neden ve Xtream patch'i

1. Trace'ten en pahalı producer, queue, writer, UI reload ve aggregation aşamasını sırala.
2. Her doğrulanan sorun için tek bir kırmızı regresyon/performance test yaz.
3. Bounded producer/consumer queue ve tek writer'ı minimum patch olarak uygula.
4. Kanıtlanırsa batch başı `COUNT(*)`, geniş DELETE ve progress/UI reload frekansını kaldır/throttle et.
5. İlk gerçek commit sonrası profil erişimi ve startup recovery'yi uygula.
6. Cancellation ve düşük-disk recovery testlerini yeşile getir.

## Faz 5 — Son doğrulama

1. Core ve tüm mevcut test paketini çalıştır.
2. Android Debug ve Release build al.
3. Aynı cihaz/veri/koşu matrisini patch sonrası yeniden çalıştır.
4. Baseline–patch karşılaştırma raporu oluştur.
5. Kod incelemesi yap, önemli bulguları düzelt ve nihai sonuçları teslim et.
