#!/usr/bin/env bash
# ============================================================
# Noctra.Billing.Api → Google Cloud Run tek komut deploy
#
# Ücretsiz katman (Always Free) yalnızca şu bölgelerde uygulanır:
#   us-central1, us-east1, us-west1  → bu yüzden us-central1 kullanılır.
#
# Bu script sizin yerinize şunları yapar:
#   1) gcloud CLI kurulu mu kontrol eder (yoksa kurulum talimatı verir)
#   2) gcloud kimlik doğrulaması kontrol eder (auth login gerekiyorsa yönlendirir)
#   3) GCP projesi seçer / oluşturmanızı ister
#   4) Service account JSON'unu Secret Manager'a yükler
#   5) Cloud Run'a deploy eder (env değişkenleriyle)
#   6) Çıkan URL'yi .env dosyasına yazar (client build için hazır)
#
# Kullanım:  bash deploy-billing.sh
#
# Ön koşullar (sizin tarafınızda, tarayıcıda 1 kez):
#   - Google Cloud hesabı (kart istenir ama ücretsiz katman dahilinde ücret
#     çekilmez; $300 deneme kredisi verilir)
#   - Play Console'da ürünler + service account JSON (docs/play-console-setup-checklist.md)
# ============================================================
set -euo pipefail

# ---------- Yapılandırma ----------
REGION="us-central1"
SERVICE_NAME="noctra-billing-api"
PACKAGE_NAME="${NOCTRA_PACKAGE_NAME:-studio.kynora.noctra}"
SUBSCRIPTION_IDS="${NOCTRA_SUBSCRIPTION_PRODUCT_IDS:-noctra_premium_monthly}"
LIFETIME_IDS="${NOCTRA_LIFETIME_PRODUCT_IDS:-noctra_premium_lifetime}"
SECRET_NAME="noctra-billing-service-account"
CREDENTIALS_JSON="${NOCTRA_GOOGLE_CREDENTIALS_JSON:-}"

# ---------- 0) API key (.env'den) ----------
# Client build-time'da ayni anahtari gomer; backend de ayni degeri almali.
API_KEY="${NOCTRA_BILLING_API_KEY:-}"
if [[ -z "${API_KEY}" ]] && [[ -f ".env" ]] && grep -q "^NOCTRA_BILLING_API_KEY=" .env; then
  API_KEY="$(grep "^NOCTRA_BILLING_API_KEY=" .env | head -1 | cut -d= -f2-)"
fi

# ---------- 0b) RTDN OIDC (.env'den) ----------
# Pub/Sub push aboneliği için audience + service account e-postası. Backend
# bunlar eksikse fail-fast ile başlamaz (RTDN auth sessizce kapanmaz). RTDN
# kullanılmayacaksa NOCTRA_RTDN_DISABLED=1 ile açıkça kapatılabilir.
RTDN_AUDIENCE="${NOCTRA_RTDN_AUDIENCE:-}"
RTDN_SERVICE_ACCOUNT_EMAIL="${NOCTRA_RTDN_SERVICE_ACCOUNT_EMAIL:-}"
RTDN_DISABLED="${NOCTRA_RTDN_DISABLED:-}"
if [[ -f ".env" ]]; then
  if [[ -z "${RTDN_AUDIENCE}" ]] && grep -q "^NOCTRA_RTDN_AUDIENCE=" .env; then
    RTDN_AUDIENCE="$(grep "^NOCTRA_RTDN_AUDIENCE=" .env | head -1 | cut -d= -f2-)"
  fi
  if [[ -z "${RTDN_SERVICE_ACCOUNT_EMAIL}" ]] && grep -q "^NOCTRA_RTDN_SERVICE_ACCOUNT_EMAIL=" .env; then
    RTDN_SERVICE_ACCOUNT_EMAIL="$(grep "^NOCTRA_RTDN_SERVICE_ACCOUNT_EMAIL=" .env | head -1 | cut -d= -f2-)"
  fi
  if [[ -z "${RTDN_DISABLED}" ]] && grep -q "^NOCTRA_RTDN_DISABLED=" .env; then
    RTDN_DISABLED="$(grep "^NOCTRA_RTDN_DISABLED=" .env | head -1 | cut -d= -f2-)"
  fi
