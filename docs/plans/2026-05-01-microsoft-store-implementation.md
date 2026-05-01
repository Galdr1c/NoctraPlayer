# Microsoft Store Packaging Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a Store-ready MSIX packaging pipeline for Noctra without changing the LibVLC playback engine.

**Architecture:** Add a dedicated Windows packaging project that references the Avalonia desktop app, move Store-specific identity and visual metadata into package files, and make the app packaging-aware only where runtime compatibility requires it. Keep package creation reproducible locally and in CI.

**Tech Stack:** .NET 8, Avalonia 11.2.1, Windows Application Packaging Project, MSIX, GitHub Actions

---

### Task 1: Add planning and packaging directories

**Files:**
- Create: `docs/plans/2026-05-01-microsoft-store-design.md`
- Create: `docs/plans/2026-05-01-microsoft-store-implementation.md`
- Create: `build/`
- Create: `Noctra.Packaging/`

**Step 1: Confirm the approved Store packaging design is saved**

Expected: `docs/plans` contains the design and implementation plan documents.

**Step 2: Create the packaging workspace**

Expected: repository has dedicated locations for package project files, assets, and scripts.

### Task 2: Add package project and manifest

**Files:**
- Create: `Noctra.Packaging/Noctra.Packaging.wapproj`
- Create: `Noctra.Packaging/Package.appxmanifest`
- Create: `Noctra.Packaging/StoreAssociation.props`
- Create: `Noctra.Packaging/Assets/...`

**Step 1: Create packaging project with desktop app reference**

Expected: the package project can resolve `Noctra.Avalonia` as the package entry point.

**Step 2: Add manifest placeholders for identity and visuals**

Expected: the package can build before real Partner Center association, while making required fields explicit.

**Step 3: Add package assets and content mapping**

Expected: logos and splash assets exist for manifest validation.

### Task 3: Make the app package-aware

**Files:**
- Modify: `Noctra.Avalonia/Noctra.Avalonia.csproj`
- Modify: `Noctra.Avalonia/Program.cs`
- Modify: `Noctra.Avalonia/App.axaml.cs`
- Modify: `Noctra.Avalonia/Services/AvaloniaDialogService.cs`
- Modify: `Noctra.Core/Services/StartupDiagnostics.cs`
- Create: `Noctra.Core/Services/PackageIdentityService.cs`
- Create: `Noctra.Core/Services/Interfaces/IPackageIdentityService.cs`

**Step 1: Add version and packaging metadata to the desktop app**

Expected: assembly/package version values are explicit and package-friendly.

**Step 2: Detect packaged execution safely**

Expected: runtime can determine whether it has package identity without crashing on unpackaged runs.

**Step 3: Improve diagnostics and fallback behavior**

Expected: toast, storage, and startup diagnostics behave safely in both packaged and unpackaged runs.

### Task 4: Add local packaging automation

**Files:**
- Create: `build/package-store.ps1`
- Create: `build/test-store-package.ps1`
- Create: `Directory.Build.props`
- Create: `Noctra.sln`

**Step 1: Add shared build metadata**

Expected: versioning and configuration can be overridden centrally.

**Step 2: Add local packaging script**

Expected: one command restores, builds, tests, and packages the app.

**Step 3: Add solution file**

Expected: local and CI builds have a stable entry point that includes packaging.

### Task 5: Add CI package workflow

**Files:**
- Create: `.github/workflows/store-package.yml`

**Step 1: Build and test on Windows**

Expected: CI validates restore, build, and test before packaging.

**Step 2: Produce package artifacts**

Expected: workflow uploads the package output and logs as downloadable artifacts.

### Task 6: Add submission and certification docs

**Files:**
- Create: `docs/microsoft-store-submission.md`
- Modify: `README.md`

**Step 1: Document Partner Center identity requirements**

Expected: publisher and package identity mismatch risk is explicit.

**Step 2: Document local package validation**

Expected: repo contains commands for package build, install, and optional certification checks.

**Step 3: Document known LibVLC risk**

Expected: maintainers know that Store infrastructure can be complete before final certification is proven.
