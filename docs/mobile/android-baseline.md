# Android Migration Baseline

Recorded on 2026-06-13 before the Android migration.

## Repository

- Build SDK selected by `global.json`: .NET 8
- Windows target: `net8.0-windows10.0.17763.0`
- Avalonia: 11.2.1
- Working tree before planning artifacts: clean

## Installed Toolchain

- .NET SDK 8.0.421
- .NET SDK 9.0.314
- Android workload: not installed
- Java/JDK: not available on `PATH`
- `JAVA_HOME`: not set
- `ANDROID_HOME`: not set
- `ANDROID_SDK_ROOT`: not set

Android implementation requires a stable .NET 10 SDK, the .NET Android
workload, JDK, and Android SDK platform/build tools.

## Windows Verification

Release build command:

```powershell
dotnet build .\Noctra.Avalonia\Noctra.Avalonia.csproj -c Release --no-restore
```

Result:

- Exit code: 0
- Errors: 0
- Warnings: 20

Test command:

```powershell
dotnet test .\Noctra.Tests\Noctra.Tests.csproj --no-restore
```

Result:

- Passed: 902
- Failed: 1
- Skipped: 0
- Total: 903

The failure was:

```text
PlayerSleepTimerTests.SleepTimer_EndOfEpisode_TriggersShutdownOnPlaybackEnded
```

The test passed when immediately rerun in isolation. It waits a fixed 2000 ms
for production logic delayed by 1500 ms, so the baseline records it as an
existing timing-sensitive test. The Android migration must not count this as a
new regression, but the full suite still needs a clean rerun before each phase
is declared complete.

## Phase 1 Result

The migration baseline was updated to:

- .NET SDK 10.0.301
- Avalonia 12.0.4
- .NET Android workload 36.1.2
- Microsoft OpenJDK 17.0.19
- Android Studio 2026.1.1 Patch 1

The Android SDK platforms and build tools will be installed with the first
Android project through the .NET Android `InstallAndroidDependencies` target,
using explicit SDK and JDK paths.

Verification after the SDK and Avalonia upgrade:

- Tests: 904 passed, 0 failed, 0 skipped
- Windows Release rebuild: 0 errors, 20 existing warnings

The sleep timer test was changed from a fixed two-second delay to bounded
condition-based waiting. This removes load-dependent timing without changing
production behavior.

## Android Shell Result

The first Android shell was added as separate projects:

- `Noctra.Mobile`: shared Avalonia mobile UI
- `Noctra.Android`: Android application host

The desktop project remains in `Noctra.Avalonia`.

Installed Android components:

- Android SDK platform 36
- Android build-tools 36.0.0
- Android platform-tools

Current verification:

- Full tests: 930 passed, 0 failed, 0 skipped
- Windows Release rebuild: 0 errors, 20 existing warnings
- Android Debug APK build: 0 errors, 0 warnings
- Vulnerable NuGet packages in the Android graph: none
- Signed APK:
  `Noctra.Android/bin/Debug/net10.0-android36.0/studio.kynora.noctra-Signed.apk`

Current platform separation:

- Shared service registrations live in
  `Noctra.Core/DependencyInjection/ServiceCollectionExtensions.cs`.
- Desktop and Android provide their own `IAppPathService` implementations.
- Android stores the database, settings, and logs in application-private
  storage, uses the application cache for temporary playback files, and uses
  the app-specific external downloads directory when available.
- Shared download flows resolve and validate paths through `IAppPathService`;
  they no longer use the desktop-only static `AppPaths` helper.
- Android startup builds its own dependency graph and reuses the shared
  `AddNoctraCoreServices` registrations.
- Provider credentials use an Android Keystore-backed AES-GCM key; invalid or
  undecryptable payloads are rejected instead of being treated as plaintext.
- Android connectivity changes are observed through `ConnectivityManager`
  without treating network presence as proof that a provider is reachable.
- M3U document import uses Android's Storage Access Framework. Selected
  documents are copied into the application-private `Imports` directory and
  shared parsing code receives a normal local file path instead of a
  platform-specific `content://` URI.
- The first mobile profile setup form reuses the desktop
  `AddProfileViewModel` for Xtream, M3U, Stalker, validation, connection
  analysis, profile limits, PIN rules, encryption, and save behavior.
- Local M3U files are preserved as file paths through selection, analysis,
  saving, initial catalog loading, refresh, and profile editing. Remote HTTP
  and HTTPS M3U sources retain the existing URL flow.
- Mobile profile labels use the same localization service and translation
  resources as the desktop application.
- The mobile More/profile area reuses the desktop `ProfilesViewModel`,
  `AddProfileViewModel`, and `AvatarPickerViewModel` contracts for listing,
  add/edit navigation, avatar selection, profile validation, and save behavior.
  Avatar PNG resources are linked from the desktop asset set into the mobile
  Avalonia package instead of maintaining a second copy.
- Mobile profile selection now mirrors the desktop PIN gate and loading flow:
  `PinEntryViewModel` handles PIN attempts, lockout, and forgot-PIN behavior,
  `ProfileLoadingViewModel` backs the loading overlay, and the selected profile
  is reloaded with its provider account before `MainViewModel.LoadProfileAsync`
  runs.
- Mobile Live, Movies, and Series surfaces now bind to the desktop
  `MainViewModel` content contracts. They reuse `FilteredChannels`,
  `SeriesViewItems`, group and sort selection, content loading/empty states,
  `NavigateCommand`, and the existing incremental load methods while presenting
  the data in phone-friendly vertical layouts.
- Mobile content cards now invoke the same `SelectMediaCommand` used by the
  desktop cards. The shared `MainViewModel.OnMediaSelected` event is subscribed
  from the mobile shell and surfaced as a temporary Android playback-readiness
  status until the native Android video surface is implemented.
- Android now registers an `IVideoPlayerService` implementation backed by
  `Android.Media.MediaPlayer` and wires the mobile shell to the shared
  `PlayerViewModel.PlayChannelAsync` path. The mobile player overlay exposes
  selected content, connection/error state, play-pause/stop/close, seek,
  volume/mute, remaining time, stream info, and quality summary bindings from
  the same desktop player contract. Secondary mobile controls now bind to the
  desktop skip backward/forward, live favorite, video fill mode, and sleep timer
  commands. The mobile sleep timer panel exposes the same Off, 15 minute,
  30 minute, 60 minute, end-of-content, and cancel actions through
  `SetSleepTimerCommand` and `CancelSleepTimerCommand`. Mobile player
  info/download controls now bind to the desktop `OpenInfoPanelCommand`,
  `DownloadCurrentContentCommand`, visibility/can-execute flags, download
  status, and current-content metadata. Android quality metadata remains limited
  until richer native track/metadata extraction is added.
- Android playback now creates a native `SurfaceView` through an
  `IVideoSurfaceService` bridge and binds it to `Android.Media.MediaPlayer` via
  `SetSurface`. The mobile shell shows the surface before starting
  `PlayerViewModel.PlayChannelAsync` and hides it when the player closes.

Android 16 native page-size compatibility is provided by:

- SkiaSharp 3.119.4 through Avalonia 12.0.4
- SQLitePCLRaw 2.1.11

The previous `XA0141` warnings are no longer present.
