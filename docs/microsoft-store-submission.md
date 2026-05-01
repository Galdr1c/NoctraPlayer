# Microsoft Store Submission Guide

## Goal

This repository packages Noctra as two Store-ready `MSIX` desktop applications using `Noctra.Packaging/Noctra.Packaging.wapproj`:

- `Noctra` (`Free` edition)
- `Noctra Premium` (`Premium` edition)

## What This Repo Now Provides

- A Windows Application Packaging Project
- `Package.appxmanifest` with Store-ready placeholders
- Edition profile config: `Noctra.Packaging/store-profiles.json`
- Local package build script: `build/package-store.ps1`
- Optional local certification script: `build/test-store-package.ps1`
- GitHub Actions workflow: `.github/workflows/store-package.yml`

## Required Local Tooling

To build the package locally, install one of these:

- Visual Studio 2022 with `MSIX Packaging Tools`
- Visual Studio 2022 Build Tools with Windows packaging/Desktop Bridge support

`dotnet` SDK alone is not enough to build `.wapproj` packaging projects.

## Partner Center Setup

Before real Store submission, reserve both app names in Partner Center and replace the placeholder values in:

- `Noctra.Packaging/StoreAssociation.props`
- `Noctra.Packaging/store-profiles.json`

The `Identity Name` and `Publisher` in the package must match the values assigned by Partner Center exactly. If they do not match, package association/submission will fail.

For the `Free` edition profile, set the premium Store target values so the upsell flow opens the real Premium product listing:

- `premiumStoreProductId`
- `premiumStoreLaunchUri`
- `premiumStoreWebUri`

## Local Build

Build the Store upload package:

```powershell
.\build\package-store.ps1
```

Build a single edition explicitly:

```powershell
.\build\package-store.ps1 -Editions Free
.\build\package-store.ps1 -Editions Premium
```

Build with an explicit version:

```powershell
.\build\package-store.ps1 -VersionPrefix 1.2.0
```

Package output defaults to:

```text
artifacts/store/
```

## Local Certification Check

If Windows App Certification Kit is installed:

```powershell
.\build\test-store-package.ps1
```

The script searches for the newest `msix/appx` package in:

- `artifacts/store`
- `Noctra.Packaging/AppPackages`

## Known Risk: LibVLC

The main unresolved Store-certification risk is the packaged runtime behavior of:

- `LibVLCSharp`
- `VideoLAN.LibVLC.Windows`

This repository now includes the infrastructure needed to package and submit the app, but final Store acceptance still depends on:

- packaged runtime validation
- native dependency inclusion in the produced package
- Partner Center certification results

## Runtime Notes

- The app now logs packaged vs unpackaged runtime context through `StartupDiagnostics`.
- Toast notifications use a safer absolute asset path and fall back to the in-app notification window if native toast display fails.
- The application still uses `LocalApplicationData`, which is compatible with packaged full-trust desktop applications and should be validated in installed package runs.
- The `Premium` edition starts with all premium features enabled automatically.
- The `Free` edition opens the Premium app listing in Microsoft Store from the upsell flow.
