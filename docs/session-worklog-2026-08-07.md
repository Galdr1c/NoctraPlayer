# Noctra — Oturum Çalışma Dokümanı (2026-08-07)

Bu doküman, bu oturumda baştan sona yapılan tüm çalışmaları kayıt altına alır: PIN sisteminin sadeleştirilmesi, Child Mode'un kaldırılması, masaüstü Downloads ekranının modernizasyonu, promo kod sisteminin sağlamlaştırılması, Google Play Billing / Premium altyapısının kurulması ve ödeme backend'inin stateless mimariye evrilmesi.

**Son durum:** Tüm projeler derleniyor (0 hata), testler yeşil (Noctra.Billing.Api.Tests: **36/36**, Noctra.Tests billing seti dahil geçer).

---

## 1. PIN Sistemi Sadeleştirmesi ve Güvenlik Düzeltmeleri

### 1.1 Mimari değişiklik: PBKDF2 → salted SHA-256 (Option A)

PIN akışı aşırı karmaşık PBKDF2 altyapısından arındırılarak Noctra'nın gerçek tehdit modeline uygun hale getirildi (aynı cihazdaki profile erişimi zorlaştıran basit kilit):

- **Yeni format:** `PIN2$<16-byte random salt>$<SHA-256 verifier>` — yalnızca 4 ASCII rakam kabul edilir, karşılaştırma sabit zamanlıdır (`FixedTimeEquals`), doğrulama sonrası bellekteki hassas veri sıfırlanır (`ZeroMemory`).
- **Yeni sınıflar:** `Noctra.Core/Services/ProfilePinVerifier.cs` + `IProfilePinService` / `ProfilePinService`.
- **`ISecurityService` sadeleştirildi:** tüm PIN kodu çıkarıldı; `Encrypt`/`Decrypt` (sağlayıcı parola koruması) ayrı tutuldu — PIN sadeleştirilirken gerçek hesap parolası koruması kaybedilmedi.
- **`PinEntryViewModel`:** doğrulama senkron hale getirildi (`IsVerifying`/`PinNeedsRehash` kaldırıldı), PBKDF2'nin neden olduğu son rakamdaki UI donması ortadan kalktı.

### 1.2 Servis katmanına taşınan erişim kontrolü

- **`ProfileAccessGrant`** oluşturuldu ve servis katmanında doğrulandı: PIN'li profil artık geçerli grant olmadan **yüklenemiyor, kaydedilemiyor, silinemiyor** — yanlış bağlanmış bir command veya yeni bir UI yolunun PIN kapısını atlaması zorlaştırıldı.
- **Kalıcı lockout:** `FailedPinAttempts` + `PinLockedUntilUtc` veritabanında saklanıyor (5 yanlış deneme → 30 saniye kilit); uygulama yeniden başlatılsa bile kilit devam ediyor, süre dolunca state temizleniyor.
- **Kapatılan mantık hataları:**
  - Premium süresi bitince mevcut PIN sessizce kaldırılmıyor.
  - Pending-deletion profil doğru PIN ile açıldığında veya düzenlenip kaydedildiğinde silme iptal ediliyor.
  - Zayıf PIN yasaklanmak yerine kullanıcıya uyarı gösteriliyor (yerelleştirilmiş uyarı + profil kurulumunda bildirim).
  - Profil seçme/düzenleme/silme için hızlı çift tıklama koruması (concurrency guard).
  - Lockout countdown task'ı PIN ekranı kapanınca iptal ediliyor (ViewModel `IDisposable`).
  - Mobil PIN alanları: erişilebilirlik metinleri + küçük ekran yerleşimi iyileştirildi.

### 1.3 Kod incelemesinden gelen düzeltmeler

