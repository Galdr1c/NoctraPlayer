# Noctra Android Mobile Implementation Plan

## Objective

Deliver the approved Android phone and tablet MVP while preserving the current
Windows application. Android uses Avalonia for the application UI, Media3 as
the primary playback engine, Android LibVLC as a one-shot fallback, Android
system PiP, Android Keystore, and Google Play Billing.

Design reference:
`docs/superpowers/specs/2026-06-13-noctra-android-mobile-design.md`

## Baseline Decisions

- Build SDK: .NET 10
- Windows target during migration: keep `net8.0-windows10.0.17763.0`
- Android target: `net10.0-android`
- Android target/compile API: 36
- Android minimum API: 31
- Debug architectures: `arm64-v8a` and `x86_64`
- Release App Bundle architecture: `arm64-v8a`
- UI: Avalonia Android
- Playback: Media3 primary, Android LibVLC fallback
- Store model: one package, one-time Premium in-app product

Package versions must be selected from current stable releases during Task 1
and pinned centrally. Do not mix Avalonia package versions across projects.

## Phase 1: Toolchain and Windows Baseline

### Task 1: Record and verify the current baseline

Files:

- Add `docs/mobile/android-baseline.md`
- Inspect `global.json`
- Inspect `Directory.Build.props`
- Inspect all project files

Actions:

1. Record installed .NET SDKs, Android workloads, JDK, Android SDK platforms,
   build tools, and available emulators/devices.
2. Record the current Windows Release build and test results.
3. Record package versions and warnings before any upgrade.
4. Do not change source behavior in this task.

Verification:

```powershell
dotnet --list-sdks
dotnet workload list
dotnet test .\Noctra.Tests\Noctra.Tests.csproj --no-restore
dotnet build .\Noctra.Avalonia\Noctra.Avalonia.csproj -c Release --no-restore
```

Exit criterion: baseline results are documented and reproducible.

### Task 2: Move the repository build SDK to .NET 10

Files:

- Modify `global.json`
- Modify `Directory.Build.props`
- Modify `Noctra.Avalonia/Noctra.Avalonia.csproj`
- Modify `Noctra.Core/Noctra.Core.csproj`
- Modify `Noctra.Tests/Noctra.Tests.csproj` only if required
- Modify `Tester/NoctraProviderTester.csproj` only if required

Actions:

1. Pin an installed stable .NET 10 SDK in `global.json`.
2. Add central properties for Avalonia and Android package versions.
3. Upgrade all Avalonia packages to one stable version supporting Android.
4. Keep the Windows target framework unchanged for this phase.
5. Resolve upgrade breaks without changing user-facing behavior.

Tests first:

- Add or update package/version consistency checks in
  `Noctra.Tests/ReleaseSourceCleanlinessTests.cs`.

Verification:

```powershell
dotnet restore .\NoctraPlayer.sln
dotnet test .\Noctra.Tests\Noctra.Tests.csproj
dotnet build .\Noctra.Avalonia\Noctra.Avalonia.csproj -c Release
```

Exit criterion: the Windows app and full test suite pass under the .NET 10 SDK.

## Phase 2: Platform Boundaries

### Task 3: Replace static application paths with an injected path service

Files:

- Add `Noctra.Core/Services/Interfaces/IAppPathService.cs`
- Add `Noctra.Core/Services/DesktopAppPathService.cs`
- Modify `Noctra.Core/Services/AppPaths.cs`
- Modify `Noctra.Core/Services/SettingsService.cs`
- Modify `Noctra.Core/Services/CacheService.cs`
- Modify `Noctra.Core/Services/ContentDownloadService.cs`
- Modify dependent view models
- Modify `Noctra.Avalonia/App.axaml.cs`
- Add `Noctra.Tests/AppPathServiceTests.cs`

Actions:

1. Define paths for database, settings, cache, logs, and downloads.
2. Preserve the existing Windows paths exactly in `DesktopAppPathService`.
3. Inject paths into services instead of reading static OS folders.
4. Keep a narrow compatibility wrapper only where migration requires it.

Verification:

```powershell
dotnet test .\Noctra.Tests\Noctra.Tests.csproj --filter "FullyQualifiedName~AppPathService|FullyQualifiedName~SettingsService|FullyQualifiedName~CacheService"
dotnet test .\Noctra.Tests\Noctra.Tests.csproj
```

