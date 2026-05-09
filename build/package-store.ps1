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

    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$PackagingDir = Join-Path $RepoRoot "Noctra.Packaging"
$ProfilesFile = Join-Path $PackagingDir "store-profiles.json"

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
if (-not $msbuild -or -not (Test-Path $msbuild)) {
    Write-Host "[ERROR] MSBuild not found. Install Visual Studio 2022 with MSIX Packaging Tools." -ForegroundColor Red
    Write-Host "        dotnet CLI alone cannot build .wapproj packaging projects." -ForegroundColor Red
    exit 1
}
Write-Host "[OK] MSBuild: $msbuild" -ForegroundColor Green

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

    $msbuildArgs = @(
        $wapproj,
        "/t:Build",
        "/p:Configuration=$Configuration",
        "/p:Platform=$Platform",
        "/p:UapAppxPackageBuildMode=StoreUpload",
        "/p:AppxPackageSigningEnabled=false",
        "/p:AppxBundle=Never",
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

    Write-Host "Running MSBuild..." -ForegroundColor DarkGray
    & $msbuild @msbuildArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Host "[FAIL] Build failed for $edition edition." -ForegroundColor Red
        $results += [PSCustomObject]@{ Edition = $edition; Status = "FAILED" }
        continue
    }

    # Copy output packages to artifacts/store
    $appPackagesDir = Join-Path $PackagingDir "AppPackages"
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