| Bulgu | Çözüm |
|---|---|
| 🔴 Deneme sayacı yarış durumu — her başarısız deneme ayrı `DbContext` ile `read → +1 → write` yapıyordu; UI kilitleyebiliyor ama DB sayacı geride kalabiliyordu | `VerifyAttemptAsync(profileId, pin)` — profil bazlı `SemaphoreSlim` içinde atomik: lockout kontrolü → PIN doğrulama → başarıda sayaç sıfırlama / hata da artırma → `SaveChanges` tek çağrıda |
| 🔴 5 dakikalık grant, oturum içi yeniden yüklemeyi bozabiliyordu (`RefreshSelectedPlaylistAsync` → `LoadProfileAsync` grant süresi dolunca `ProfileAccessDeniedException`) | Kısa ömürlü grant (tek düzenleme/silme) ile aktif oturum kavramları ayrıldı; oturum içi reload tekrar PIN sormuyor |
| 🔴 Masaüstünde X ile kapatmak "PIN unuttum" sayılıyordu (`result == null` → `HandleForgotPin`) | Açık sonuç bayrağı: X / Escape / İptal = iptal; yalnızca "PIN'imi unuttum" butonu null sonucu üretiyor |
| 🔴 Eski PIN temizleme migration'ı hatayı sessizce yutuyordu (`TryExecuteUpdateAsync` tüm hataları yakalayıp 0 döndürüyordu) | Reset loglanıyor, başarısızlıkta tekrar deneniyor; migration gerçek format doğrulaması yapıyor (3 parça, salt 16 byte, hash 32 byte) — bozuk kayıtlar PIN2 prefix'ine bakılmaksızın sıfırlanıp bildirime ekleniyor |
| 🟡 Silme için yanlış purpose metni (`Delete` → `Purpose.Edit`) | `ProfileAccessPurpose.Delete` → `Profiles.Pin.Purpose.Delete`, `PinChange` → `Purpose.Edit` eşlemesi ayrıldı (tüm dillerde çeviri zaten vardı) |

---

## 2. Child Mode'un Kaldırılması

### 2.1 Karar

IPTV tarafındaki derecelendirme/kategori/metadata güvenilir olmadığı için keyword + metadata tabanlı Child Mode'un yanlış güvenlik vaadi verdiği değerlendirildi ve **tamamen kaldırılması** kararlaştırıldı. PIN'li standart profiller kaldı; "yalnızca çocuklara uygun içerik gösterir" iddiası kaldırıldı.

### 2.2 Faz 1 — Davranış kaldırma

- Profil oluşturma/düzenlemedeki "Çocuk Profili" seçeneği, child badge, renk ve açıklamalar kaldırıldı.
- Eski `IsChild=true` profiller normal profil olarak açılıyor; kullanıcıya bir defalık açıklama gösteriliyor.

### 2.3 Faz 2 — Kod temizliği

- **Silinen:** `ChildSafetyHelper`, `ApplyChildFilter`, `EnsureChildProfileCleanedAsync`, `PurgeNonCompliantSeriesAsync`, progressive Xtream/Stalker child filtre çağrıları, `IsChild` üzerinden çalışan ViewModel koşulları, child badge/avatar stilleri, child-mode çeviri anahtarları, child filtre testleri, reason/keyword listeleri.
- `ProfileSaveRequest.IsChild` alanı ve `ProfileService` içindeki okuması kaldırıldı (kolon yerinde kaldı — legacy migration işareti olarak açıklamasıyla).
- README ve dokümanlar güncellendi.

### 2.4 Migration: eski child profillerin silinmesi

Kullanıcı kararıyla eski child profiller standart profile çevrilmek yerine **siliniyor**; silme öncesi kullanıcıya `Profiles.Child.Notice.Title` bildirimi gösteriliyor (NeedRefresh gibi ek akışlara gerek duyulmadı).

Son incelemelerden gelen sağlamlaştırmalar:

| Bulgu | Çözüm |
|---|---|
| 🔴 Silme atomik değildi — her `ExecuteDeleteAsync` ayrı commit yapıyordu, ortada hata olursa yarım durum kalıyordu | Tüm DB işlemleri (EPG, series, import job, playlist, profil, provider) tek açık transaction içinde; dosya sistemi temizliği transaction dışında (SQLite write lock) ve önce çalışıyor — hata olursa profil korunuyor, migration sonraki açılışta tekrar deneniyor |
| 🔴 Bildirim kaybolabiliyordu — DB silme başarılı ama sonraki cleanup hata verirse notice flag yazılmıyordu | Silme sonucu alınır alınmaz notice bayrağı atomik kaydediliyor; cleanup hataları genel sonucu bozmuyor, loglanıp sonraki başlangıçta tekrarlanıyor |
| 🔴 Global orphan-provider temizliği ilgisiz hesapları da silebiliyordu | Temizlik yalnızca silinen child profillerin `ProviderAccountId` adaylarıyla sınırlandı (başka profil tarafından kullanılmıyorsa); GC gibi davranmıyor |
| 🟡 ImportJobs orphan kalabiliyordu | Silinen profile/playlistine ait import job kayıtları temizleniyor |
| 🟡 Mobil başlangıçta iki bakım işlemi yarışıyordu (`EnsureServices` fire-and-forget purge + arka plan `DeleteChildProfilesAsync`) | Mobilde tek bakım zinciri: initialize → schema fixup → expired purge → child migration; `EnsureServices` içindeki bağımsız fire-and-forget purge kaldırıldı |
| Test | Kolon düşürme migration testi + idempotency/hatalı yollar/paralellik senaryoları |