### Task 4: Make security storage platform-specific

Files:

- Keep `Noctra.Core/Services/Interfaces/ISecurityService.cs`
- Rename or replace `Noctra.Core/Services/SecurityService.cs` with a clearly
  desktop-owned implementation
- Modify desktop DI in `Noctra.Avalonia/App.axaml.cs`
- Add or update `Noctra.Tests/SecurityServiceTests.cs`

Actions:

1. Remove the non-Windows plaintext fallback.
2. Keep Windows DPAPI behavior in `DesktopSecurityService`.
3. Ensure callers depend only on `ISecurityService`.
4. Add tests proving encryption failure never silently stores plaintext.

### Task 5: Remove LibVLC types from the shared player contract

Files:

- Modify `Noctra.Core/Services/Interfaces/IVideoPlayerService.cs`
- Add `Noctra.Core/Models/PlaybackRequest.cs`
- Add `Noctra.Core/Models/PlaybackState.cs`
- Add `Noctra.Core/Models/PlaybackFailure.cs`
- Modify `Noctra.Core/Services/VideoPlayerService.cs`
- Modify `Noctra.Core/ViewModels/PlayerViewModel.cs`
- Modify `Noctra.Core/ViewModels/Player/*`
- Modify `Noctra.Avalonia/MainWindow.axaml.cs`
- Modify `Noctra.Avalonia/Controls/MemoryVideoView.cs`
- Extend player tests in `Noctra.Tests`

Actions:

1. Replace exposed `LibVLCSharp.MediaPlayer` and `VLCState` types with
   engine-neutral state, events, track models, and commands.
2. Move native-player handle access to a desktop-only adapter used by
   `MemoryVideoView`.
3. Represent URL, headers, user agent, media type, and resume position in
   `PlaybackRequest`.
4. Preserve all current Windows playback behavior.

Verification:

```powershell
rg -n "LibVLCSharp" Noctra.Core\Services\Interfaces Noctra.Core\Models
dotnet test .\Noctra.Tests\Noctra.Tests.csproj --filter "FullyQualifiedName~Player"
dotnet test .\Noctra.Tests\Noctra.Tests.csproj
dotnet build .\Noctra.Avalonia\Noctra.Avalonia.csproj -c Release
```

Exit criterion: no LibVLC type appears in a cross-platform public contract.

### Task 6: Extract shared service registration

Files:

- Add `Noctra.Core/DependencyInjection/ServiceCollectionExtensions.cs`
- Modify `Noctra.Avalonia/App.axaml.cs`
- Add `Noctra.Tests/DependencyInjectionTests.cs`

Actions:

1. Register provider, database, localization, profile, EPG, and media services
   in a shared extension.
2. Keep dialogs, windows, player, security, paths, updates, package identity,
   reviews, and billing in platform projects.
3. Add a test that resolves the shared graph with fake platform services.

## Phase 3: Android Shell

### Task 7: Create the Android and shared mobile UI projects

Files:

- Add `Noctra.Android/Noctra.Android.csproj`
- Add Android manifest/resources and application/activity entry points
- Add `Noctra.Mobile/Noctra.Mobile.csproj`
- Add `Noctra.Mobile/App.axaml`
- Add `Noctra.Mobile/App.axaml.cs`
- Modify `NoctraPlayer.sln`
- Modify `.gitignore`

Actions:

1. Create an Avalonia Android app targeting `net10.0-android`.
2. Set target API 36 and minimum API 31.
3. Reference `Noctra.Core` and `Noctra.Mobile`.
4. Put reusable mobile Avalonia views and resources in `Noctra.Mobile`.
5. Keep Android native integrations in `Noctra.Android`.
6. Add debug APK and emulator build profiles.

Verification:

```powershell
dotnet restore .\NoctraPlayer.sln
dotnet build .\Noctra.Android\Noctra.Android.csproj -c Debug
dotnet build .\Noctra.Avalonia\Noctra.Avalonia.csproj -c Release
dotnet test .\Noctra.Tests\Noctra.Tests.csproj
```

### Task 8: Implement the mobile shell and responsive navigation

Files:

- Add `Noctra.Mobile/Views/MobileShellView.axaml`
- Add `Noctra.Mobile/ViewModels/MobileShellViewModel.cs`
- Add mobile theme/token resources
- Add initial Home, Live, Movies, Series, and More page shells
- Add `Noctra.Tests/MobileShellViewModelTests.cs`

