[CmdletBinding()]
param(
    [string]$PackagePath = "",
    [string]$ReportPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Get-AppCertPath {
    $candidates = @(
        "C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe",
        "C:\Program Files\Windows Kits\10\App Certification Kit\appcert.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "Windows App Certification Kit was not found. Install the Windows 10/11 SDK with App Certification Kit support."
}

function Resolve-PackagePath {
    param([string]$RepoRoot, [string]$RequestedPath)

    if ($RequestedPath) {
        return (Resolve-Path $RequestedPath).Path
    }

    $searchRoots = @(
        (Join-Path $RepoRoot "artifacts\store"),
        (Join-Path $RepoRoot "Noctra.Packaging\AppPackages")
    )

    foreach ($root in $searchRoots) {
        if (-not (Test-Path $root)) {
            continue
        }

        $package = Get-ChildItem $root -Recurse -Include *.msix, *.msixbundle, *.appx, *.appxbundle |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1

        if ($package) {
            return $package.FullName
        }
    }

    throw "No MSIX/AppX package was found. Build a package first or pass -PackagePath explicitly."
}

$repoRoot = Get-RepoRoot
$appCertPath = Get-AppCertPath
$resolvedPackagePath = Resolve-PackagePath -RepoRoot $repoRoot -RequestedPath $PackagePath

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $reportDir = Join-Path $repoRoot "artifacts\store-validation"
    New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
    $ReportPath = Join-Path $reportDir "appcert-report.xml"
}

$resolvedReportPath = [System.IO.Path]::GetFullPath($ReportPath)

Write-Host "Testing package: $resolvedPackagePath"
Write-Host "Report output: $resolvedReportPath"
Write-Warning "Run this script from an elevated PowerShell session if the certification kit requires administrator context."

& $appCertPath reset
& $appCertPath test -appxpackagepath $resolvedPackagePath -reportoutputpath $resolvedReportPath

Write-Host "Certification report generated: $resolvedReportPath"
