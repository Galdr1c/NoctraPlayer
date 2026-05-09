<#
.SYNOPSIS
    Runs Windows App Certification Kit (WACK) against the latest Noctra package.

.DESCRIPTION
    Searches for the newest .msix or .appx package in the standard output
    directories and runs appcert.exe (WACK) against it.

.PARAMETER PackagePath
    Optional explicit path to a .msix/.appx file. If omitted, the script
    searches artifacts/store and Noctra.Packaging/AppPackages.

.EXAMPLE
    .\build\test-store-package.ps1
    .\build\test-store-package.ps1 -PackagePath ".\artifacts\store\Noctra.msix"
#>

param(
    [string]$PackagePath = ""
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot

# ------------------------------------------------------------------
# Find WACK
# ------------------------------------------------------------------
$wackPaths = @(
    "${env:ProgramFiles(x86)}\Windows Kits\10\App Certification Kit\appcert.exe",
    "${env:ProgramFiles}\Windows Kits\10\App Certification Kit\appcert.exe"
)

$appcert = $null
foreach ($p in $wackPaths) {
    if (Test-Path $p) {
        $appcert = $p
        break
    }
}

if (-not $appcert) {
    Write-Host "[ERROR] Windows App Certification Kit (appcert.exe) not found." -ForegroundColor Red
    Write-Host "        Install it via Visual Studio Installer or Windows SDK." -ForegroundColor Red
    exit 1
}
Write-Host "[OK] WACK found: $appcert" -ForegroundColor Green

# ------------------------------------------------------------------
# Find package
# ------------------------------------------------------------------
if (-not $PackagePath) {
    $searchDirs = @(
        (Join-Path $RepoRoot "artifacts\store"),
        (Join-Path $RepoRoot "Noctra.Packaging\AppPackages")
    )

    $packages = @()
    foreach ($dir in $searchDirs) {
        if (Test-Path $dir) {
            $packages += Get-ChildItem -Path $dir -Recurse -Include "*.msix", "*.appx" | Sort-Object LastWriteTime -Descending
        }
    }

    if ($packages.Count -eq 0) {
        Write-Host "[ERROR] No .msix or .appx package found in:" -ForegroundColor Red
        foreach ($dir in $searchDirs) { Write-Host "        $dir" -ForegroundColor Red }
        Write-Host ""
        Write-Host "Run .\build\package-store.ps1 first to build a package." -ForegroundColor Yellow
        exit 1
    }

    $PackagePath = $packages[0].FullName
}

if (-not (Test-Path $PackagePath)) {
    Write-Host "[ERROR] Package not found: $PackagePath" -ForegroundColor Red
    exit 1
}

Write-Host "[OK] Testing package: $PackagePath" -ForegroundColor Green
Write-Host ""

# ------------------------------------------------------------------
# Run WACK
# ------------------------------------------------------------------
$reportDir = Join-Path $RepoRoot "artifacts\wack-reports"
if (-not (Test-Path $reportDir)) {
    New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
}

$timestamp = Get-Date -Format "yyyy-MM-dd_HHmmss"
$reportPath = Join-Path $reportDir "wack_report_$timestamp.xml"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Running Windows App Certification Kit" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Package: $PackagePath"
Write-Host "Report:  $reportPath"
Write-Host ""

& $appcert test -appxpackagepath $PackagePath -reportoutputpath $reportPath

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "[PASS] All WACK tests passed!" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "[FAIL] Some WACK tests failed. Review the report:" -ForegroundColor Red
    Write-Host "       $reportPath" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Full report saved to: $reportPath"
