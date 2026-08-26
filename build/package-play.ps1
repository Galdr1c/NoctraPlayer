<#
.SYNOPSIS
    Builds a Play Console-ready, signed Release .aab for Noctra.Android.

.DESCRIPTION
    Wraps `dotnet publish` with the .NET for Android signing properties so the
    produced Android App Bundle is signed with YOUR upload key — required by
    Play App Signing (Google re-signs for distribution; you must still upload
    an AAB signed with your upload key).

    Secrets are NEVER written into this script or the csproj. They are read,
    in order of precedence:
      1. Script parameters   (-StorePass / -KeyPass)
      2. Environment vars    NOCTRA_ANDROID_STORE_PASS / NOCTRA_ANDROID_KEY_PASS
      3. Local secrets file  (default: build\android-signing.local.env, gitignored)

    The secrets file is plain KEY=VALUE lines:
        KeystorePath=C:\Keys\noctra-upload.keystore
        KeyAlias=noctra-upload
        StorePass=...
        KeyPass=...

    If no keystore exists at KeystorePath yet, run once with -CreateKeystore to
    generate it via keytool (you will be prompted for passwords and identity).

.PARAMETER Configuration
    Build configuration. Only Release produces an installable store bundle.
    Defaults to Release.

.PARAMETER OutputDir
    Directory the signed .aab is copied to. Defaults to artifacts/play.

.PARAMETER KeystorePath
    Path to the upload keystore (.keystore/.jks).

.PARAMETER KeyAlias
    Alias of the upload key inside the keystore.

.PARAMETER StorePass / KeyPass
    Keystore / key passwords. Prefer the env vars or the secrets file instead.

.PARAMETER CreateKeystore
    Creates the keystore with keytool if it does not exist, then exits.

.PARAMETER SkipVerification
    Skips the jarsigner verification pass.

.EXAMPLE
    # One-time: create the upload key (back it up afterwards!)
    .\build\package-play.ps1 -CreateKeystore

    # Every release:
    $env:NOCTRA_ANDROID_STORE_PASS = '...' ; $env:NOCTRA_ANDROID_KEY_PASS = '...'
    .\build\package-play.ps1
#>

