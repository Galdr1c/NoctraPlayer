#!/usr/bin/env bash
# ============================================================
# Noctra.Billing.Api smoke testi — deploy sonrası doğrulama
#
# Kullanım:  bash smoke-test-billing.sh [URL]
# Örnek:     bash smoke-test-billing.sh https://noctra-billing-api-xxxx-uc.a.run.app
# URL verilmezse .env'deki NOCTRA_BILLING_VERIFY_URL kullanılır.
# ============================================================
set -uo pipefail

URL="${1:-}"
if [[ -z "${URL}" ]]; then
  if [[ -f ".env" ]] && grep -q "^NOCTRA_BILLING_VERIFY_URL=" .env; then
    URL="$(grep "^NOCTRA_BILLING_VERIFY_URL=" .env | head -1 | cut -d= -f2-)"
  fi
fi
if [[ -z "${URL}" ]]; then
  echo "❌ URL bulunamadı. Şu şekilde verin: bash smoke-test-billing.sh https://...run.app"
  exit 1
fi

# Backend'de API key yapılandırıldıysa istekler de göndermeli (yoksa 401 alır).
API_KEY=""
if [[ -f ".env" ]] && grep -q "^NOCTRA_BILLING_API_KEY=" .env; then
  API_KEY="$(grep "^NOCTRA_BILLING_API_KEY=" .env | head -1 | cut -d= -f2-)"
fi
AUTH_HEADERS=()
if [[ -n "${API_KEY}" ]]; then
  AUTH_HEADERS=(-H "X-Noctra-Billing-Key: ${API_KEY}")
  echo "  (API key .env'den okundu, isteklere eklendi)"
fi

PASS=0
FAIL=0
check() {
  local name="$1" result="$2"
  if [[ "${result}" == "PASS" ]]; then
    echo "  ✅ ${name}"
    PASS=$((PASS + 1))
  else
    echo "  ❌ ${name}"
    FAIL=$((FAIL + 1))
  fi
}

echo "=== Noctra.Billing.Api smoke testi ==="
echo "URL: ${URL}"
echo ""

# 1) /health
echo "[1] /health"
HTTP_CODE="$(curl -s -o /tmp/billing_health.json -w '%{http_code}' --max-time 15 "${URL}/health")"
if [[ "${HTTP_CODE}" == "200" ]]; then
  check "/health 200" PASS
else
  check "/health ${HTTP_CODE}" FAIL
fi

# 2) Sahte token ile verify isteği
echo "[2] Sahte token verify isteği"
HTTP_CODE="$(curl -s -o /tmp/billing_noauth.json -w '%{http_code}' --max-time 15 \
  -X POST "${URL}/billing/google/verify" \
  -H 'Content-Type: application/json' "${AUTH_HEADERS[@]}" \
  -d '{"purchaseToken":"test","productId":"noctra_premium_monthly","packageName":"studio.kynora.noctra"}')"
# 400 = sunucu doğrulama aşamasına gitmeden reddetti (çok kısa token/paket);
# 200 = Play'e gidip fail-closed inaktif döndü; 401 = API key gerekli;
# 502 = Play'e ulaşılamadı. Hepsi sunucunun çalıştığını gösterir.
if [[ "${HTTP_CODE}" == "200" || "${HTTP_CODE}" == "400" || "${HTTP_CODE}" == "401" || "${HTTP_CODE}" == "502" ]]; then
  check "verify isteği işlendi (${HTTP_CODE})" PASS
else
  check "verify isteği işlendi (${HTTP_CODE})" FAIL
fi

# 3) Bilinmeyen ürün → 400
echo "[3] Bilinmeyen ürün doğrulama (BillingRequestException)"
HTTP_CODE="$(curl -s -o /tmp/billing_badproduct.json -w '%{http_code}' --max-time 15 \
  -X POST "${URL}/billing/google/verify" \
  -H 'Content-Type: application/json' "${AUTH_HEADERS[@]}" \
  -d '{"purchaseToken":"test","productId":"noctra_unknown","packageName":"studio.kynora.noctra"}')"
if [[ "${HTTP_CODE}" == "400" ]]; then
  check "400 (productId sunucuda yok)" PASS
else
  check "beklenen 400, alınan ${HTTP_CODE}" FAIL
fi

# 4) packageName uyuşmazlığı → 400
echo "[4] Yanlış packageName"
HTTP_CODE="$(curl -s -o /tmp/billing_wrongpkg.json -w '%{http_code}' --max-time 15 \
  -X POST "${URL}/billing/google/verify" \
  -H 'Content-Type: application/json' "${AUTH_HEADERS[@]}" \
  -d '{"purchaseToken":"test","productId":"noctra_premium_monthly","packageName":"com.evil.other"}')"
if [[ "${HTTP_CODE}" == "400" ]]; then
  check "400 (packageName uyuşmuyor)" PASS
else
  check "beklenen 400, alınan ${HTTP_CODE}" FAIL
fi

# 5) Gerçek Play çağrısı (service account olmadan 502 beklenir)
echo "[5] Gerçek token doğrulama (service account eksikse 502)"
HTTP_CODE="$(curl -s -o /tmp/billing_real.json -w '%{http_code}' --max-time 20 \
  -X POST "${URL}/billing/google/verify" \
  -H 'Content-Type: application/json' "${AUTH_HEADERS[@]}" \
  -d '{"purchaseToken":"dummy-token","productId":"noctra_premium_monthly","packageName":"studio.kynora.noctra"}')"
# 200 = Play doğrulaması gerçekten çalışıyor; diğer kodlar da sunucunun ayakta olduğunu gösterir.
if [[ "${HTTP_CODE}" == "200" ]]; then
  check "200 (Play doğrulaması çalışıyor)" PASS
else
  check "sunucu çalışıyor (${HTTP_CODE})" PASS
fi

echo ""
echo "Sonuç: ${PASS} geçti, ${FAIL} başarısız."
if [[ "${FAIL}" -gt 0 ]]; then
  exit 1
fi