Actions:

1. Implement five bottom destinations for compact width.
2. Switch to a navigation rail at the selected tablet breakpoint.
3. Preserve selected destination across activity recreation.
4. Implement safe-area, system-bar, touch-target, and back-navigation behavior.
5. Keep player navigation separate from normal destination history.

### Task 9: Add Android platform services

Files:

- Add `Noctra.Android/Services/AndroidAppPathService.cs`
- Add `Noctra.Android/Services/AndroidSecurityService.cs`
- Add `Noctra.Android/Services/AndroidDispatcherService.cs`
- Add `Noctra.Android/Services/AndroidNetworkService.cs`
- Add `Noctra.Android/Services/AndroidFilePickerService.cs`
- Add `Noctra.Android/DependencyInjection/AndroidServiceCollectionExtensions.cs`
- Add platform service tests where host-testable

Actions:

1. Use app-private storage for database, settings, cache, and logs.
2. Use Android Keystore for secrets.
3. Use the Storage Access Framework for M3U document selection.
4. Observe Android connectivity without treating network presence as proof that
   a provider is reachable.
5. Redact provider secrets from Android logs.

## Phase 4: Profiles and Catalog

### Task 10: Implement mobile profile and provider setup

Files:

- Add mobile profile list, add/edit, validation, and loading views
- Reuse or adapt `ProfilesViewModel`, `AddProfileViewModel`, and
  `ProfileLoadingViewModel`
- Add mobile dialog/navigation implementation
- Extend provider/profile tests

Actions:

1. Support Xtream Codes, M3U URL/file, and Stalker Portal.
2. Validate before saving.
3. Show classified errors for authentication, timeout, invalid address,
   network unavailable, invalid payload, and empty catalog.
4. Enforce existing Free/Premium profile limits.
5. Verify failed refresh preserves the last usable catalog.

### Task 11: Implement Home, Live, Movies, Series, Search, and Favorites

Files:

- Add corresponding mobile views and focused view models/adapters
- Add reusable mobile media cards and responsive grids
- Reuse shared localization and image loading
- Add view-model and filtering tests

Actions:

1. Query local SQLite data first.
2. Run provider refresh and metadata enrichment asynchronously.
3. Use paging/virtualization for large provider catalogs.
4. Avoid loading full poster collections into memory.
5. Add loading, empty, stale-data, offline, and error states.

### Task 12: Add basic EPG

Files:

- Add mobile live-channel EPG summary and program list
- Adapt existing EPG services/view models
- Extend EPG tests for Android time-zone/lifecycle cases

Actions:

1. Show current and next programs in Live.
2. Support manual refresh in Free.
3. Gate automatic refresh and expanded source limits behind Premium.
4. Defer advanced source-management UI.

## Phase 5: Native Android Playback

### Task 13: Implement the engine-neutral Android playback coordinator

Files:

- Add `Noctra.Android/Playback/IAndroidPlaybackEngine.cs`
- Add `Noctra.Android/Playback/AndroidPlaybackCoordinator.cs`
- Add fake engines and coordinator tests
- Register it as `IVideoPlayerService`

Tests first:

1. Media3 success never creates LibVLC.
2. Recoverable Media3 startup failure creates LibVLC once.
3. Both engines failing produces one terminal error.
4. Cancellation prevents stale callbacks.
5. Engine selection remains fixed for the active session.

### Task 14: Implement Media3

Files:

- Add `Noctra.Android/Playback/Media3PlaybackEngine.cs`
- Add Android Media3 bindings/packages
- Add mapping tests for requests, headers, state, tracks, and failures

Actions:

1. Support HLS, progressive VOD, and provider headers.
2. Map native state and errors into shared playback models.
3. Implement audio/subtitle track selection and VOD seek.
4. Implement bounded reconnect behavior for transient network changes.

### Task 15: Implement Android LibVLC fallback

Files:

- Add `Noctra.Android/Playback/LibVlcPlaybackEngine.cs`
- Add Android LibVLC packages/native assets
- Add mapping and disposal tests

Actions:

1. Pass the same `PlaybackRequest` semantics used by Media3.
2. Ensure Media3 is fully released before fallback starts.
3. Prevent fallback loops.
4. Validate APK size and ABI packaging.

### Task 16: Implement the native player host and overlay

