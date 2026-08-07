# Play Billing Backend Doğrulaması (Noctra.Billing.Api)

Google Play abonelik bitişi istemcide **hesaplanmaz**. Gerçek bitiş yalnızca
Play Developer API'den gelir: `purchases.subscriptionsv2.get` →
`lineItems.expiryTime`. Bu API, `androidpublisher` OAuth yetkisi ister ve
service account kimliği **yalnızca sunucu ortamında** tutulmalıdır (APK'ya
konursa tersine mühendislikle ele geçirilir).

Mimari:

```
Android (BillingClient)
   │  purchaseToken
   ▼
Noctra.Billing.Api (senin backend'in)      ← service account burada
   │  subscriptionsv2.get / products.get
   ▼
Google Play Developer API
```

## Bu repo'da neler var

### Backend — `Noctra.Billing.Api` (ASP.NET Core Minimal API, .NET 8)

| Uç nokta | Açıklama |
|---|---|
| `POST /billing/google/verify` | Purchase token'ı Play'de doğrular, package/product'i **kendi yapılandırmasıyla** doğrular, gerçek `expiryTime` değerini kaydeder ve normalize sonucu döner. |
| `GET /billing/entitlement?installationId=` | Kurulum başına kayıtlı hakların listesi (izleme/hata ayıklama). |
| `POST /billing/google/rtdn` | (Opsiyonel) Google Play RTDN Pub/Sub push mesajı; token'ı çıkarıp son durumu tekrar sorgular. |
| `GET /health` | Canlılık. |

