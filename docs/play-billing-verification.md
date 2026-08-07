# Play Billing Backend Doğrulaması (Noctra.Billing.Api)

Google Play abonelik bitişi istemcide **hesaplanmaz**. Gerçek bitiş yalnızca
Play Developer API'den gelir: `purchases.subscriptionsv2.get` →
`lineItems.expiryTime`. Bu API, `androidpublisher` OAuth yetkisi ister ve
erişim yalnızca **sunucu tarafında** yapılmalıdır (APK'ya konursa tersine
mühendislikle kötüye kullanılabilir).

Mimari — backend **stateless**'tir:

```
Android (BillingClient)
   │  purchaseToken (her açılış / resume / satın alma)
   ▼
Noctra.Billing.Api (Cloud Run, stateless)
   │  ADC (service identity) → subscriptionsv2.get / products.get
   ▼
Google Play Developer API (gerçek state'in kaynağı)
```

## Neden stateless?

Noctra'nın merkezi kullanıcı hesabı yoktur; entitlement'ın gerçek kaynağı
Google'dır. Client her açılışta/resume'da token'ı backend'e gönderdiği için
backend'in **kalıcı veritabanına ihtiyacı yoktur**:

- ❌ SQLite / Firestore / Supabase / Upstash → gerek yok
- ❌ RTDN (Pub/Sub) → gerek yok; kullanıcı uygulamayı açtığında doğrulama
  her zaman güncel state'i çeker
- ❌ token → installation mapping → gerek yok
- ✅ Backend tek iş yapar: **token'ı Play'de doğrula ve döndür**

Bu, Cloud Run'da sıfır yapılandırma, sıfır kalıcı depolama bağımlılığı ve
ölçekleme derdi olmayan bir dağıtım sağlar.

## Bu repo'da neler var

### Backend — `Noctra.Billing.Api` (ASP.NET Core Minimal API, .NET 8)

| Uç nokta | Açıklama |
|---|---|
| `POST /billing/google/verify` | Purchase token'ı Play'de doğrular, package/product'i **kendi yapılandırmasıyla** doğrular, gerçek `expiryTime` değerini döner. |
| `GET /health` | Canlılık. |

