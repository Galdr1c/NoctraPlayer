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
    foreach ($storeName in @("Cert:\CurrentUser\Root", "Cert:\CurrentUser\TrustedPeople")) {
        $alreadyTrusted = Get-ChildItem $storeName -ErrorAction SilentlyContinue |
            Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
        if (-not $alreadyTrusted) {
            try {
                Import-Certificate -FilePath $cerPath -CertStoreLocation $storeName | Out-Null
            } catch {
                Write-Host "[WARN] Certificate import to $storeName failed: $($_.Exception.Message)" -ForegroundColor Yellow
            }
        }
    }

    [PSCustomObject]@{
        PfxPath = $pfxPath
        CerPath = $cerPath
        Password = "NoctraLocalTest123!"
    }
}

# ------------------------------------------------------------------
# Partner Center symbol package
# ------------------------------------------------------------------
# Creates a ZIP of the .pdb/.dll/.exe files from the newest Release build
# output. Upload it to Partner Center > Health > Failures > Upload symbols so
# future crashes/hangs resolve to meaningful stack traces instead of
# "Uncategorized". PDBs are produced by default (DebugType=portable) in
# Release builds.
function New-SymbolZip {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$EditionKey,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Platform,
        [Parameter(Mandatory = $true)][string]$OutputDir
    )

    $debugLog = Join-Path $OutputDir "symbol-zip-debug.log"
    $trace = @()
    $trace += "EditionKey=$EditionKey Version=$Version"

    $appBin = Join-Path $RepoRoot "Noctra.Avalonia\bin"
    $trace += "appBin=$appBin"
    $pdb = Get-ChildItem -Path $appBin -Recurse -Filter "Noctra.pdb" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "Release" } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if (-not $pdb) {
        Write-Host "[WARN] Noctra.pdb not found under $appBin - symbols zip skipped." -ForegroundColor Yellow
        try { [System.IO.File]::AppendAllText($debugLog, "[trace] $($trace -join ' | ') | NO_PDB`r`n") } catch { }
        return $null
    }
    $trace += "pdb=$($pdb.FullName)"

    # Windows PowerShell 5.1's Get-ChildItem -Include is unreliable without a
    # wildcard in -Path (can emit $null/empty results even with -Recurse). Use an
    # explicit glob and materialize a plain string list of symbol files.
    $symbolGlob = Join-Path $pdb.DirectoryName '*'
    $trace += "symbolGlob=$symbolGlob"
    $symbols = @(
        Get-ChildItem -Path $symbolGlob -Recurse -File -Include "*.pdb", "*.dll", "*.exe" -ErrorAction SilentlyContinue |
            Where-Object { $null -ne $_ -and $null -ne $_.FullName }
    )
    $trace += "symbols.Count=$($symbols.Count)"
    if ($symbols.Count -gt 0) {
        $trace += "symbols0=$($symbols[0].GetType().FullName):$($symbols[0].FullName)"
    }
    if ($symbols.Count -eq 0) {
        Write-Host "[WARN] No symbol candidates under $($pdb.DirectoryName) - symbols zip skipped." -ForegroundColor Yellow
        try { [System.IO.File]::AppendAllText($debugLog, "[trace] $($trace -join ' | ') | NO_SYMBOLS`r`n") } catch { }
        return $null
    }

    $fullNames = @($symbols | ForEach-Object { $_.FullName } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $trace += "fullNames.Count=$($fullNames.Count)"
    if ($fullNames.Count -eq 0) {
        Write-Host "[WARN] Symbol list is empty after filtering - symbols zip skipped." -ForegroundColor Yellow
        try { [System.IO.File]::AppendAllText($debugLog, "[trace] $($trace -join ' | ') | EMPTY_AFTER_FILTER`r`n") } catch { }
        return $null
    }

    $zipPath = Join-Path $OutputDir "Noctra.${EditionKey}_${Version}_${Platform}_Symbols.zip"
    $trace += "zipPath=$zipPath"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

    # Use System.IO.Compression directly: Compress-Archive in Windows PowerShell
    # 5.1 has several quirks (ValidateNotNullOrEmpty failures on -Path, 2GB limit,
    # large file-set issues) that make it unreliable for packaging runs.
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $base = $pdb.DirectoryName
        foreach ($file in $fullNames) {
            $relative = $file.Substring($base.Length).TrimStart('\', '/').Replace('\', '/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file, $relative, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally {
        $zip.Dispose()
    }

    $trace += "zipBytes=$((Get-Item $zipPath).Length)"
    try { [System.IO.File]::AppendAllText($debugLog, "[trace] $($trace -join ' | ') | OK`r`n") } catch { }
    return $zipPath
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
$readmeFile = Join-Path $RepoRoot "README.md"
Write-Host ""
Write-Host "Version:       $VersionPrefix ($packageVersion)" -ForegroundColor White
Write-Host "Configuration: $Configuration" -ForegroundColor White
Write-Host "Platform:      $Platform" -ForegroundColor White
Write-Host "Editions:      $($Editions -join ', ')" -ForegroundColor White
Write-Host "Output:        $OutputDir" -ForegroundColor White
Write-Host "README badge:  $readmeFile" -ForegroundColor White
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
        "/p:NoctraStoreProductId=$($profile.storeProductId)",
        "/p:NoctraStoreReviewLaunchUri=$($profile.storeReviewLaunchUri)",
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
            $destName = "Noctra.$($profile.edition)_${packageVersion}_$Platform.$($pkg.Extension.TrimStart('.'))"
            $dest = Join-Path $OutputDir $destName
            Copy-Item $pkg.FullName $dest -Force
            Write-Host "[OK] Package copied: $dest" -ForegroundColor Green
        }
    }

    # Create Partner Center symbol package for this edition's Release build.
    # Deliberately non-fatal: a symbol-zip hiccup must never block the MSIX packages.
    try {
        $symbolZip = New-SymbolZip `
            -RepoRoot $RepoRoot `
            -EditionKey $profile.edition `
            -Version $packageVersion `
            -Platform $Platform `
            -OutputDir $OutputDir
        if ($symbolZip) {
            Write-Host "[OK] Partner Center symbols zip: $symbolZip" -ForegroundColor Green
            Write-Host "     Upload at Partner Center > Health > Failures > Upload symbols." -ForegroundColor DarkGray
        }
    } catch {
        $debugLog = Join-Path $OutputDir "symbol-zip-debug.log"
        $details = "Edition=$edition Version=$packageVersion`r`n$($_.Exception.ToString())`r`n$($_.ScriptStackTrace)"
        try { [System.IO.File]::AppendAllText($debugLog, "[$(Get-Date -Format o)] $details`r`n`r`n") } catch { }
        Write-Host "[WARN] Partner Center symbols zip failed for $edition; package build continues. Details: $debugLog" -ForegroundColor Yellow
    }

    $results += [PSCustomObject]@{ Edition = $edition; Status = "SUCCESS" }
    Write-Host "[OK] $edition build completed." -ForegroundColor Green
    Write-Host ""
}

# ------------------------------------------------------------------
# Update README version badge (only if all builds succeeded)
# ------------------------------------------------------------------
if ($results.Status -notcontains "FAILED") {
    if (Test-Path $readmeFile) {
        $readmeContent = Get-Content $readmeFile -Raw
        $oldBadgePattern = 'version-[0-9]+\.[0-9]+\.[0-9]+'
        $newBadgeReplacement = "version-$VersionPrefix"
        $readmeContent = [regex]::Replace($readmeContent, $oldBadgePattern, $newBadgeReplacement)
        $oldAltPattern = 'alt="Version [0-9]+\.[0-9]+\.[0-9]+"'
        $newAltReplacement = 'alt="Version ' + $VersionPrefix + '"'
        $readmeContent = [regex]::Replace($readmeContent, $oldAltPattern, $newAltReplacement)
        [System.IO.File]::WriteAllText($readmeFile, $readmeContent, [System.Text.UTF8Encoding]::new($false))
        Write-Host "[OK] README.md version badge updated to $VersionPrefix" -ForegroundColor Green
    } else {
        Write-Host "[WARN] README.md not found, badge update skipped." -ForegroundColor Yellow
    }
} else {
    Write-Host "[SKIP] README.md badge not updated due to build failure." -ForegroundColor Yellow
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
