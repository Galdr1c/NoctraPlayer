# NoctraPlayer Performans ve Stabilite — Faz 1 Uygulama Raporu

Tarih: 11 Ağustos 2026  
Birincil hedef: Android  
Kaynak inceleme: `NoctraPlayer_Performance_Stability_Full_Report.md`

## Sonuç

Ana rapordaki gecikmeli kasma/donma zincirinin ilk kritik bölümü kod üzerinde doğrulandı. Problem tek bir görsel hatadan değil; görünmeyen sayfalarda biriken görsel işleri ile her Android resume olayında birden fazla grid'in yüksek öncelikli ve üst görsel ağaca yayılan yeniden yerleşim çalışması başlatmasının birleşiminden doğuyordu.

Bu fazda resume/layout fırtınası sınırlandı ve görsel yükleme hattı gerçek tüketici ömrüne bağlandı. Değişiklikler Android ve ortak mobil katmanda uygulandığı için masaüstünde ortak `RemoteImage`/mobil bileşenlerin kullanıldığı senaryolara da fayda sağlar; fakat bu fazın asıl kabul hedefi Android'dir.

## Doğrulanan kök nedenler

1. `MainView`, Home/Live/Movies/Series/Search dahil ağır içerikleri aynı anda oluşturuyor ve görünürlüğü değiştirerek visual tree'de tutuyor. Bu, rapordaki P0-01 ve P0-02 bulgularını doğrular.
2. Visual tree'ye bağlı her `MobileVirtualizingCardGrid`, global resume sinyalini alıyordu. Görünmeyen sayfa ayrımı yapılmadan 8 denemeye kadar `Render` öncelikli toparlama çalışması başlatılabiliyordu.
3. Grid toparlama kodu kendi kontrolü yerine parent zincirinin tamamını measure/arrange için geçersiz kılıyordu. Birden çok gizli grid ile birlikte bu davranış UI thread işini katlıyordu.
4. `RemoteImage.CancelPendingLoad()` tüketicinin beklemesini durduruyor, ancak ortak HTTP indirme, response body kopyalama, retry beklemesi ve decode admission gerçek tüketici ömrüyle iptal edilmiyordu.
5. Görsel hattında indirme/decode concurrency sınırı bulunmasına rağmen farklı URL'ler için bekleyen iş sayısının ayrı bir üst sınırı yoktu. Menü dolaşımı ve hızlı scroll sırasında iş kuyruğu birikebiliyordu.

Bu zincir, uygulamanın önce normal çalışıp tekrar tekrar scroll/navigation/background-resume sonrasında giderek ağırlaşmasıyla uyumludur. Güçlü masaüstü donanımı aynı iş birikimini daha uzun süre maskeleyebilir.

## Uygulanan düzeltmeler

### Android yaşam döngüsü

- Android `OnPause` ortak mobil yaşam döngüsüne bağlandı.
- Foreground durumu atomik olarak takip ediliyor.
- Pause olayı devam eden grid resume toparlamalarını sürüm geçersizleştirmesiyle durduruyor.
- Android UI kuyruğunda bekleyen resume bildirimi generation ile korunuyor; daha sonra gelen pause durumunu geri çeviremiyor.

### Grid resume/layout kontrolü

- Toparlama yalnız foreground, visual tree'ye bağlı ve gerçekten görünür grid için çalışıyor.
- Deneme sayısı 8'den 3'e indirildi.
- Her deneme öncesinde görünürlük, foreground ve generation tekrar kontrol ediliyor.
- Parent visual tree boyunca invalidation kaldırıldı; yalnız grid'in kendi measure/arrange durumu geçersiz kılınıyor.
- `DispatcherPriority.Render` yerine `DispatcherPriority.Loaded` kullanılıyor.

### Sınırlı ve referans sayımlı görsel işleri

- Farklı cache anahtarları için eşzamanlı/bekleyen ortak iş sayısı 48 ile sınırlandı.
- Aynı görseli isteyen kartlar tek loader'ı paylaşmaya devam ediyor.
- Bir tüketicinin iptali diğer tüketicilerin ortak işini bozmaz.
- Son tüketici ayrıldığında alttaki ortak iş iptal edilir. Anahtar yeni tüketicilere kapatılır, fakat 48'lik kapasite slotu loader gerçekten sona erene kadar tutulur.
- Kapasite dolduğunda yeni farklı iş en fazla bir kez, 100 ms sonra ve yalnız kontrol hâlâ aktifse yeniden denenir.
- Active/running/queued sayımları ayrı tutulur; iptali geç işleyen loader'lar kapasiteyi aşamaz.

### Uçtan uca iptal ve güvenli UI commit

Cancellation token şu aşamalara taşındı:

- download semaphore admission,
- HTTP `SendAsync`,
- response stream açma,
- response body kopyalama,
- retry gecikmeleri,
- decode semaphore admission,
- decode öncesi/sonrası geçerlilik kontrolü.

Response body için mevcut 8 saniyelik süre sınırı tüketici token'ıyla birleştirildi. İptal sonrası oluşturulmuş bitmap cache'e veya UI'a yazılmadan önce tekrar kontrol ediliyor. Görsel UI commit'leri `Render` yerine `Loaded` önceliğinde yapılıyor.