Doğrulama cevabı (client'ın uyguladığı tek şey):

```json
{
  "isActive": true,
  "entitlementType": "Subscription",
  "expiresAtUtc": "2026-09-06T15:42:10Z",
  "autoRenewEnabled": true,
  "state": "SUBSCRIPTION_STATE_ACTIVE",
  "isTrialPeriod": false,
  "verifiedAtUtc": "2026-08-06T17:05:00Z"
}
```

- **Abonelik durumları (Google'ın gerçek kuralları):**
  - `ACTIVE` → hak verir
  - `IN_GRACE_PERIOD` → hak verir (ödeme gecikti, avantaj sürer)
  - `CANCELED` → ödenen sürenin (expiryTime) sonuna kadar hak verir
  - `PAUSED` / `ON_HOLD` → **hak vermez** (Google erişimi kaldırır)
  - `EXPIRED` / `PENDING` / bilinmeyen → hak vermez
- **Tek seferlik paket:** `purchases.products.get` — `purchaseState`:
  `0 = Purchased`, `1 = Canceled`, `2 = Pending` (BillingClient'ın client-side
  enum'u değildir).
- **Trial tespiti:** `subscriptionsv2.get` → `lineItems[].offerPhase` —
  `freeTrial` alanının varlığı kullanıcının şu an trial fazında olduğunu
  söyler. Ek Monetization API isteği yoktur.

### Kimlik doğrulama — ADC (Application Default Credentials)

Private key **yok**. Cloud Run'a bağlanan service account (service identity)
metadata sunucusundan otomatik token alır:

```
Cloud Run → noctra-billing-runtime@PROJECT.iam.gserviceaccount.com
              ↓ ADC
           Google Play Developer API
```

- Secret Manager'a JSON yüklemek yok, manuel RS256 JWT yok, key rotation yok.
- Yerel geliştirmede: `gcloud auth application-default login` + aynı service
  account'u kullanmak (veya kendi JSON'unu `GOOGLE_APPLICATION_CREDENTIALS`
  ile vermek — production'da gerekmez).

### Android client

- `AndroidStorePurchaseService.GetEntitlementAsync` satın alma token'larını
  backend'e gönderir; süre **hiçbir şekilde** istemcide hesaplanmaz
  (`ComputeSubscriptionEnd` kaldırıldı).
- Doğrulama hizmetine ulaşılamazsa son bilinen doğrulanmış hak
  (`AppSettings.StoreVerifiedEntitlementJson` önbelleği) kullanılır — hak
  asla erken düşmez.
- Backend adresi ve opsiyonel anahtar, promosyon uç noktasıyla aynı modelle
  **build-time** gömülür (aşağıya bakın).

## Kurulum

### 1. Play Console

1. **Google Play Console** → uygulaman → **Monetize → Products** altında
   ürünleri oluştur: `noctra_premium_monthly` (subscription, auto-renewing,
   aylık) ve `noctra_premium_lifetime` (one-time, non-consumable).
2. **Setup → API access**: "Google Play Developer API"yi etkinleştir.
3. **Setup → API access** sayfasından service account'a şu izinleri ver:
   - **View financial data, orders and cancellation survey responses**
   - **Manage orders and subscriptions** (acknowledge için)

> Not: Play API erişimi için service account'un e-postası
> (`noctra-billing-runtime@PROJECT.iam.gserviceaccount.com`) Play Console'da
> yetkilendirilir. Bu service account'un private key'ini indirmek gerekmez —
> Cloud Run'da ADC kullanılır.

### 2. Backend'i dağıt

**Öneri: Google Cloud Run ücretsiz (Always Free) katmanı** — .NET için en
doğal, düşük trafikte maliyeti **$0** (ayda 2 milyon istek + 180K vCPU-sn +
360K GiB-sn ücretsiz; arka planda idle kalınca 0 örnek).

⚠️ **Bölge önemli**: ücretsiz katman yalnızca `us-central1`, `us-east1` ve
`us-west1` bölgelerinde uygulanır. Bu yüzden `us-central1` kullanılır.

```bash
bash deploy-billing.sh        # Linux / macOS
.\deploy-billing.ps1          # Windows
```

Script şunları yapar: projeyi seçer/oluşturur, `noctra-billing-runtime`
service account'unu oluşturur (yoksa), Cloud Run'a **service identity
olarak bağlar**, env değişkenlerini set eder ve çıkan URL'yi `.env`'e yazar.

Elle eşdeğeri:

```bash
gcloud run deploy noctra-billing-api \
  --source . \
  --dockerfile Noctra.Billing.Api/Dockerfile \
  --region us-central1 \
  --allow-unauthenticated \
  --service-account noctra-billing-runtime@PROJECT.iam.gserviceaccount.com \
  --set-env-vars="NOCTRA_PACKAGE_NAME=studio.kynora.noctra,NOCTRA_SUBSCRIPTION_PRODUCT_IDS=noctra_premium_monthly,NOCTRA_LIFETIME_PRODUCT_IDS=noctra_premium_lifetime" \
  --min-instances 0 --max-instances 2 --memory 256Mi --cpu 1
```

Stateless olduğu için ölçeklendirme/veri kaybı derdi yoktur: her istek
bağımsızdır, instance kapanabilir.

### Neden Firebase / Supabase / Upstash değil?

Backend **stateless** olduğu için **hiçbir veri servisine ihtiyaç yoktur**:

| Platform | Runtime | Bu backend için? |
|---|---|---|
| Firebase Cloud Functions | Node.js / Python / Go | ❌ .NET çalıştıramaz + veri servisi gerekmiyor |
| Supabase | Deno / Postgres | ❌ .NET çalıştıramaz; free projeler inactivity'de pause olur |
| Upstash | Redis (compute değil) | ❌ Backend'in kalıcı verisi yok |
| Vercel / Netlify | Frontend + serverless | ❌ backend-only .NET için uygun değil |
| **Google Cloud Run** | **Docker / .NET buildpack** | ✅ tek gerekli parça |

İleride merkezi hesap / web paneli / dashboard gerekirse Firestore eklenebilir;
şu anki yapıda buna ihtiyaç yok.

### 3. Ortam değişkenleri (backend)

| Değişken | Gerekli | Açıklama |
|---|---|---|
| `NOCTRA_PACKAGE_NAME` | ✅ | Uygulama paket adı (örn. `studio.kynora.noctra`). |
| `NOCTRA_SUBSCRIPTION_PRODUCT_IDS` | ✅ | Virgülle ayrılmış abonelik ürün kimlikleri. |
| `NOCTRA_LIFETIME_PRODUCT_IDS` | ✅ | Virgülle ayrılmış tek seferlik ürün kimlikleri. |
| `NOCTRA_BILLING_API_KEY` | ⬜ | Opsiyonel paylaşılan sır; ayarlanırsa client `X-Noctra-Billing-Key` başlığı gönderir, backend eşleşmezse 401 döner. |

Play erişimi ortam değişkeniyle değil **ADC (service identity)** ile yapılır.

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
  (Play yanıtı ayrıştırma, durum/trial mantığı, doğrulama akışı; sahte HTTP
  handler'ları + fake token provider).
- Client: `BillingVerificationClientTests`, `StoreEntitlementFallbackTests`,
  `StoreEntitlementTests` (Noctra.Tests).
- Canlı test: Play Console → **License testing** → test cihazlarını ekle ve
  satın alma test token'larıyla `POST /billing/google/verify`'u dene.

## Güvenlik notları

- Play API erişimi yalnızca sunucuda (ADC); asla APK'da değil. Private key
  indirilip gömülmez.
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
- **Stateless olmanın etkisi**: iptal/refund gibi olaylar uygulama açıkken
  arka planda anında yansımaz (RTDN yoktur); kullanıcı uygulamayı açtığında /
  resume olduğunda doğrulama her zaman Play'deki gerçek durumu çeker.
  Noctra'nın merkezi hesabı olmadığı için bu kayıp yok sayılır.
