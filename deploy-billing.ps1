# ============================================================
# Noctra.Billing.Api → Google Cloud Run tek komut deploy (Windows)
#
# Ücretsiz katman (Always Free) yalnızca şu bölgelerde uygulanır:
#   us-central1, us-east1, us-west1  → bu yüzden us-central1 kullanılır.
#
# Kullanım:  .\deploy-billing.ps1
# Ön koşul:  gcloud CLI kurulu (winget install Google.CloudSDK) + gcloud auth login
# ============================================================
$ErrorActionPreference = "Stop"

$Region = "us-central1"
$ServiceName = "noctra-billing-api"

# gcloud hatalarinda script durur (native komutlar $ErrorActionPreference'a takilmaz)
function Invoke-Gcloud {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Args)
    gcloud @Args
    if ($LASTEXITCODE -ne 0) { throw "gcloud komutu başarısız (exit $LASTEXITCODE): gcloud $($Args -join ' ')" }
}
$PackageName = if ($env:NOCTRA_PACKAGE_NAME) { $env:NOCTRA_PACKAGE_NAME } else { "studio.kynora.noctra" }
$SubscriptionIds = if ($env:NOCTRA_SUBSCRIPTION_PRODUCT_IDS) { $env:NOCTRA_SUBSCRIPTION_PRODUCT_IDS } else { "noctra_premium_monthly" }
$LifetimeIds = if ($env:NOCTRA_LIFETIME_PRODUCT_IDS) { $env:NOCTRA_LIFETIME_PRODUCT_IDS } else { "noctra_premium_lifetime" }
$SecretName = "noctra-billing-service-account"

# Client build-time'da ayni anahtari gomer; backend de ayni degeri almali.
$ApiKey = $env:NOCTRA_BILLING_API_KEY
if ([string]::IsNullOrWhiteSpace($ApiKey) -and (Test-Path ".env")) {
    $ApiLine = Get-Content ".env" | Where-Object { $_ -match "^NOCTRA_BILLING_API_KEY=" } | Select-Object -First 1
    if ($ApiLine) { $ApiKey = $ApiLine.Substring("NOCTRA_BILLING_API_KEY=".Length).Trim() }
}

# ---------- 0b) RTDN OIDC (.env'den) ----------
# Pub/Sub push aboneliği için audience + service account e-postası. Backend
# bunlar eksikse fail-fast ile başlamaz; RTDN kullanılmayacaksa DISABLED=1.
$RtdnAudience = $env:NOCTRA_RTDN_AUDIENCE
$RtdnServiceAccountEmail = $env:NOCTRA_RTDN_SERVICE_ACCOUNT_EMAIL
$RtdnDisabled = $env:NOCTRA_RTDN_DISABLED
if (Test-Path ".env") {
    if ([string]::IsNullOrWhiteSpace($RtdnAudience)) {
        $Line = Get-Content ".env" | Where-Object { $_ -match "^NOCTRA_RTDN_AUDIENCE=" } | Select-Object -First 1
        if ($Line) { $RtdnAudience = $Line.Substring("NOCTRA_RTDN_AUDIENCE=".Length).Trim() }
    }
    if ([string]::IsNullOrWhiteSpace($RtdnServiceAccountEmail)) {
        $Line = Get-Content ".env" | Where-Object { $_ -match "^NOCTRA_RTDN_SERVICE_ACCOUNT_EMAIL=" } | Select-Object -First 1
        if ($Line) { $RtdnServiceAccountEmail = $Line.Substring("NOCTRA_RTDN_SERVICE_ACCOUNT_EMAIL=".Length).Trim() }
    }
    if ([string]::IsNullOrWhiteSpace($RtdnDisabled)) {
        $Line = Get-Content ".env" | Where-Object { $_ -match "^NOCTRA_RTDN_DISABLED=" } | Select-Object -First 1
        if ($Line) { $RtdnDisabled = $Line.Substring("NOCTRA_RTDN_DISABLED=".Length).Trim() }
    }
}

if ($RtdnDisabled -ne "1") {
    if ([string]::IsNullOrWhiteSpace($RtdnAudience) -or [string]::IsNullOrWhiteSpace($RtdnServiceAccountEmail)) {
        Write-Host "`n❌ RTDN yapılandırması eksik (backend fail-fast ile başlamaz)." -ForegroundColor Red
        Write-Host "   .env dosyasına ekleyin:"
        Write-Host "   NOCTRA_RTDN_AUDIENCE=https://pubsub.example.com/push"
        Write-Host "   NOCTRA_RTDN_SERVICE_ACCOUNT_EMAIL=push-sa@PROJECT.iam.gserviceaccount.com"
        Write-Host "   (Pub/Sub push subscription 'Authentication' ayarındaki değerler.)"
        Write-Host "   RTDN kullanmayacaksanız:  NOCTRA_RTDN_DISABLED=1`n"
        exit 1
    }
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
Invoke-Gcloud services enable run.googleapis.com cloudbuild.googleapis.com secretmanager.googleapis.com | Out-Null

# ---------- 4) Service account secret ----------
$CredentialsJson = $env:NOCTRA_GOOGLE_CREDENTIALS_JSON
if ([string]::IsNullOrWhiteSpace($CredentialsJson)) {
    $Candidate = @("service-account.json", "noctra-service-account.json") | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($Candidate) {
        $CredentialsJson = Get-Content $Candidate -Raw
        Write-Host "📄 $Candidate dosyasından okundu." -ForegroundColor Cyan
    }
    else {
        Write-Host "`n⚠️  Service account JSON bulunamadı." -ForegroundColor Yellow
        Write-Host "   Play Console → Setup → API access → JSON indirin, bu klasöre"
        Write-Host "   'service-account.json' adıyla koyun ve tekrar çalıştırın.`n"
        exit 1
    }
}

$SecretExists = gcloud secrets describe $SecretName --project=$Project 2>$null
if (-not $SecretExists) {
    $CredentialsJson | Invoke-Gcloud secrets create $SecretName --data-file=- --project=$Project
    Write-Host "🔐 Secret oluşturuldu: $SecretName" -ForegroundColor Cyan
}

# ---------- 5) Deploy ----------
Write-Host "`n🚀 Deploy ediliyor ($Region)..." -ForegroundColor Cyan
# --dockerfile: Dockerfile Noctra.Billing.Api/ alt klasöründe; repo kökünde
# birden çok .csproj olduğu için buildpack kök Dockerfile olmadan başarısız olur.
$EnvArgs = "NOCTRA_PACKAGE_NAME=$PackageName,NOCTRA_SUBSCRIPTION_PRODUCT_IDS=$SubscriptionIds,NOCTRA_LIFETIME_PRODUCT_IDS=$LifetimeIds"
if (-not [string]::IsNullOrWhiteSpace($ApiKey)) {
    $EnvArgs += ",NOCTRA_BILLING_API_KEY=$ApiKey"
}
if ($RtdnDisabled -eq "1") {
    $EnvArgs += ",NOCTRA_RTDN_DISABLED=1"
}
else {
    $EnvArgs += ",NOCTRA_RTDN_AUDIENCE=$RtdnAudience,NOCTRA_RTDN_SERVICE_ACCOUNT_EMAIL=$RtdnServiceAccountEmail"
}

Invoke-Gcloud run deploy $ServiceName `
    --source . `
    --dockerfile Noctra.Billing.Api/Dockerfile `
    --region $Region `
    --allow-unauthenticated `
    --set-secrets="NOCTRA_GOOGLE_CREDENTIALS_JSON=$SecretName`:latest" `
    --set-env-vars="$EnvArgs" `
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
