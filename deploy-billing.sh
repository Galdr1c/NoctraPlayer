#!/usr/bin/env bash
# ============================================================
# Noctra.Billing.Api → Google Cloud Run tek komut deploy
#
# Backend STATELESS'tir: entitlement verisi saklanmaz, RTDN yoktur,
# SQLite/Firestore yoktur. Tek işi: purchase token'ı Google Play
# Developer API'de doğrulamak.
#
# Kimlik doğrulama: Secret Manager'da private key YOK. Cloud Run'a
# bağlanan service account (service identity) metadata üzerinden
# otomatik token verir (ADC). Bu service account'a Play Console'da
# gerekli izinler verilir.
#
# Ücretsiz katman (Always Free) yalnızca şu bölgelerde uygulanır:
#   us-central1, us-east1, us-west1  → bu yüzden us-central1 kullanılır.
#
# Kullanım:  bash deploy-billing.sh
#
# Ön koşullar (sizin tarafınızda, tarayıcıda 1 kez):
#   - Google Cloud hesabı
#   - Play Console'da ürünler (docs/play-console-setup-checklist.md)
#   - Play Console → Setup → API access: aşağıdaki service account
#     e-postasına gerekli izinler verilmiş
# ============================================================
set -euo pipefail

# ---------- Yapılandırma ----------
REGION="us-central1"
SERVICE_NAME="noctra-billing-api"
# Cloud Run service identity — Play API'ye erişecek service account.
# Tam e-posta adresi proje seçildikten sonra SA_EMAIL olarak hesaplanır.
SERVICE_ACCOUNT_NAME="noctra-billing-runtime"
PACKAGE_NAME="${NOCTRA_PACKAGE_NAME:-studio.kynora.noctra}"
SUBSCRIPTION_IDS="${NOCTRA_SUBSCRIPTION_PRODUCT_IDS:-noctra_premium_monthly}"
LIFETIME_IDS="${NOCTRA_LIFETIME_PRODUCT_IDS:-noctra_premium_lifetime}"

# ---------- 0) API key (.env'den) ----------
# Client build-time'da ayni anahtari gomer; backend de ayni degeri almali.
API_KEY="${NOCTRA_BILLING_API_KEY:-}"
if [[ -z "${API_KEY}" ]] && [[ -f ".env" ]] && grep -q "^NOCTRA_BILLING_API_KEY=" .env; then
  API_KEY="$(grep "^NOCTRA_BILLING_API_KEY=" .env | head -1 | cut -d= -f2-)"
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
# Source deployment Cloud Build + Artifact Registry altyapısını kullanır;
# Artifact Registry API'si ilk deploy'da istendiği için önceden etkinleştirilir.
gcloud services enable run.googleapis.com cloudbuild.googleapis.com artifactregistry.googleapis.com

# ---------- 4) Service account (service identity) ----------
# Cloud Run'a bağlanacak service account — private key YOK, ADC ile
# metadata üzerinden token alır. Yalnızca oluşturulmamışsa oluşturulur.
SA_EMAIL="${SERVICE_ACCOUNT_NAME}@${PROJECT}.iam.gserviceaccount.com"
if ! gcloud iam service-accounts describe "${SA_EMAIL}" --project="${PROJECT}" >/dev/null 2>&1; then
  echo "🔐 Service account oluşturuluyor: ${SA_EMAIL}"
  gcloud iam service-accounts create "${SERVICE_ACCOUNT_NAME}" \
    --display-name="Noctra Billing Runtime" --project="${PROJECT}"
fi

echo ""
echo "ℹ️  Play Console → Setup → API access → bu e-postaya izin verin:"
echo "      ${SA_EMAIL}"
echo "   (proje izinleri docs/play-console-setup-checklist.md'de anlatılıyor.)"
echo ""

# ---------- 5) Deploy ----------
echo "🚀 Deploy ediliyor (${REGION})..."
# Source deployment: Dockerfile, --source dizininde aranır. Dockerfile,
# Noctra.Billing.Api klasörünü build context kabul edecek şekilde yazılmıştır
# (repo kökünde birden çok .csproj olduğu için source-root buildpack
# kullanılamaz — gcloud run deploy'da --dockerfile argümanı yoktur).
# Her KEY=VALUE ayrı --set-env-vars bayrağıyla verilir: değerler virgül
# içerebilir (örn. çoklu ürün: NOCTRA_SUBSCRIPTION_PRODUCT_IDS=a,b,c) —
# tek virgüllü string'de gcloud virgülü env ayrımı sanıp deploy'u kırardı.
ENV_FLAGS=(
  "--set-env-vars=NOCTRA_PACKAGE_NAME=${PACKAGE_NAME}"
  "--set-env-vars=NOCTRA_SUBSCRIPTION_PRODUCT_IDS=${SUBSCRIPTION_IDS}"
  "--set-env-vars=NOCTRA_LIFETIME_PRODUCT_IDS=${LIFETIME_IDS}"
)
if [[ -n "${API_KEY}" ]]; then
  ENV_FLAGS+=( "--set-env-vars=NOCTRA_BILLING_API_KEY=${API_KEY}" )
fi

gcloud run deploy "${SERVICE_NAME}" \
  --source Noctra.Billing.Api \
  --region "${REGION}" \
  --allow-unauthenticated \
  --service-account "${SA_EMAIL}" \
  "${ENV_FLAGS[@]}" \
  --min-instances 0 \
  --max-instances 2 \
  --memory 256Mi \
  --cpu 1 \
  --project="${PROJECT}"

# ---------- 6) URL'yi .env'e yaz ----------
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