Doğrulama cevabı (client'ın uyguladığı tek şey):

```json
{
  "isActive": true,
  "entitlementType": "Subscription",
  "expiresAtUtc": "2026-09-06T15:42:10Z",
  "autoRenewEnabled": true,
  "state": "SUBSCRIPTION_STATE_ACTIVE",
  "verifiedAtUtc": "2026-08-06T17:05:00Z"
}
```

- **Abonelik durumları:** `ACTIVE`, `IN_GRACE_PERIOD`, `ACCOUNT_HOLD`,
  `ON_HOLD`, `PAUSED` ve `CANCELED` (ödenen süre bitene kadar) hak verir;
  `EXPIRED` ve `PENDING` vermez. Bitiş her zaman Play'in `expiryTime`'ıdır.
- **Tek seferlik paket:** `purchases.products.get` (`purchaseState==1`).
- Veritabanı: sıfır yapılandırmalı SQLite (`entitlements` tablosu). Ham token
  saklanmaz, yalnızca SHA-256 özeti tutulur.

### Android client

- `AndroidStorePurchaseService.GetEntitlementAsync` artık satın alma
  token'larını backend'e gönderir; süre **hiçbir şekilde** istemcide
  hesaplanmaz (`ComputeSubscriptionEnd` kaldırıldı).
- Doğrulama hizmetine ulaşılamazsa (`IsVerified=false`) son bilinen
  doğrulanmış hak (`AppSettings.StoreVerifiedEntitlementJson` önbelleği)
  kullanılır — hak asla erken düşmez.
- Anonim `InstallationId` ilk kullanımda üretilip global ayarlara yazılır.
- Backend adresi ve opsiyonel anahtar, promosyon uç noktasıyla aynı modelle
  **build-time** gömülür (aşağıya bakın).

## Kurulum

### 1. Play Console

1. **Google Play Console** → uygulaman → **Monetize → Products** altında
   ürünleri oluştur: `noctra_premium_monthly` (subscription, auto-renewing,
   aylık) ve `noctra_premium_lifetime` (one-time, non-consumable).
2. **Setup → API access**: "Google Play Developer API"yi etkinleştir.
3. Aynı sayfada bir **service account** oluştur (Google Cloud Console'a
   yönlendirir) ve **JSON anahtarını indir**.
4. Play Console'a dönüp service account'a şu izinleri ver:
   - **View financial data, orders and cancellation survey responses**
   - **Manage orders and subscriptions** (acknowledge için)

### 2. Backend'i dağıt

**Ücretsiz barındırma önerisi: Google Cloud Run ücretsiz katmanı**
(C#/.NET için en doğal; düşük trafikte maliyeti $0, ileride RTDN/Pub/Sub ile
doğal bütünleşir):

```bash
# Service account JSON'unu Secret Manager'a koy, sonra:
gcloud run deploy noctra-billing-api \
  --source . \
  --region europe-west1 \
  --allow-unauthenticated \
  --set-env-vars="NOCTRA_PACKAGE_NAME=studio.kynora.noctra,NOCTRA_SUBSCRIPTION_PRODUCT_IDS=noctra_premium_monthly,NOCTRA_LIFETIME_PRODUCT_IDS=noctra_premium_lifetime"
```

Alternatifler: **Render ücretsiz katmanı** (billing kartı istemez; cold start
var, uykuya dalar), Oracle Cloud Always Free VPS, veya ~5$/ay bir VPS
(Hetzner/Contabo). Vercel/Netlify ASP.NET Core backend için **uygun değildir**.

### 3. Ortam değişkenleri (backend)

| Değişken | Gerekli | Açıklama |
|---|---|---|
| `NOCTRA_PACKAGE_NAME` | ✅ | Uygulama paket adı (örn. `studio.kynora.noctra`). |
| `NOCTRA_SUBSCRIPTION_PRODUCT_IDS` | ✅ | Virgülle ayrılmış abonelik ürün kimlikleri. |
| `NOCTRA_LIFETIME_PRODUCT_IDS` | ✅ | Virgülle ayrılmış tek seferlik ürün kimlikleri. |
| `NOCTRA_GOOGLE_CREDENTIALS_JSON` | ⬜ | Service account JSON içeriği (Cloud Run secret önerilir). |
| `GOOGLE_APPLICATION_CREDENTIALS` | ⬜ | Ya da JSON dosyasının yolu. İkisinden biri zorunlu. |
| `NOCTRA_BILLING_API_KEY` | ⬜ | Opsiyonel paylaşılan sır; ayarlanırsa client `X-Noctra-Billing-Key` başlığı gönderir, backend eşleşmezse 401 döner. |
| `NOCTRA_BILLING_DB_PATH` | ⬜ | SQLite dosya yolu (varsayılan: `noctra-billing.db`). |

### 4. Client tarafı (build-time yapılandırma)

Backend adresi **kaynak kodda tutulmaz** — promosyon uç noktasıyla aynı model:

```bash
# Build makinesinde / .env içinde (Release build AssemblyMetadata'e gömülür)
NOCTRA_BILLING_VERIFY_URL=https://noctra-billing-api-xxxx.run.app
NOCTRA_BILLING_API_KEY=<opsiyonel paylaşılan sır>
```

- **Release**: `Noctra.Core.csproj` bu değerleri `Noctra.BillingVerifyUrl` /
  `Noctra.BillingApiKey` metadata'sı olarak gömer. Kullanıcı ayar dosyasından
  değiştirilemez (kullanıcı kendi sunucusunu işaret edip hak üretemesin diye).
- **DEBUG**: yalnızca ortam değişkeninden okunur (`.env` yeterli).

## Test etme

- Backend: `dotnet test Noctra.Billing.Api.Tests/Noctra.Billing.Api.Tests.csproj`
  (Play yanıtı ayrıştırma, SQLite depo, doğrulama akışı; sahte HTTP handler'ları).
- Client: `BillingVerificationClientTests`, `StoreEntitlementFallbackTests`,
  `StoreEntitlementTests` (Noctra.Tests).
- Canlı test: Play Console → **License testing** → test cihazlarını ekle ve
  satın alma test token'larıyla `POST /billing/google/verify`'u dene.

## RTDN (opsiyonel, canlı için önerilir)

RTDN olmadan: satın alma tamamlanınca + uygulama açılışında + resume'da
doğrulama yapılır — kullanıcı uygulamayı açmıyorken iptal/refund/hold
güncellenmez. RTDN ile:

1. Play Console → **Monetize → Subscriptions** → RTDN'yi aç, Pub/Sub topic
   oluştur (Cloud Pub/Sub ücretsiz katman: ilk 10 GB/ay).
2. `POST /billing/google/rtdn` uç noktasını push aboneliği olarak ekle.
3. Backend, bildirimdeki token'ı `subscriptionsv2.get` ile yeniden sorgular
   (endpoint bu iş için hazır; token daha önce doğrulanmışsa kaydı günceller).

## Güvenlik notları

- Service account JSON'i yalnızca sunucuda; asla APK'da değil.
- Client'ın gönderdiği `packageName`/`productId`'ye güvenilmez — backend kendi
  sabit yapılandırmasıyla doğrular. Abonelikte ek güvence: `subscriptionsv2.get`
  URL'inde ürün yalnızca token olduğu için istemci premium productId ile başka
  bir aboneliğin token'ını gönderemez — Google'ın cevabındaki
  `lineItems[].productId` istenen ürünle eşleşmezse fail-closed inaktif döner.
- Play API hatası (401/403/5xx) → backend 502 döner; client son bilinen
  doğrulanmış hakkı kullanmaya devam eder. Token geçersizse Play 404 döner →
  fail-closed inaktif sonuç.
- **Acknowledge**: Satın alma, BillingClient üzerinden **client tarafında**
  acknowledge edilir (3 gün içinde yapılmazsa Play otomatik iade eder). Backend
  bunu gerektirmez; yalnızca entitlement'ı doğrular.
- **Token bağlama**: Backend token'ları kuruluma bağlamaz; aynı Google hesabı
  yeni cihazda satın alımı geri yüklediğinde aynı token yeni `InstallationId`
  ile yeniden doğrulanabilir (legit restore). Ham token saklanmaz, yalnızca
  SHA-256 özeti tutulur.
- **RTDN uç noktası auth**: `POST /billing/google/rtdn` yalnızca mevcut
  token'ları yeniden doğrulatır ve daima ack döner; bilinmeyen token işlenmez
  (fail-closed). Üretimde Pub/Sub push mesajının OIDC token'ı Google'ın açık
  anahtarlarıyla doğrulanmalıdır — kötü niyetli istek en fazla gereksiz bir
  Play sorgusu tetikler, hak veremez.
- **SQLite**: `PRAGMA journal_mode=WAL` + `busy_timeout=5000` etkin;
  verify + RTDN eşzamanlı yazmalarında kilitlenme yerine kısa bekleme olur.