fi

if [[ "${RTDN_DISABLED}" != "1" ]]; then
  if [[ -z "${RTDN_AUDIENCE}" || -z "${RTDN_SERVICE_ACCOUNT_EMAIL}" ]]; then
    echo ""
    echo "❌ RTDN yapılandırması eksik (backend fail-fast ile başlamaz)."
    echo "   Pub/Sub push aboneliği oluşturup .env dosyasına şunları ekleyin:"
    echo ""
    echo "   NOCTRA_RTDN_AUDIENCE=https://pubsub.example.com/push"
    echo "   NOCTRA_RTDN_SERVICE_ACCOUNT_EMAIL=push-sa@PROJECT.iam.gserviceaccount.com"
    echo ""
    echo "   (Pub/Sub push subscription oluştururken 'Authentication' kısmında"
    echo "   belirlediğiniz audience ve imzalayan service account e-postası.)"
    echo "   RTDN kullanmayacaksanız:  NOCTRA_RTDN_DISABLED=1"
    exit 1
  fi
fi

# ---------- 1) gcloud kontrol ----------
if ! command -v gcloud >/dev/null 2>&1; then
  echo "❌ gcloud CLI bulunamadı."
  echo ""
  echo "Windows:  winget install Google.CloudSDK"
  echo "          (veya https://cloud.google.com/sdk/docs/install  → Windows installer)"
  echo "macOS:    brew install --cask google-cloud-sdk"
  echo "Linux:    curl https://sdk.cloud.google.com | bash"
  echo ""
  echo "Kurulumdan sonra yeni bir terminal açıp bu scripti tekrar çalıştırın."
  exit 1
fi

# ---------- 2) Kimlik doğrulama ----------
if ! gcloud auth list --filter=status:ACTIVE --format="value(account)" 2>/dev/null | grep -q .; then
  echo "⚠️  gcloud ile giriş yapılmamış. Tarayıcıda Google hesabınızla giriş yapın:"
  gcloud auth login
fi
ACCOUNT="$(gcloud auth list --filter=status:ACTIVE --format="value(account)" | head -1)"
echo "✅ Kimlik: $ACCOUNT"

# ---------- 3) Proje ----------
PROJECT="$(gcloud config get-value project 2>/dev/null || true)"
if [[ -z "${PROJECT}" ]]; then
  echo ""
  echo "GCP proje ID'sini girin (Console'da oluşturduğunuz proje; boşsa burada "
  echo "oluşturmak için 2 girin):"
  echo "  1) Var olan projeyi kullan"
  echo "  2) Yeni proje oluştur"
  read -r -p "Seçim [1/2]: " CHOICE
  if [[ "${CHOICE}" == "2" ]]; then
    read -r -p "Yeni proje ID'si (örn. noctra-billing): " PROJECT
    gcloud projects create "${PROJECT}"
  else
    gcloud projects list --format="value(projectId)"
    read -r -p "Proje ID: " PROJECT
  fi
  gcloud config set project "${PROJECT}"
fi
echo "✅ Proje: $PROJECT"
gcloud services enable run.googleapis.com cloudbuild.googleapis.com secretmanager.googleapis.com

# ---------- 4) Service account secret ----------
if [[ -z "${CREDENTIALS_JSON}" ]]; then
  if [[ -f "service-account.json" ]]; then
    CREDENTIALS_JSON="$(cat service-account.json)"
    echo "📄 service-account.json dosyasından okundu."
  elif [[ -f "noctra-service-account.json" ]]; then
    CREDENTIALS_JSON="$(cat noctra-service-account.json)"
    echo "📄 noctra-service-account.json dosyasından okundu."
  else
    echo ""
    echo "⚠️  Service account JSON bulunamadı."
    echo "   Play Console → Setup → API access → service account JSON'unu indirin,"
    echo "   dosyayı bu klasöre 'service-account.json' adıyla koyun ve tekrar çalıştırın."
    exit 1
  fi
