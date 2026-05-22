<#
.SYNOPSIS
    Builds Noctra MSIX Store packages for submission to the Microsoft Store.

.DESCRIPTION
    Reads edition profiles from Noctra.Packaging/store-profiles.json,
    builds the WAP packaging project for each selected edition, and copies
    the resulting MSIX/APPX packages to artifacts/store/.

    Requires Visual Studio 2022 with MSIX Packaging Tools or equivalent
    Desktop Bridge build targets installed.

.PARAMETER Editions
    Which editions to build. Default: both Free and Premium.
    Examples: -Editions Free   |   -Editions Premium   |   -Editions Free,Premium

.PARAMETER VersionPrefix
    Semantic version prefix (e.g. 1.2.0). Defaults to 1.0.0.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER Platform
    Target platform. Defaults to x64.

.PARAMETER OutputDir
    Output directory for packages. Defaults to artifacts/store.

.PARAMETER SignForSideload
    Builds a locally installable signed MSIX using a self-signed test
    certificate. Store uploads should normally leave this off.

.EXAMPLE
    .\build\package-store.ps1
    .\build\package-store.ps1 -Editions Free
    .\build\package-store.ps1 -Editions Premium -VersionPrefix 2.1.0
#>

param(
    [ValidateSet("Free", "Premium")]
    [string[]]$Editions = @("Free", "Premium"),

    [string]$VersionPrefix = "1.0.0",

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$Platform = "x64",

    [string]$OutputDir = "",

    [switch]$SignForSideload
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$PackagingDir = Join-Path $RepoRoot "Noctra.Packaging"
$ProfilesFile = Join-Path $PackagingDir "store-profiles.json"
$CertificatesDir = Join-Path $PackagingDir "Certificates"

if (-not $OutputDir) {
    $OutputDir = Join-Path $RepoRoot "artifacts\store"
}

# ------------------------------------------------------------------
# Validate prerequisites
# ------------------------------------------------------------------
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Noctra Store Package Builder" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check MSBuild
$msbuild = $null
$vsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vsWhere) {
    $msbuild = & $vsWhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
}
if (-not $msbuild) {
    $msbuildCandidates = @(
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    )
    $msbuild = $msbuildCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $msbuild -or -not (Test-Path $msbuild)) {
    Write-Host "[ERROR] MSBuild not found. Install Visual Studio 2022 with MSIX Packaging Tools." -ForegroundColor Red
    Write-Host "        dotnet CLI alone cannot build .wapproj packaging projects." -ForegroundColor Red
    exit 1
}
Write-Host "[OK] MSBuild: $msbuild" -ForegroundColor Green

# Check Desktop Bridge targets before invoking MSBuild. Without these targets,
# .wapproj files fail with a misleading "Build target does not exist" error.
$desktopBridgeCandidates = @(
    "${env:ProgramFiles(x86)}\MSBuild\Microsoft\DesktopBridge\Microsoft.DesktopBridge.props",
    "${env:ProgramFiles}\MSBuild\Microsoft\DesktopBridge\Microsoft.DesktopBridge.props",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Microsoft\DesktopBridge\Microsoft.DesktopBridge.props",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\Community\MSBuild\Microsoft\DesktopBridge\Microsoft.DesktopBridge.props",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\Professional\MSBuild\Microsoft\DesktopBridge\Microsoft.DesktopBridge.props",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Microsoft\DesktopBridge\Microsoft.DesktopBridge.props"
)
$desktopBridgeProps = $desktopBridgeCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $desktopBridgeProps) {
    Write-Host "[ERROR] Microsoft Desktop Bridge build targets were not found." -ForegroundColor Red
    Write-Host "        Install Visual Studio 2022/Build Tools with Windows Application Packaging/MSIX tooling." -ForegroundColor Red
    exit 1
}
Write-Host "[OK] Desktop Bridge targets: $desktopBridgeProps" -ForegroundColor Green

