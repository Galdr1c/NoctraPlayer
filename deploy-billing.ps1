# ============================================================
# Noctra.Billing.Api → Google Cloud Run tek komut deploy (Windows)
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
# Ücretsiz katman: us-central1/us-east1/us-west1 → us-central1 kullanılır.
#
# Kullanım:  .\deploy-billing.ps1
# Ön koşul:  gcloud CLI kurulu (winget install Google.CloudSDK) + gcloud auth login
# ============================================================
$ErrorActionPreference = "Stop"

$Region = "us-central1"
$ServiceName = "noctra-billing-api"
$ServiceAccountName = "noctra-billing-runtime"

# gcloud hatalarinda script durur (native komutlar $ErrorActionPreference'a takilmaz)
function Invoke-Gcloud {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Args)
    gcloud @Args
    if ($LASTEXITCODE -ne 0) { throw "gcloud komutu başarısız (exit $LASTEXITCODE): gcloud $($Args -join ' ')" }
}
$PackageName = if ($env:NOCTRA_PACKAGE_NAME) { $env:NOCTRA_PACKAGE_NAME } else { "studio.kynora.noctra" }
$SubscriptionIds = if ($env:NOCTRA_SUBSCRIPTION_PRODUCT_IDS) { $env:NOCTRA_SUBSCRIPTION_PRODUCT_IDS } else { "noctra_premium_monthly" }
$LifetimeIds = if ($env:NOCTRA_LIFETIME_PRODUCT_IDS) { $env:NOCTRA_LIFETIME_PRODUCT_IDS } else { "noctra_premium_lifetime" }

# Client build-time'da ayni anahtari gomer; backend de ayni degeri almali.
$ApiKey = $env:NOCTRA_BILLING_API_KEY
if ([string]::IsNullOrWhiteSpace($ApiKey) -and (Test-Path ".env")) {
    $ApiLine = Get-Content ".env" | Where-Object { $_ -match "^NOCTRA_BILLING_API_KEY=" } | Select-Object -First 1
    if ($ApiLine) { $ApiKey = $ApiLine.Substring("NOCTRA_BILLING_API_KEY=".Length).Trim() }
}

# ---------- 1) gcloud kontrol ----------
if (-not (Get-Command gcloud -ErrorAction SilentlyContinue)) {
    Write-Host "`n❌ gcloud CLI bulunamadı. Kurulum:" -ForegroundColor Red
    Write-Host "   winget install Google.CloudSDK"
    Write-Host "   (veya https://cloud.google.com/sdk/docs/install → Windows installer)"
    Write-Host "Kurulumdan sonra YENİ bir terminal açıp scripti tekrar çalıştırın.`n"
    exit 1
}

# ---------- 2) Kimlik doğrulama ----------
$Accounts = gcloud auth list --filter=status:ACTIVE --format="value(account)" 2>$null
if (-not $Accounts) {
    Write-Host "`n⚠️  gcloud ile giriş yapılmamış. Tarayıcıda Google hesabınızla giriş yapın:" -ForegroundColor Yellow
    Invoke-Gcloud auth login
}
$Account = (gcloud auth list --filter=status:ACTIVE --format="value(account)" | Select-Object -First 1)
Write-Host "✅ Kimlik: $Account" -ForegroundColor Green

# ---------- 3) Proje ----------
$Project = gcloud config get-value project 2>$null
if ([string]::IsNullOrWhiteSpace($Project)) {
    Write-Host ""
    Write-Host "GCP proje ID'si girin:"
    Invoke-Gcloud projects list --format="value(projectId)"
    $Project = Read-Host "Proje ID"
    Invoke-Gcloud config set project $Project
}
Write-Host "✅ Proje: $Project" -ForegroundColor Green
Invoke-Gcloud services enable run.googleapis.com cloudbuild.googleapis.com | Out-Null

