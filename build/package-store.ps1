[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$OutputDir = "",
    [string]$VersionPrefix = "",
    [string[]]$Editions = @("Free", "Premium"),
    [switch]$SkipTests,
    [switch]$EnableSigning
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Get-MsBuildPath {
    $vsWhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vsWhere) {
        $path = & $vsWhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
        if ($path) {
            return $path
        }
    }

    $fallbacks = @(
        "C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    )

    foreach ($candidate in $fallbacks) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "MSBuild with Desktop Bridge support was not found. Install Visual Studio 2022 Build Tools or Visual Studio with MSIX/Windows Application Packaging support."
}

function Invoke-Step {
    param(
        [string]$Name,
        [scriptblock]$Action
    )

    Write-Host "==> $Name"
    & $Action
}

function Get-PackageVersion {
    param([string]$Prefix)

    if ([string]::IsNullOrWhiteSpace($Prefix)) {
        return ""
    }

    if ($Prefix -match '^\d+\.\d+\.\d+\.\d+$') {
        return $Prefix
    }

    if ($Prefix -match '^\d+\.\d+\.\d+$') {
        return "$Prefix.0"
    }

    throw "VersionPrefix must be in Major.Minor.Patch or Major.Minor.Patch.Revision format."
}

function Get-EditionProfiles {
    param([string]$RepoRoot)

    $profilePath = Join-Path $RepoRoot "Noctra.Packaging\store-profiles.json"
    if (-not (Test-Path $profilePath)) {
        throw "Store profile config was not found: $profilePath"
    }

    $json = Get-Content $profilePath -Raw | ConvertFrom-Json
    $profiles = @{}

    foreach ($property in ($json | Get-Member -MemberType NoteProperty)) {
        $profile = $json.($property.Name)
        $profiles[$property.Name] = @{
            edition = [string]$profile.edition
            editionDisplayName = [string]$profile.editionDisplayName
            appDisplayName = [string]$profile.appDisplayName
            storeIdentityName = [string]$profile.storeIdentityName
            storePublisher = [string]$profile.storePublisher
            storePublisherDisplayName = [string]$profile.storePublisherDisplayName
            premiumStoreProductId = [string]$profile.premiumStoreProductId
            premiumStoreLaunchUri = [string]$profile.premiumStoreLaunchUri
            premiumStoreWebUri = [string]$profile.premiumStoreWebUri
        }
    }

    return $profiles
}

$repoRoot = Get-RepoRoot
Set-Location $repoRoot

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "artifacts\store"
}

$msbuildPath = Get-MsBuildPath
$packageVersion = Get-PackageVersion -Prefix $VersionPrefix
$signingEnabled = if ($EnableSigning) { "true" } else { "false" }
$editionProfiles = Get-EditionProfiles -RepoRoot $repoRoot
$normalizedOutputDir = (Resolve-Path (New-Item -ItemType Directory -Force -Path $OutputDir)).Path
if (-not $normalizedOutputDir.EndsWith("\")) {
    $normalizedOutputDir += "\"
}

Invoke-Step "Restore solution" {
    dotnet restore Noctra.sln
}

Invoke-Step "Build solution" {
    dotnet build Noctra.sln -c $Configuration
}

if (-not $SkipTests) {
    Invoke-Step "Run tests" {
        dotnet test Noctra.Tests\Noctra.Tests.csproj -c $Configuration --no-build
    }
}

foreach ($edition in $Editions) {
    $normalizedEdition = $edition.ToLowerInvariant()
    if (-not $editionProfiles.ContainsKey($normalizedEdition)) {
        throw "Unknown edition '$edition'. Valid editions: $($editionProfiles.Keys -join ', ')"
    }

    $profile = $editionProfiles[$normalizedEdition]
    $editionOutputDir = Join-Path $normalizedOutputDir $normalizedEdition
    New-Item -ItemType Directory -Force -Path $editionOutputDir | Out-Null
    if (-not $editionOutputDir.EndsWith("\")) {
        $editionOutputDir += "\"
    }

    $msbuildArgs = @(
        "Noctra.Packaging\Noctra.Packaging.wapproj",
        "/restore",
        "/p:Configuration=$Configuration",
        "/p:Platform=$Platform",
        "/p:GenerateAppxPackageOnBuild=true",
        "/p:UapAppxPackageBuildMode=StoreUpload",
        "/p:AppxBundle=Never",
        "/p:AppxPackageDir=$editionOutputDir",
        "/p:AppxPackageSigningEnabled=$signingEnabled",
        "/p:NoctraEdition=$($profile.edition)",
        "/p:NoctraEditionDisplayName=$($profile.editionDisplayName)",
        "/p:NoctraAppDisplayName=$($profile.appDisplayName)",
        "/p:StoreIdentityName=$($profile.storeIdentityName)",
        "/p:StorePublisher=$($profile.storePublisher)",
        "/p:StorePublisherDisplayName=$($profile.storePublisherDisplayName)",
        "/p:NoctraPremiumStoreProductId=$($profile.premiumStoreProductId)",
        "/p:NoctraPremiumStoreLaunchUri=$($profile.premiumStoreLaunchUri)",
        "/p:NoctraPremiumStoreWebUri=$($profile.premiumStoreWebUri)"
    )

    if ($packageVersion) {
        $msbuildArgs += "/p:NoctraVersionPrefix=$VersionPrefix"
        $msbuildArgs += "/p:NoctraPackageVersion=$packageVersion"
    }

    Invoke-Step "Build Store package ($edition)" {
        & $msbuildPath @msbuildArgs
    }
}

Write-Host "Store package output: $normalizedOutputDir"