---

## 3. Masaüstü Downloads Ekranı Modernizasyonu

`Noctra.Avalonia/Views/DownloadsView.axaml`, mobildeki bilgi hiyerarşisine göre yenilendi:

- **Sheet sistemine geçiş:** eskimiş `ComboBox` kaldırıldı; sıralama, `DesktopIconButton` + `DesktopSelectionSheet` (`SelectionSheetHost`, ZIndex 40000) ile yapılıyor — masaüstündeki tüm ekranlarla tutarlı.
- **`Downloads.Action.OpenFolder` butonu korundu.**
- **LiveView pattern:** `SelectedDownloadSortOrder` `PropertyChanged` observer'ı (LiveView stili), `OpenDownloadsFolder`, dispose pattern.
- **Kart düzeni:** kartın tüm gövdesi tıklanabilir, çöp kutusu ayrı hit target; depolama kartı, aktif indirmeler, kuyruk ve hata durumları daha açık.
- Mobil taraf (MobileDownloadsView) aynı yenilenmede sadeleştirildi: failed indirmeler için hata kartı + yeniden dene/sil eylemleri, live-duplicate kaydın unique index'i bozmaması engellendi.

---

## 4. Promo Kod Sistemi Sağlamlaştırılması

### 4.1 Config doğrulaması ve hata taksonomisi

- **Yük doğrulaması:** 256 KB response sınırı (`Content-Length` + sınırlı akış okuma), `text/html` reddi, opsiyonel `schemaVersion` (yalnızca v1), boş kodlu veya normalize edilmiş kodu çift olan config tamamen reddediliyor (ör. `AB CD` vs `ABCD`) — JSON sırası artık davranışı belirlemiyor.
- **Hata sınıfları:** `Offline`, `Timeout`, `ServiceUnavailable`, `ConfigurationInvalid` — her durum için yerelleştirilmiş mesaj; "internet bağlantınızı kontrol edin" yanlış yönlendirmesi ve geliştiriciye dönük "URL yönetici tarafından yapılandırılmalı" mesajı kaldırıldı.
- **ViewModel güvenliği:** beklenmeyen exception loglanıyor, kullanıcıya yalnızca yerelleştirilmiş genel mesaj gösteriliyor (teknik `exception.Message` sızması engellendi).
- **Bilinen sınırlar (bilinçli):** `no-cache` (anında güncelleme) ETag yeniden kullanımıyla çelişiyor; imza yok — ikisi de server-side redemption akışına ertelendi.

### 4.2 UI/UX düzeltmeleri