Files:

- Add `Noctra.Android/Playback/AndroidPlayerView.cs`
- Add `Noctra.Mobile/Controls/PlayerHost.cs`
- Add native controls, watermark, and lifecycle bridge
- Add orientation and state tests

Actions:

1. Host video and controls in one native Android container.
2. Do not place Avalonia controls over the native video surface.
3. Enter landscape for full-screen playback and restore browsing orientation.
4. Implement play/pause, seek, tracks, next/previous, exit, PiP, Premium sleep
   timer, and Free watermark.
5. Verify touch input after controls auto-hide and reappear.

### Task 17: Add Android system PiP

Files:

- Add `Noctra.Android/Playback/AndroidPictureInPictureService.cs`
- Modify the main Android activity lifecycle
- Add lifecycle state tests

Actions:

1. Use `PictureInPictureParams`.
2. Keep one playback session while entering/leaving PiP.
3. Pause background playback when PiP is not active.
4. Expose supported media actions through system PiP controls.

## Phase 6: Licensing and Release

### Task 18: Introduce a platform-neutral entitlement contract

Files:

- Add or revise entitlement interfaces in `Noctra.Core`
- Adapt current Windows `LicenseService`
- Add `Noctra.Android/Billing/DevelopmentBillingService.cs`
- Extend license tests

Actions:

1. Separate product rules from store purchase transport.
2. Keep existing Free/Premium limits unchanged.
3. Allow a debug-only development entitlement provider for sideloaded APKs.
4. Ensure Release builds cannot expose manual Premium activation.

### Task 19: Add Google Play Billing

Files:

- Add `Noctra.Android/Billing/GooglePlayBillingService.cs`
- Add purchase UI in mobile settings/upsell
- Add billing result and restore tests

Actions:

1. Configure one non-consumable Premium product.
2. Query owned purchases at startup and after billing reconnect.
3. Handle success, pending, cancellation, unavailable store, and restore.
4. Acknowledge completed purchases.
5. Treat Play ownership as authoritative; cached entitlement is temporary.
6. Query and acknowledge ownership through Google Play Billing for the MVP.
7. Before public production release, document whether server-side verification
   is being added or explicitly accept the increased purchase-fraud risk.

### Task 20: APK, App Bundle, policy, and physical-device validation

Files:

- Add Android build/release scripts under `build/`
- Add `docs/mobile/android-release-checklist.md`
- Add Android manifest, signing configuration templates, and Play Store listing
  asset checklist
- Update `README.md`

Actions:

1. Produce signed internal-test APK and Play-ready AAB.
2. Validate API 31, a mid-range supported API, and API 36.
3. Test ARM64 physical devices with M3U, Xtream, and Stalker providers.
4. Test HLS live, VOD seek, subtitles, audio tracks, fallback, rotation, PiP,
   process recreation, offline startup, and network switching.
5. Verify privacy disclosures, ads behavior, billing, content disclaimer, and
   data safety declarations.
6. Run Android lint/package inspection and check native ABI contents.

Final verification:

```powershell
dotnet test .\Noctra.Tests\Noctra.Tests.csproj
dotnet build .\Noctra.Avalonia\Noctra.Avalonia.csproj -c Release
dotnet build .\Noctra.Android\Noctra.Android.csproj -c Release
```

Release exit criteria:

- Windows build passes with no new unexplained warnings.
- Existing and new unit tests pass.
- Android Debug APK installs and runs on supported physical devices.
- Release AAB is accepted by Play Console internal testing.
- Media3-to-LibVLC fallback, PiP, Keystore, provider refresh preservation, and
  Premium restore have documented physical-device results.

## Risk Gates

Stop feature expansion and resolve the gate before proceeding when:

1. The Avalonia/.NET upgrade causes unresolved Windows regressions.
2. Native Android video cannot maintain correct touch/z-order behavior.
3. Media3 and LibVLC native dependencies produce incompatible ABI packaging.
4. Activity recreation creates duplicate player or refresh sessions.
5. Play Billing ownership cannot be restored reliably.
6. Large catalogs exceed agreed startup, memory, or scrolling thresholds.

## Deferred Work

- Android TV / Google TV
- iOS
- Chromecast
- Android Auto
- Offline downloads
- History UI and advanced history management
- Cloud sync and Windows profile transfer
- Advanced EPG source management
