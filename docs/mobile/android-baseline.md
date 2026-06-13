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

- Full tests: 913 passed, 0 failed, 0 skipped
- Windows Release rebuild: 0 errors, 20 existing warnings
- Android Debug APK build: 0 errors, 0 warnings
- Vulnerable NuGet packages in the Android graph: none
- Signed APK:
  `Noctra.Android/bin/Debug/net10.0-android36.0/studio.kynora.noctra-Signed.apk`

Android 16 native page-size compatibility is provided by:

- SkiaSharp 3.119.4 through Avalonia 12.0.4
- SQLitePCLRaw 2.1.11

The previous `XA0141` warnings are no longer present.