param(
    [ValidateSet("Release")]
    [string]$Configuration = "Release",

    [string]$OutputDir = "",

    [string]$KeystorePath = "",
    [string]$KeyAlias = "noctra-upload",
    [string]$StorePass = "",
    [string]$KeyPass = "",

    [switch]$CreateKeystore,
    [switch]$SkipVerification,

    [int]$ValidityDays = 10000,
    [string]$DistinguishedName = "CN=Kynora Studio, OU=Mobile, O=Kynora Studio, C=TR"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$AndroidProject = Join-Path $RepoRoot "Noctra.Android\Noctra.Android.csproj"
$TargetFramework = "net10.0-android36.0"
$SecretsFileDefault = Join-Path $PSScriptRoot "android-signing.local.env"

if (-not $OutputDir) {
    $OutputDir = Join-Path $RepoRoot "artifacts\play"
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Noctra Play Bundle Builder" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ------------------------------------------------------------------
# JDK tools (keytool / jarsigner)
# ------------------------------------------------------------------
function Get-JavaTool {
    param([Parameter(Mandatory)][string]$Name)
    $candidates = @()
    if ($env:JAVA_HOME) { $candidates += (Join-Path $env:JAVA_HOME "bin\$Name.exe") }
    foreach ($root in @("C:\Program Files\Android\jdk", "C:\Program Files\Eclipse Adoptium")) {
        if (Test-Path $root) {
            $candidates += Get-ChildItem -Path $root -Recurse -Filter "$Name.exe" -ErrorAction SilentlyContinue |
                Select-Object -ExpandProperty FullName
        }
    }
    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    if ($cmd) { $candidates += $cmd.Source }
    return $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}

# ------------------------------------------------------------------
# Load secrets file first (lowest priority except defaults)
# ------------------------------------------------------------------
function Read-SecretsFile {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return @{} }
    $map = @{}
    foreach ($line in Get-Content $Path) {
        $trimmed = $line.Trim()
        if (-not $trimmed -or $trimmed.StartsWith("#") -or -not $trimmed.Contains("=")) { continue }
        $key, $value = $trimmed.Split("=", 2)
        $map[$key.Trim()] = $value.Trim()
    }
    return $map
}

function Get-EndpointConfigValue {
    param([Parameter(Mandatory)][string]$Name)

    $environmentValue = [Environment]::GetEnvironmentVariable($Name)
    if (-not [string]::IsNullOrWhiteSpace($environmentValue)) {
        return $environmentValue.Trim()
    }

    $envPath = Join-Path $RepoRoot ".env"
    if (Test-Path $envPath) {
        $prefix = $Name + "="
        foreach ($line in Get-Content $envPath) {
            $trimmed = $line.Trim()
            if ($trimmed.StartsWith($prefix, [StringComparison]::Ordinal)) {
                return $trimmed.Substring($prefix.Length).Trim()
            }
        }
    }

    return ""
}

$secrets = Read-SecretsFile $SecretsFileDefault

if (-not $KeystorePath -and $secrets.ContainsKey("KeystorePath")) { $KeystorePath = $secrets["KeystorePath"] }
if ($secrets.ContainsKey("KeyAlias")) { $KeyAlias = $secrets["KeyAlias"] }
if (-not $StorePass -and $env:NOCTRA_ANDROID_STORE_PASS) { $StorePass = $env:NOCTRA_ANDROID_STORE_PASS }
if (-not $KeyPass -and $env:NOCTRA_ANDROID_KEY_PASS) { $KeyPass = $env:NOCTRA_ANDROID_KEY_PASS }
if (-not $StorePass -and $secrets.ContainsKey("StorePass")) { $StorePass = $secrets["StorePass"] }
if (-not $KeyPass -and $secrets.ContainsKey("KeyPass")) { $KeyPass = $secrets["KeyPass"] }

# ------------------------------------------------------------------
# -CreateKeystore: generate the upload key once via keytool
# ------------------------------------------------------------------
$keytool = Get-JavaTool "keytool"
if ($CreateKeystore) {
    if (-not $keytool) {
        Write-Host "[ERROR] keytool not found. Install a JDK and set JAVA_HOME." -ForegroundColor Red
        exit 1
    }
    if (-not $KeystorePath) {
        Write-Host "[ERROR] -KeystorePath is required with -CreateKeystore." -ForegroundColor Red
        exit 1
    }
    $keystoreFull = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($KeystorePath)
    if (Test-Path $keystoreFull) {
        Write-Host "[OK] Keystore already exists: $keystoreFull (nothing created)." -ForegroundColor Green
        Write-Host "     NEVER regenerate or overwrite it; back it up in two safe places." -ForegroundColor Yellow
        exit 0
    }
    $parent = Split-Path -Parent $keystoreFull
    if ($parent -and -not (Test-Path $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    # Non-interactive mode: when both passwords are known up front (params,
    # env vars or the secrets file), pass them straight to keytool so the
    # command can run unattended (CI / agent sessions).
    $genArgs = @(
        "-genkeypair", "-v",
        "-keystore", $keystoreFull,
        "-alias", $KeyAlias,
        "-keyalg", "RSA", "-keysize", "2048", "-validity", "$ValidityDays",
        "-dname", $DistinguishedName
    )
    if ($StorePass -and $KeyPass) {
        $genArgs += @("-storepass", $StorePass)
        if ($KeyPass -ne $StorePass) { $genArgs += @("-keypass", $KeyPass) }
    } else {
        Write-Host "[..] Creating upload keystore. You will be prompted for:" -ForegroundColor DarkGray
        Write-Host "       keystore password, key password (can match). Identity fields are preset."
    }
    & $keytool @genArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[FAIL] keytool failed." -ForegroundColor Red
        exit 1
    }
    Write-Host "[OK] Upload keystore created: $keystoreFull" -ForegroundColor Green
    Write-Host "     Back it up (passwords included) in TWO safe places NOW." -ForegroundColor Yellow
    Write-Host "     Losing it means losing the ability to update the app under" -ForegroundColor Yellow
    Write-Host "     the same certificate unless Play support resets the key." -ForegroundColor Yellow
    exit 0
}

# ------------------------------------------------------------------
# Validate signing inputs
# ------------------------------------------------------------------
if (-not $KeystorePath) {
    Write-Host "[ERROR] No keystore configured." -ForegroundColor Red
    Write-Host "        Create one:  .\build\package-play.ps1 -CreateKeystore -KeystorePath C:\Keys\noctra-upload.keystore" -ForegroundColor White
    Write-Host "        Or set KeystorePath in: $SecretsFileDefault" -ForegroundColor White
    exit 1
}
if (-not $StorePass -or -not $KeyPass) {
    Write-Host "[ERROR] Signing passwords not provided." -ForegroundColor Red
    Write-Host "        Set NOCTRA_ANDROID_STORE_PASS / NOCTRA_ANDROID_KEY_PASS env vars," -ForegroundColor White
    Write-Host "        or add them to: $SecretsFileDefault" -ForegroundColor White
    exit 1
}

$keystoreResolved = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($KeystorePath)
if (-not (Test-Path $keystoreResolved)) {
    Write-Host "[ERROR] Keystore not found: $keystoreResolved" -ForegroundColor Red
    Write-Host "        Create it:  .\build\package-play.ps1 -CreateKeystore -KeystorePath `"$KeystorePath`"" -ForegroundColor White
    exit 1
}

if ($StorePass.Length -lt 6) {
    Write-Host "[WARN] Store password looks short (<6 chars); keytool requires >=6." -ForegroundColor Yellow
}

# A Play bundle without the verifier URL can display a Play purchase window,
# but it can never verify the resulting token and therefore cannot grant the
# Premium entitlement. Fail before publishing instead of producing a broken
# Internal Testing artifact. -CreateKeystore exits earlier and remains usable
# without a configured billing backend.
$BillingVerifyUrl = Get-EndpointConfigValue "NOCTRA_BILLING_VERIFY_URL"
if ([string]::IsNullOrWhiteSpace($BillingVerifyUrl)) {
    Write-Host "[ERROR] NOCTRA_BILLING_VERIFY_URL is missing." -ForegroundColor Red
    Write-Host "        Deploy Noctra.Billing.Api first or set it in .env / the process environment." -ForegroundColor White
    exit 1
}

$billingUri = $null
if (-not [Uri]::TryCreate($BillingVerifyUrl, [UriKind]::Absolute, [ref]$billingUri) -or
    $billingUri.Scheme -ne "https" -or
    [string]::IsNullOrWhiteSpace($billingUri.Host)) {
    Write-Host "[ERROR] NOCTRA_BILLING_VERIFY_URL must be an absolute HTTPS URL." -ForegroundColor Red
    exit 1
}

$BillingApiKey = Get-EndpointConfigValue "NOCTRA_BILLING_API_KEY"

Write-Host "Project:   $AndroidProject"
Write-Host "Config:    $Configuration ($TargetFramework)"
Write-Host "Keystore:  $keystoreResolved"
Write-Host "Alias:     $KeyAlias"
Write-Host "Billing:   configured (URL is not printed)"
Write-Host ""

# ------------------------------------------------------------------
# Publish the signed bundle
# ------------------------------------------------------------------
# NOTE: passwords are passed as MSBuild properties on the command line only;
# they are never persisted into any project file. The `env:` property prefix
# is intentionally avoided because .NET for Android does not reliably support
# it when producing AABs.
$publishArgs = @(
    "publish", $AndroidProject,
    "-c", $Configuration,
    "-f", $TargetFramework,
    "-p:AndroidKeyStore=true",
    "-p:AndroidSigningKeyStore=$keystoreResolved",
    "-p:AndroidSigningKeyAlias=$KeyAlias",
    "-p:AndroidSigningStorePass=$StorePass",
    "-p:AndroidSigningKeyPass=$KeyPass",
    "-p:NOCTRA_BILLING_VERIFY_URL=$BillingVerifyUrl"
)
if (-not [string]::IsNullOrWhiteSpace($BillingApiKey)) {
    $publishArgs += "-p:NOCTRA_BILLING_API_KEY=$BillingApiKey"
}
Write-Host "[..] dotnet publish (this can take several minutes)..." -ForegroundColor DarkGray
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FAIL] dotnet publish failed." -ForegroundColor Red
    exit 1
}

# Locate the freshest .aab under the Android project's bin folder
$aab = Get-ChildItem -Path (Join-Path $RepoRoot "Noctra.Android\bin") -Recurse -Filter "*.aab" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match [regex]::Escape($Configuration) } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $aab) {
    Write-Host "[ERROR] No .aab found under Noctra.Android\bin after publish." -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$dest = Join-Path $OutputDir $aab.Name
Copy-Item $aab.FullName $dest -Force

# ------------------------------------------------------------------
# Verify the signature (best effort)
# ------------------------------------------------------------------
if (-not $SkipVerification) {
    $jarsigner = Get-JavaTool "jarsigner"
    if ($jarsigner) {
        Write-Host "[..] Verifying signature with jarsigner..." -ForegroundColor DarkGray
        & $jarsigner -verify -verbose:summary -certs $dest | Select-String -Pattern "jar verified|unsigned|security" -Context 0,2
        if ($LASTEXITCODE -eq 0) {
            Write-Host "[OK] Bundle signature verified." -ForegroundColor Green
        } else {
            Write-Host "[ERROR] jarsigner could NOT verify the bundle. Do NOT upload it." -ForegroundColor Red
            exit 1
        }
    } else {
        Write-Host "[WARN] jarsigner not found; skipping signature verification." -ForegroundColor Yellow
    }
}

# ------------------------------------------------------------------
# Summary
# ------------------------------------------------------------------
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "[OK] Signed bundle ready:" -ForegroundColor Green
Write-Host "     $dest"
Write-Host "     Size: $([math]::Round((Get-Item $dest).Length / 1MB, 1)) MB"
Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Play Console -> Internal testing -> upload this .aab"
Write-Host "  2. Complete Play App Signing with the Google-generated app signing key"
Write-Host "  3. Always sign future bundles with THIS SAME keystore/alias"
Write-Host ""
