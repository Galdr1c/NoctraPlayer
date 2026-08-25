# Play Console + Google Cloud Kurulum Kontrol Listesi

Bu sayfadaki adımlar **tarayıcıda sizin hesabınızla** yapılır — başka hiçbir
yerde tekrarlanmaz. Script'ler (deploy-billing.sh / .ps1) bu adımların
çıktılarını kullanır.

> ⏱️ Toplam süre: 20–30 dk. Tek seferlik.

---

## 1. Google Cloud hesabı (10 dk)

1. **https://console.cloud.google.com** adresine gidin, Google hesabınızla
   giriş yapın.
2. İlk açılışta **kart bilgisi** istenir — bu normaldir. Ücretsiz katman
   dahilinde **ücret çekilmez**; ayrıca 90 gün geçerli **$300 deneme kredisi**
   verilir. (Güvenli taraf: aylık bütçe uyarısı kurmak için
   **Billing → Budgets & alerts** → yeni bütçe, örn. $1 eşiği.)
3. **Yeni proje** oluşturun: `noctra-billing` (veya istediğiniz ad).
   Proje ID'sini not edin (deploy scripti sorar).

## 2. Play Console'da ürünler (10 dk)

1. **https://play.google.com/console** → uygulamanızı seçin.
2. Sol menüden **Monetize → Products → Subscriptions**:
   - **Create subscription** → `noctra_premium_monthly`
     - Billing period: aylık (1 month), auto-renewing
     - Base plan oluşturun (fiyatını belirleyin)
     - **Save + Activate**
3. **Monetize → Products → In-app products**:
   - **Create product** → `noctra_premium_lifetime`
     - Product type: **Managed product** (non-consumable)
     - Fiyatını belirleyin
     - **Save + Activate**
4. Uygulamanız **canlı (production) değilse** bile ürünleri test amaçlı
   oluşturabilirsiniz; ancak gerçek satın alma için uygulamanın en az
   **internal testing** kanalında ve ürünlerin canlı durumda olması gerekir.

## 3. Service account (5 dk)

1. Play Console → uygulamanız → **Setup → API access**.
2. **"Play Developer API"yi etkinleştirin** (yoksa "Link" / "Create").
3. **Create new service account** → sizi Google Cloud Console'a yönlendirir:
   - Service account adı: `noctra-billing`
   - **Create and continue** → rol gerekmez (Play izinleri Play Console'dan
     verilir) → **Done**
   - Service account satırında **⋮ → Manage keys → Add key → JSON** →
     JSON dosyası iner.
4. Play Console'a dönüp (sayfayı yenileyin) service account'u seçin ve
   **Grant access**:
   - **View financial data, orders and cancellation survey responses**
   - **Manage orders and subscriptions** (acknowledge izni için)
5. İnen JSON'u repo köküne **`service-account.json`** adıyla koyun.

> 🔒 Bu dosya **git'e eklenmemeli**. `.gitignore`'a eklemek için:
> `echo "service-account.json" >> .gitignore`

## 4. Ürün ID'leri eşleşmesi

Script varsayılan olarak şu ID'leri kullanır — Play Console'daki ürün
ID'leriniz bunlarla **birebir aynı** olmalıdır:

| Ürün | ID |
|---|---|
| Aylık abonelik | `noctra_premium_monthly` |
| Kalıcı paket | `noctra_premium_lifetime` |
| Paket adı | `studio.kynora.noctra` |

Farklıysa deploy komutundaki `NOCTRA_SUBSCRIPTION_PRODUCT_IDS` /
`NOCTRA_LIFETIME_PRODUCT_IDS` / `NOCTRA_PACKAGE_NAME` değerlerini güncelleyin.

---

## 5. Upload key ve imzalı AAB (Play'e yükleme için)

Play Console'a yüklenen her AAB **kendi upload key'inizle imzalı** olmalıdır.
Bu, AdMob/Huawei ID'lerinden bağımsız bir zorunluluktur: "All uploaded bundles
must be signed" hatası imza eksikliği anlamına gelir.

Tek seferlik:

```powershell
# 1. Upload keystore oluştur (şifreleri sorar)
.\build\package-play.ps1 -CreateKeystore -KeystorePath C:\Keys\noctra-upload.keystore

# 2. Keystore'u İKİ ayrı güvenli yerde yedekle. Kaybedilirse uygulama
#    aynı sertifika ile güncellenemez.
```

Her release'te:

```powershell
$env:NOCTRA_ANDROID_STORE_PASS = '...'
$env:NOCTRA_ANDROID_KEY_PASS   = '...'
.\build\package-play.ps1
```

Script `artifacts\play\*.aab` altına imzalı bundle'ı koyar ve `jarsigner`
ile doğrular. Şifreler repo'da hiçbir dosyaya yazılmaz; istenirse
`build/android-signing.local.env` (gitignored) içine KEY=VALUE olarak
konabilir. Ayrıntılar: `build/package-play.ps1` açıklama başlığı.

---

## Sonraki adım

```
bash deploy-billing.sh        # Windows: .\deploy-billing.ps1
bash smoke-test-billing.sh    # deploy sonrası doğrulama
```

Script, `.env` dosyasına `NOCTRA_BILLING_VERIFY_URL=<URL>` yazar. Ardından
**Release** build alındığında bu adres APK'ya gömülür (bkz.
`docs/play-billing-verification.md`).