# ---------- 4) Service account (service identity) ----------
# Cloud Run'a bağlanacak service account — private key YOK, ADC ile
# metadata üzerinden token alır. Yalnızca oluşturulmamışsa oluşturulur.
$SaEmail = "$ServiceAccountName@$Project.iam.gserviceaccount.com"
$SaExists = gcloud iam service-accounts describe $SaEmail --project=$Project 2>$null
if (-not $SaExists) {
    Write-Host "🔐 Service account oluşturuluyor: $SaEmail" -ForegroundColor Cyan
    Invoke-Gcloud iam service-accounts create $ServiceAccountName --display-name="Noctra Billing Runtime" --project=$Project
}

Write-Host ""
Write-Host "ℹ️  Play Console → Setup → API access → bu e-postaya izin verin:" -ForegroundColor Yellow
Write-Host "      $SaEmail"
Write-Host "   (proje izinleri docs/play-console-setup-checklist.md'de anlatılıyor.)"
Write-Host ""

# ---------- 5) Deploy ----------
Write-Host "`n🚀 Deploy ediliyor ($Region)..." -ForegroundColor Cyan
# --dockerfile: Dockerfile Noctra.Billing.Api/ alt klasöründe; repo kökünde
# birden çok .csproj olduğu için buildpack kök Dockerfile olmadan başarısız olur.
$EnvArgs = "NOCTRA_PACKAGE_NAME=$PackageName,NOCTRA_SUBSCRIPTION_PRODUCT_IDS=$SubscriptionIds,NOCTRA_LIFETIME_PRODUCT_IDS=$LifetimeIds"
if (-not [string]::IsNullOrWhiteSpace($ApiKey)) {
    $EnvArgs += ",NOCTRA_BILLING_API_KEY=$ApiKey"
}

Invoke-Gcloud run deploy $ServiceName `
    --source . `
    --dockerfile Noctra.Billing.Api/Dockerfile `
    --region $Region `
    --allow-unauthenticated `
    --service-account $SaEmail `
    --set-env-vars="$EnvArgs" `
    --min-instances 0 `
    --max-instances 2 `
    --memory 256Mi `
    --cpu 1 `
    --project=$Project

# ---------- 6) URL'yi .env'e yaz ----------
$Url = gcloud run services describe $ServiceName --region=$Region --project=$Project --format="value(status.url)" 2>$null
if ([string]::IsNullOrWhiteSpace($Url)) {
    Write-Host "⚠️  URL otomatik alınamadı — Cloud Console'dan kopyalayın." -ForegroundColor Yellow
    exit 0
}

Write-Host "`n🎉 Deploy tamamlandı!" -ForegroundColor Green
Write-Host "   URL: $Url"

$EnvFile = ".env"
$Line = "NOCTRA_BILLING_VERIFY_URL=$Url"
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
if (Test-Path $EnvFile) {
    $Lines = Get-Content $EnvFile
    $Idx = [Array]::FindIndex($Lines, [Predicate[string]]{ param($s) $s.StartsWith("NOCTRA_BILLING_VERIFY_URL=") })
    if ($Idx -ge 0) {
        $Lines[$Idx] = $Line
    }
    else {
        $Lines += $Line
    }
    [System.IO.File]::WriteAllLines((Join-Path (Get-Location) $EnvFile), $Lines, $Utf8NoBom)
}
else {
    [System.IO.File]::WriteAllText((Join-Path (Get-Location) $EnvFile), $Line + "`n", $Utf8NoBom)
}
Write-Host "📝 .env dosyasına yazıldı: NOCTRA_BILLING_VERIFY_URL=$Url" -ForegroundColor Cyan

Write-Host ""
Write-Host "Sağlık kontrolü:"
try { Invoke-RestMethod -Uri "$Url/health" -TimeoutSec 10 } catch { Write-Host "⚠️  /health yanıt vermedi — birkaç saniye sonra tekrar deneyin." }
