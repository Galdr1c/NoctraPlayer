# Microsoft Store Packaging Design

**Date:** 2026-05-01

**Scope:** Prepare Noctra for real Microsoft Store submission as a packaged Win32 desktop app without replacing the current LibVLC playback engine.

## Goal

Ship Noctra as an `MSIX`-packaged desktop application that can be submitted to Microsoft Store through Partner Center, built locally and in CI with repeatable packaging outputs.

## Chosen Approach

Use a dedicated Windows Application Packaging Project (`.wapproj`) that references the existing Avalonia desktop application. Keep the current `LibVLCSharp` playback stack in place and make the application packaging-aware where needed for storage, diagnostics, notifications, and native dependency distribution.

## Why This Approach

- It matches Microsoft's supported path for Store-hosted Win32 apps distributed as `MSIX`.
- It minimizes product risk by preserving the current playback engine and application architecture.
- It isolates Store-specific metadata, assets, and build logic from the main application project.
- It allows early validation of the real blocker: whether the packaged `LibVLC` runtime behaves acceptably in local package tests and Store certification.

## Architecture

### Packaging Layer

- Add `Noctra.Packaging/Noctra.Packaging.wapproj`.
- Add `Package.appxmanifest` with Store-ready placeholders for identity, visual assets, target device family, and full-trust desktop entry point.
- Add packaging assets required by the manifest.

### Application Metadata

- Add assembly/package version metadata so the app and package can be versioned consistently.
- Ensure required content and native runtime files flow into package output.

### Packaged Runtime Compatibility

- Keep current `LocalApplicationData` storage model, but make packaged execution detectable for diagnostics and fallback behavior.
- Harden notification and Win32 interop surfaces so package-related failures degrade safely instead of breaking startup or core flows.
- Improve startup diagnostics so packaged test failures are easier to localize.

### Automation

- Add local scripts to restore, build, test, and package.
- Add GitHub Actions workflow to produce package artifacts on Windows runners.
- Keep publisher identity configurable so the repo can build before Partner Center association.

## Risks

### Primary Risk: LibVLC in MSIX

`LibVLCSharp` plus `VideoLAN.LibVLC.Windows` is the main technical uncertainty. The repository can be made Store-ready from an infrastructure perspective, but final Store acceptance still depends on package validation, runtime behavior, and certification outcomes.

### Secondary Risks

- `user32.dll` P/Invoke in custom video view could behave differently under package constraints.
- Native Windows toast behavior may differ between unpackaged and packaged runs.
- Partner Center publisher identity must eventually match the reserved Store identity exactly.

## Out of Scope

- Replacing the playback engine.
- Completing Partner Center listing text, screenshots, pricing, or age ratings inside the repository.
- Guaranteeing Microsoft Store approval before a real submission is tested against Partner Center certification.

## Deliverables

- Packaging project and manifest
- Packaging visual assets
- Build and packaging scripts
- CI package workflow
- Store submission and certification documentation