### Yalnız aktif sayfanın çalışması

- Navigation sırasında önceki sayfadaki `RemoteImage` tüketicileri iptal ediliyor.
- Yeni sayfada yalnız görünür görseller başlatılıyor.
- Uygulama pause olduğunda aktif sayfanın görsel tüketicileri durduruluyor.
- Resume sonrasında yalnız mevcut sayfa, layout toparlandıktan sonra `Background` önceliğinde yeniden etkinleştiriliyor.
- Foreground, aktif yüzey, visual-root ve etkili görünürlük koşulları start, retry, cache ve UI commit aşamalarında kalıcı olarak tekrar kontrol ediliyor.
- Profil ekranı veya player ana içeriği örttüğünde alttaki görsel işleri durduruluyor; bu durum kökten miras alındığı için daha sonra realize edilen kartlar da pasif kalıyor. Geri dönüldüğünde yalnız mevcut sayfa yeniden etkinleştiriliyor.
- Yeni URL için cache miss veya kapasite reddi oluştuğunda eski kart görseli bırakılmıyor; placeholder korunuyor.
- Abonelikler normal visual-tree attach/detach ömrüne bağlı ve tekrar aboneliğe karşı idempotent.

### Telemetri

`PerformanceTrace` üzerinden, görüntü başına release log üretmeden aşağıdaki sayaçlar yayınlanıyor:

- `GridResumeRequested`, `GridResumeSkippedInactive`, `GridResumeAttempted`, `GridResumeCompleted`, `GridResumeGenerationCancelled`
- `ImageDistinctActive`, `ImageDistinctQueued`, `ImageConsumerCancelled`, `ImageUnderlyingCancelled`, `ImageOverflowRejected`, `ImageStaleCommitDropped`

## Rapor maddeleriyle durum eşlemesi

| Madde | Faz 1 durumu | Açıklama |
|---|---|---|
| P0-01 | Kısmi azaltım | Ağır sayfalar hâlâ oluşturuluyor; gizli sayfaların görsel işleri artık aktif tutulmuyor. |
| P0-02 | Kısmi azaltım | RemoteImage ve resume-grid işleri inactive-aware oldu; tüm view-model/timer kaynakları henüz kapsanmadı. |
| P0-03 | Uygulandı | Resume çalışması foreground/görünürlük/visual-root ile sınırlandı. |
| P0-04 | Uygulandı | Retry 8 → 3 ve her turda geçerlilik kontrolü. |
| P0-05 | Uygulandı | Parent-chain layout invalidation kaldırıldı. |
| P0-07 | Uygulandı | HTTP/body/retry/decode admission uçtan uca iptal edilebilir. |
| P0-08 | Uygulandı | 48 farklı iş kapasiteli ortak coordinator eklendi. |
| P1-07 | Büyük ölçüde uygulandı | İptal sonrası cache/UI commit engellendi. |
| P1-15 | Uygulandı | Görsel commit'lerinde Render önceliği kaldırıldı. |

P0-06, P0-09–P0-12 ve diğer P1 maddeleri bu fazda değiştirilmedi.

## Test ve derleme kanıtı

- Performans/stabilite odaklı test seçimi: **26/26 geçti**.
- Tam test takımı: **1.946 geçti, 6 başarısız, toplam 1.952**.
- Çalışma öncesi baz: **1.921 geçti, 6 başarısız, toplam 1.927**.
- Eklenen 25 yeni testten sonra başarısız test seti çalışma öncesindeki aynı 6 test olarak kaldı; yeni kalıcı regresyon yok.
- `Noctra.Mobile`: **0 hata**, 3 mevcut uyarı.
- `Noctra.Android`: **0 hata**, son doğrulamada 72 uyarı.
- `git diff --check`: **başarılı**.

Android build uyarıları arasında mevcut AndroidX sürüm kısıtı, nullable ve Android API seviye analizleri bulunuyor; bu fazın değişiklikleri derleme hatası üretmiyor.

## Açık kabul çalışması

Kod ve otomatik doğrulama tamamlandı; gerçek cihaz üzerinde uzun süreli davranış ölçümü bu ortamda yapılmadı. Aşağıdaki cihaz senaryosu Faz 1 kabulü için çalıştırılmalıdır:

1. Live, Movies, Series ve Search arasında en az 30 tur gezinme.
2. Her sayfada hızlı aşağı/yukarı scroll ve görsel yüklenirken sayfa değiştirme.
3. En az 20 kez background → foreground döngüsü.
4. Başlangıç, 10. ve 20. döngüde PSS/native heap, UI frame time, GC ve aktif HTTP iş sayısını kaydetme.
5. Son döngüde gezinme/scroll gecikmesinin başlangıca göre sürekli büyümediğini doğrulama.

## Sonraki faz önceliği

1. Ağır içerik sayfalarını lazy oluşturma ve inactive sayfaları yaşam döngüsüyle tamamen durdurma.
2. Navigation collection reset dalgalarını tek commit'e indirme.
3. Search fuzzy hesaplamasını generation/cancellation ve tek tarama mimarisine taşıma.
4. TMDB enrichment için uygulama-geneli limiter, navigation token ve bounded queue ekleme.
5. Android `OnTrimMemory` ile bitmap cache küçültme ve native-memory telemetrisi ekleme.