| # | Bulgu | Çözüm |
|---|---|---|
| 13 | Kalıcı Premium'da promo kartı hâlâ görünüyordu | `CanUsePromoCodes => !IsEditionLockedPremium` — kalıcı Premium'da kart gizleniyor; süreli Premium'da kart kalıyor, buton "Süre Ekle" oluyor |
| 15 | Yeni kod yazılırken eski kırmızı hata metni ekranda kalıyordu | `OnPromoCodeInputChanged` — input değişince status temizleniyor (işlem sürmüyorsa) |
| 16 | İşlem sürerken input düzenlenebiliyordu | Form + TextBox işlem sırasında birlikte devre dışı kalıyor |
| 17 | Mobil klavyedeki "Bitti" kodu uygulamıyordu | IME action doğrudan command'i tetikliyor |
| 18 | Mobil loading ikonu animasyonlu değildi | Global `MaterialIcon.Rotating` stili (startup'ı kıran risk) kaldırıldı; `Animation="Spin"` (Material.Icons.Avalonia 3.0.2 yerleşik) kullanıldı — masaüstünde de aynı düzeltme |
| 19 | Sonuç yalnız renkle anlatılıyordu | ✓/⚠ ikonlar + erişilebilir durum metni + **ekran okuyucu live announcement** (`AutomationProperties.LiveSetting`) eklendi |
| 20 | Kod giriş alanı mobil için yapılandırılmamıştı | `MaxLength: 64`, otomatik düzeltme/kapitalizasyon ayarları, paste destekli, baş/son boşluklar otomatik temizleniyor |
| 21 | Tarih bütün dillerde Türkçe sabit formattı | Kültüre duyarlı format (`"g"`) + yanında kalan süre gösterimi |

---

## 5. Google Play Billing / Premium Sistemi

### 5.1 Ürün modeli

- **Aylık abonelik** (otomatik yenilenen) + **tek seferlik kalıcı paket** — her ikisi de Play Console'da ürün oluşturmayı gerektiriyor (`noctra_premium_monthly`, `noctra_premium_lifetime`).
- Premium kaynağı ayrıştırıldı: `PremiumSource` (`GooglePlayLifetime`, `GooglePlaySubscription`, `Promo`, `PremiumEdition`); `IsTrialPeriod` yalnızca **gerçek trial** için true (ücretli aylık kullanıcı "trial" görünemez).
- Lifetime öncelikli; aksi halde abonelik/promo süresinin geç olanı geçerli.

### 5.2 Android client (AndroidStorePurchaseService)

- BillingClient v9 binding (JNI reflection ile `getSubscriptionOfferDetails` dahil) — aylık/lifetime ayrımı, fiyat gösterimi ve purchase flow aynı offer token'ıyla tutarlı.
- **Offer seçimi** `FirstOrDefault` değil: `offerId`/offer tag + tüm pricing phase'ler (`recurrenceMode`) okunur; trial/intro fiyatı aylık fiyat olarak gösterilemez; gösterilen fiyat her zaman recurring phase'ten gelir.
- **Pending purchase:** `PurchaseState.Pending` Premium vermez; "ödeme bekleniyor" mesajı gösterilir, resume'da yeniden sorgulanır.
- **Acknowledge:**
  - Sonuç değerlendiriliyor (non-OK response sessiz geçmiyor), transient hatada **1 kısa retry** (1.5 sn), sonraki sorgu otomatik yeniden dener (`IsAcknowledged=false` korunur) — Play'in 3 günlük otomatik refund'u riski azaltıldı.
  - **Restore yolunda da lifetime kayıtları tek tek acknowledge ediliyor** (kaçırılan callback / başka cihaz / app kill senaryoları).
- **Geçici Play hatası downgrade üretemez:** query `Success` değilse son doğrulanmış entitlement cache'i korunuyor (boş liste "satın alma yok" sanılmıyor).
- **Cold start:** son server-verified entitlement senkron yükleniyor → Free→Premium UI sıçraması kalktı; cache gerçek Play `expiryTime`'ına uyuyor.
- **Resume/focus:** `RefreshSubscriptionStatusAsync` artık gerçek store sorgusu yapıyor; eşzamanlı refresh'ler lock ile serileştiriliyor; `SubscriptionChanged` UI thread'e dispatcher üzerinden taşınıyor.
- **Expiry timer:** Premium tam bitiş anında timer tetiklenip UI yeniliyor; uzun süreler periyodik yeniden kuruluyor (sistem saati değişimi/uzun sleep yakalanır).
- **Mobil upsell UX:** genel "Satın Al" butonu kaldırıldı (yalnız iki plan kartı CTA); cross-plan fallback yok (lifetime ürün yoksa monthly açılmıyor, plan-unavailable mesajı); sheet yalnızca gerçek satın alma tamamlandığında kapanıyor; satın alma sırasında butonlar disable + spinner.
- **Lifetime + aktif aylık:** lifetime satın alan kullanıcıya aylığı Play abonelik yönetiminden iptal etmesi hatırlatılıyor (uygulama iptal saymıyor).
- **Diag servisi güncellendi:** `DiagnosticLicenseReport` — Premium Source, Expires (UTC), Trial, Pending Purchase, App Build, Locale, Report Generated.

### 5.3 Önemli düzeltmeler (inceleme turlarından)

| Bulgu | Çözüm |
|---|---|
| 🔴 Abonelik bitişi client'ta tahmin ediliyordu (purchaseTime + period; `max(now+period)` aşırı grant) | Bitiş backend'den gerçek `lineItems.expiryTime` ile geliyor; client tahmini tamamen kaldırıldı |
| 🔴 `LaunchBillingFlow == OK` "satın alma tamamlandı" sayılıyordu | OK yalnızca ekranın açıldığı anlamına gelir; hak yalnızca doğrulanmış satın alma sonucundan sonra açılıyor |
| 🔴 `IsAutoRenewing` "abonelik aktif mi" sanılıyordu | Aktiflik sorusunun cevabı değil; iptal edilmiş abonelik expiry'ye kadar aktif kalabilir |

---

## 6. Billing Backend: RTDN'dan Stateless'e Evrim

### 6.1 İlk aşama (stateful)

`Noctra.Billing.Api` (ASP.NET Core Minimal API) önce SQLite `EntitlementStore` + RTDN (Pub/Sub) + OIDC doğrulaması içeriyordu: token→installation mapping, `/billing/entitlement`, subscription/one-time/voided RTDN modelleri.

Bu aşamada yapılan doğru düzeltmeler:

- **Lifetime `purchaseState` düzeltmesi:** `purchases.products.get`'te `0=Purchased, 1=Canceled, 2=Pending` (client enum ile karıştırılmıştı — gerçek lifetime reddediliyor, iptal edilmiş olan Premium veriyordu).
- **`autoRenewing` doğru yerden:** root değil `lineItems[].autoRenewingPlan.autoRenewEnabled`.
- **`expiryTime` yalnız eşleşen line item'dan** — başka ürünün line item'ı expiry'ye katkıda bulunamaz.
- **`PAUSED` ve `ON_HOLD` Premium vermiyor;** `CANCELED` gerçek expiry'ye kadar koruyor; `ACCOUNT_HOLD` güncel enum değil (`ON_HOLD`).
- **Trial tespiti tek istekle:** `lineItems[].offerPhase.freeTrial`'ın varlığı = kullanıcı free trial fazında; gereksiz Monetization API sorgusu kaldırıldı.
- **RTDN düzeltmeleri:** `subscriptionId` Google tarafından deprecated edildiği için `ReverifyByTokenAsync(purchaseToken)` — productId store satırlarından türetiliyor; OIDC'ye `email` + `email_verified` claim kontrolü; deploy fail-fast; `voidedPurchaseNotification`; `packageName` doğrulaması; one-time notificationType yorumları düzeltildi.

### 6.2 Kullanıcı kararı: stateless + ADC

İnceleme sonrası kullanıcı iki öneriyi onayladı:

> **Stateless'e geç** + **ADC'ye geç**

Gerekçe: Noctra'da merkezi kullanıcı hesabı yok; hakkın kaynağı Google'ın kendisi; client zaten her açılışta token gönderiyor. DB + RTDN ikinci bir 7/24 state kopyası tutuyordu — karmaşıklık değerden büyüktü.

**Silinen (~2200 satır):** `EntitlementStore` (SQLite + 2 NuGet paketi), `GET /billing/entitlement`, RTDN endpoint'i + `OidcTokenVerifier`, tüm RTDN modelleri, token→installation mapping, multi-device update mantığı, Secret Manager + `NOCTRA_GOOGLE_CREDENTIALS_JSON` + manuel RS256 JWT (`LoadCredentials`/`SignAssertion`).

**Kalan mimari:**

```
Noctra Android ──purchaseToken──▶ Cloud Run (Noctra.Billing.Api, stateless)
                                      │  ADC / service identity
                                      ▼
                              Google Play Developer API
                                      │
                                      ▼
                              Doğrulanmış entitlement → Android local verified cache
```

- **Tek uç nokta:** `POST /billing/google/verify` (+ `/health`).
- **ADC:** `GoogleCredential.GetApplicationDefault().CreateScoped(androidpublisher)` — Cloud Run'da bağlı service account (`noctra-billing-runtime@…`) metadata'dan token veriyor; local geliştirmede `gcloud auth application-default login`. Startup'ta ADC fail-fast (yanlış yapılandırma ilk istekte 502 değil, deploy anında görünür).
- **Client sıfır değişiklik:** `/billing/entitlement` hiç kullanılmıyordu; iptal/refund uygulama bir sonraki açılışında yansıyor.

### 6.3 Deploy ve hardening (son tur)

| Konu | Çözüm |
|---|---|
| 🔴 `gcloud run deploy`'da `--dockerfile` argümanı yok | Deploy scriptleri `--source Noctra.Billing.Api`'ye geçti; Dockerfile kendi klasörünü build context kabul ediyor (`COPY Noctra.Billing.Api.csproj .` / `COPY . .`); `artifactregistry.googleapis.com` açıkça etkinleştirildi; `.dockerignore` eklendi (bin/obj dışarıda) |
| 🧹 `InstallationId` artığı | Backend request + validasyon, `AppSettings.InstallationId`, `SettingsService` satırları, Android `EnsureInstallationIdAsync` + kullanılmayan `ISettingsService` ctor/DI bağımlılığı, testler, smoke-test — uçtan uca kaldırıldı; request `{ packageName, productId, purchaseToken }` |
| 🛡️ Product ID overlap | Aynı ID hem subscription hem lifetime listesindeyse startup'ta `InvalidOperationException` (testli) |
| 🛡️ Rate limit | IP başına 30 req/dk, 429; bölüm anahtarı **`X-Forwarded-For`'un son değeri** (`ClientIpResolver`) — Cloud Run'da `RemoteIpAddress` proxy'ye ait; ilk değer sahtelenebilir, GFE gerçek IP'yi zincirin sonuna ekler (6 test) |
| 🛡️ Token uzunluk | `purchaseToken` 10–4096 aralığı dışında Google çağrısı öncesi red |
| 📝 Eski yorumlar | Trial tespiti `offerPhase.freeTrial` olarak güncellendi (Contracts + IBillingVerificationClient) |
| ✅ Play API hata testleri | 401/403/429/500/503 testleri; hatalar 502 olarak yansıyor, client son doğrulanmış entitlement'ı koruyor |

### 6.4 Deploy / kurulum dokümanları

- `docs/play-billing-verification.md` — stateless + ADC mimarisine göre yeniden yazıldı.
- `docs/play-console-setup-checklist.md` — Play Console'da yapılacak tek seferlik adımlar.
- `deploy-billing.sh` / `deploy-billing.ps1` — Cloud Run'a deploy, service account oluşturma + bağlama, `.env`'e `NOCTRA_BILLING_VERIFY_URL` yazma.
- `smoke-test-billing.sh` — deploy sonrası endpoint doğrulaması.

**Kullanıcıya kalan:** service account e-postasına (`noctra-billing-runtime@<PROJEKT>.iam.gserviceaccount.com`) Play Console → Setup → API access'te izin vermek; Cloud Run için billing hesabı (ücretsiz katman: aylık 2M request, 180K vCPU-sn, 360K GiB-sn RAM; `--min-instances 0 --max-instances 2 --memory 256Mi`).

---

## 7. Test ve Doğrulama Durumu

- `Noctra.Billing.Api.Tests`: **36/36** geçti — Play şema testleri (gerçek Google JSON şekilleri: canceled/paused/on-hold/trial/lifetime state), token uzunluk, overlap config, ClientIpResolver, Play API hata kodları, ADC token header, BillingConfig fail-fast.
- `Noctra.Tests`: PIN (PinSystemScenarios, ProfileAccessGrant, CriticalApplicationScenarios, DatabaseSchemaFixup, PlaylistServiceIntegration) + billing seti (BillingVerificationClient, StoreEntitlement, LicenseService) dahil geçer.
- Build: Noctra.Core / Noctra.Android / Noctra.Billing.Api / Noctra.Billing.Api.Tests — 0 hata.
- Script syntax: `bash -n` deploy + smoke OK.
- Kod incelemeleri: her tur sonunda `code-reviewer-deepseek-flash` ile; yakalanan gerçek sorunlar (ör. rate limit bölüm anahtarı, retired ürün guard'ı, XFF semantiği) düzeltildi ve testlerle sabitlendi.

---

## 8. Bilinçli Bırakılan Noktalar

- **Promo:** no-cache vs ETag çelişkisi + imza eksikliği — server-side redemption akışına ertelendi.
- **EPG:** global cache olarak kalması bilinçli; silinen playlist EPG'lerinin yaşam döngüsü net tanımlı değil (bug sayılmıyor).
- **`Profile.IsChild` kolonu:** legacy migration işareti olarak bir-iki sürüm daha kalacak; child-silme migration'ı gereken uyum penceresinde kaldıktan sonra kaldırılacak.
- **RTDN:** kaldırıldı — iptal/refund anlık değil, uygulama açılışında yansıyor (kullanıcı hesabı olmayan ürün için doğru trade-off).
- **Cloud Run maliyeti:** ücretsiz kotada kalmak çok olası ancak garantili değil; bütçe alarmı önerildi.