fi

if ! gcloud secrets describe "${SECRET_NAME}" --project="${PROJECT}" >/dev/null 2>&1; then
  echo "${CREDENTIALS_JSON}" | gcloud secrets create "${SECRET_NAME}" \
    --data-file=- --project="${PROJECT}"
  echo "🔐 Secret oluşturuldu: ${SECRET_NAME}"
fi

# ---------- 5) Deploy ----------
echo "🚀 Deploy ediliyor (${REGION})..."
# --dockerfile: Dockerfile Noctra.Billing.Api/ alt klasöründe; repo kökünde
# birden çok .csproj olduğu için buildpack kök Dockerfile olmadan başarısız olur.
ENV_ARGS="NOCTRA_PACKAGE_NAME=${PACKAGE_NAME},NOCTRA_SUBSCRIPTION_PRODUCT_IDS=${SUBSCRIPTION_IDS},NOCTRA_LIFETIME_PRODUCT_IDS=${LIFETIME_IDS}"
if [[ -n "${API_KEY}" ]]; then
  ENV_ARGS="${ENV_ARGS},NOCTRA_BILLING_API_KEY=${API_KEY}"
fi
if [[ "${RTDN_DISABLED}" == "1" ]]; then
  ENV_ARGS="${ENV_ARGS},NOCTRA_RTDN_DISABLED=1"
else
  ENV_ARGS="${ENV_ARGS},NOCTRA_RTDN_AUDIENCE=${RTDN_AUDIENCE},NOCTRA_RTDN_SERVICE_ACCOUNT_EMAIL=${RTDN_SERVICE_ACCOUNT_EMAIL}"
fi

gcloud run deploy "${SERVICE_NAME}" \
  --source . \
  --dockerfile Noctra.Billing.Api/Dockerfile \
  --region "${REGION}" \
  --allow-unauthenticated \
  --set-secrets="NOCTRA_GOOGLE_CREDENTIALS_JSON=${SECRET_NAME}:latest" \
  --set-env-vars="${ENV_ARGS}" \
  --project="${PROJECT}"

# ---------- 6) URL'yi .env'e yaz ----------
# status.url tam URL döndürür (örn. https://noctra-billing-api-xxxx-uc.a.run.app)
URL="$(gcloud run services describe "${SERVICE_NAME}" --region="${REGION}" --project="${PROJECT}" --format='value(status.url)' 2>/dev/null)"
if [[ -z "${URL}" ]]; then
  echo "⚠️  URL otomatik alınamadı — Cloud Console'dan kopyalayın."
  gcloud run services list --region="${REGION}" --project="${PROJECT}"
  exit 0
fi

echo ""
echo "🎉 Deploy tamamlandı!"
echo "   URL: ${URL}"
echo ""

# .env'e yaz (mevcut NOCTRA_BILLING_VERIFY_URL varsa güncelle, yoksa ekle)
if [[ -f ".env" ]]; then
  if grep -q "^NOCTRA_BILLING_VERIFY_URL=" .env; then
    sed -i "s|^NOCTRA_BILLING_VERIFY_URL=.*|NOCTRA_BILLING_VERIFY_URL=${URL}|" .env
  else
    echo "NOCTRA_BILLING_VERIFY_URL=${URL}" >> .env
  fi
else
  echo "NOCTRA_BILLING_VERIFY_URL=${URL}" > .env
fi
echo "📝 .env dosyasına yazıldı: NOCTRA_BILLING_VERIFY_URL=${URL}"

echo ""
echo "Sağlık kontrolü:"
curl -fsS "${URL}/health" || echo "⚠️  /health yanıt vermedi — birkaç saniye sonra tekrar deneyin."