function Ensure-SideloadCertificate {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Publisher
    )

    if (-not (Test-Path $CertificatesDir)) {
        New-Item -ItemType Directory -Path $CertificatesDir -Force | Out-Null
    }

    $safeName = ($Publisher -replace '[^A-Za-z0-9.-]', '_')
    $pfxPath = Join-Path $CertificatesDir "$safeName.pfx"
    $cerPath = Join-Path $CertificatesDir "$safeName.cer"
    $password = ConvertTo-SecureString -String "NoctraLocalTest123!" -Force -AsPlainText
    $cert = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq $Publisher -and $_.NotAfter -gt (Get-Date).AddDays(30) } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1

    if (-not $cert) {
        $cert = New-SelfSignedCertificate `
            -Type Custom `
            -Subject $Publisher `
            -KeyUsage DigitalSignature `
            -FriendlyName "Noctra Local MSIX Test Certificate" `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
    }

    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $password -Force | Out-Null
    Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null
    Import-Certificate -FilePath $cerPath -CertStoreLocation Cert:\CurrentUser\Root | Out-Null
    Import-Certificate -FilePath $cerPath -CertStoreLocation Cert:\CurrentUser\TrustedPeople | Out-Null

    [PSCustomObject]@{
        PfxPath = $pfxPath
        CerPath = $cerPath
        Password = "NoctraLocalTest123!"
    }
}

# Check profiles
if (-not (Test-Path $ProfilesFile)) {
    Write-Host "[ERROR] Store profiles not found: $ProfilesFile" -ForegroundColor Red
    exit 1
}
$profiles = Get-Content $ProfilesFile -Raw | ConvertFrom-Json
Write-Host "[OK] Profiles loaded: $ProfilesFile" -ForegroundColor Green

# Prepare output
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$packageVersion = "$VersionPrefix.0"
$manifestFile = Join-Path $PackagingDir "Package.appxmanifest"
$manifestTemplate = Get-Content $manifestFile -Raw
Write-Host ""
Write-Host "Version:       $VersionPrefix ($packageVersion)" -ForegroundColor White
Write-Host "Configuration: $Configuration" -ForegroundColor White
Write-Host "Platform:      $Platform" -ForegroundColor White
Write-Host "Editions:      $($Editions -join ', ')" -ForegroundColor White
Write-Host "Output:        $OutputDir" -ForegroundColor White
Write-Host ""

# ------------------------------------------------------------------
# Build each edition
# ------------------------------------------------------------------
$results = @()
foreach ($edition in $Editions) {
    $profileKey = $edition.ToLower()
    $profile = $profiles.$profileKey

    if (-not $profile) {
        Write-Host "[WARN] No profile found for edition '$edition', skipping." -ForegroundColor Yellow
        continue
    }

    Write-Host "----------------------------------------" -ForegroundColor DarkGray
    Write-Host "Building: $($profile.appDisplayName) ($edition)" -ForegroundColor Cyan
    Write-Host "----------------------------------------" -ForegroundColor DarkGray

    $wapproj = Join-Path $PackagingDir "Noctra.Packaging.wapproj"
    $packagingBinDir = Join-Path $PackagingDir "bin"
    $packagingObjDir = Join-Path $PackagingDir "obj"
    $appPackagesDir = Join-Path $PackagingDir "AppPackages"
    foreach ($generatedDir in @($packagingBinDir, $packagingObjDir, $appPackagesDir)) {
        if ((Test-Path $generatedDir) -and ((Resolve-Path $generatedDir).Path.StartsWith((Resolve-Path $PackagingDir).Path, [StringComparison]::OrdinalIgnoreCase))) {
            Remove-Item -LiteralPath $generatedDir -Recurse -Force
        }
    }

    $renderedManifest = $manifestTemplate.
        Replace('$(StoreIdentityName)', [string]$profile.storeIdentityName).
        Replace('$(StorePublisher)', [string]$profile.storePublisher).
        Replace('$(NoctraPackageVersion)', [string]$packageVersion).
        Replace('$(NoctraAppDisplayName)', [string]$profile.appDisplayName).
        Replace('$(StorePublisherDisplayName)', [string]$profile.storePublisherDisplayName)

    $signingArgs = @("/p:AppxPackageSigningEnabled=false")
    if ($SignForSideload) {
        $certificate = Ensure-SideloadCertificate -Publisher $profile.storePublisher
        Write-Host "[OK] Sideload certificate trusted for current user: $($certificate.CerPath)" -ForegroundColor Green
        $signingArgs = @(
            "/p:AppxPackageSigningEnabled=true",
            "/p:PackageCertificateKeyFile=$($certificate.PfxPath)",
            "/p:PackageCertificatePassword=$($certificate.Password)"
        )
    }

    $msbuildArgs = @(
        $wapproj,
        "/t:Build",
        "/p:Configuration=$Configuration",
        "/p:Platform=$Platform",
        "/p:UapAppxPackageBuildMode=StoreUpload",
        "/p:AppxBundle=Never",
        "/p:BuildingStorePackage=true",
        "/p:RuntimeIdentifier=win-x64",
        "/p:SelfContained=true",
        "/p:WindowsPackageType=MSIX",
        "/p:EnableMsixTooling=true",
        "/p:NoctraVersionPrefix=$VersionPrefix",
        "/p:NoctraPackageVersion=$packageVersion",
        "/p:NoctraEdition=$($profile.edition)",
        "/p:NoctraEditionDisplayName=$($profile.editionDisplayName)",
        "/p:NoctraAppDisplayName=$($profile.appDisplayName)",
        "/p:NoctraAuthors=$($profile.storePublisherDisplayName)",
        "/p:NoctraCompany=$($profile.storePublisherDisplayName)",
        "/p:StoreIdentityName=$($profile.storeIdentityName)",
        "/p:StorePublisher=$($profile.storePublisher)",
        "/p:StorePublisherDisplayName=$($profile.storePublisherDisplayName)",
        "/p:NoctraPremiumStoreProductId=$($profile.premiumStoreProductId)",
        "/p:NoctraPremiumStoreLaunchUri=$($profile.premiumStoreLaunchUri)",
        "/p:NoctraPremiumStoreWebUri=$($profile.premiumStoreWebUri)",
        "/restore",
        "/v:minimal"
    )
    $msbuildArgs += $signingArgs

    Write-Host "Running MSBuild..." -ForegroundColor DarkGray
    Set-Content -Path $manifestFile -Value $renderedManifest -Encoding UTF8
    try {
        & $msbuild @msbuildArgs
    }
    finally {
        Set-Content -Path $manifestFile -Value $manifestTemplate -Encoding UTF8
    }

    if ($LASTEXITCODE -ne 0) {
        Write-Host "[FAIL] Build failed for $edition edition." -ForegroundColor Red
        $results += [PSCustomObject]@{ Edition = $edition; Status = "FAILED" }
        continue
    }

    # Copy output packages to artifacts/store
    if (Test-Path $appPackagesDir) {
        $packages = Get-ChildItem -Path $appPackagesDir -Recurse -Include "*.msix", "*.appx", "*.msixupload", "*.appxupload" | Sort-Object LastWriteTime -Descending
        foreach ($pkg in $packages) {
            $destName = "$($profile.appDisplayName -replace ' ','_')_${VersionPrefix}_$($pkg.Extension.TrimStart('.'))"
            # Keep original extension-based name if simpler
            $dest = Join-Path $OutputDir $pkg.Name
            Copy-Item $pkg.FullName $dest -Force
            Write-Host "[OK] Package copied: $dest" -ForegroundColor Green
        }
    }

    $results += [PSCustomObject]@{ Edition = $edition; Status = "SUCCESS" }
    Write-Host "[OK] $edition build completed." -ForegroundColor Green
    Write-Host ""
}

# ------------------------------------------------------------------
# Summary
# ------------------------------------------------------------------
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Build Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
$results | Format-Table -AutoSize
Write-Host "Output directory: $OutputDir"
Write-Host ""

if ($results.Status -contains "FAILED") {
    exit 1
}
