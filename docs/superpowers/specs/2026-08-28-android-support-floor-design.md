# Android Support Floor Design

## Decision

Noctra's Android minimum supported version is Android 12 / API 31:
`Noctra.Android.csproj` declares `SupportedOSPlatformVersion=31` and all
developer-facing prerequisites use Android 12+ / API 31.

## Reason

The Huawei ANE-LX1 test device runs Android 9 / EMUI 9.1 with a Mali-T830 GPU.
During playback its audio and hardware H.264 decoder remain active, but the
video area becomes white. EGL/Mali native-window messages continue without a
Media3 playback exception. A controlled pre-API-30 SurfaceView Z-order rebuild
and transparent pixel-format fallback restored the Avalonia controls but did
not restore the video pixels. This identifies the failure as an incompatible
legacy vendor compositor path at the Avalonia SurfaceView/native TextureView
boundary.

## Scope

- Android 9–11 devices are excluded from new Play/AppGallery releases by the
  manifest minimum; existing installs remain untouched until users move to a
  supported device.
- Android 12+ keeps the existing EGL, SurfaceView/TextureView composition and
  playback implementation.
- The failed legacy surface workaround is removed so it cannot add surface
  churn, white frames, or control regressions to supported devices.
- The minimum SDK is raised; target SDK, decoder, billing, advertising, PiP,
  downloads, and desktop support are otherwise unchanged.

## Documentation and Build Consistency

The README, `tools/android/build-ffmpeg-16k.sh` API floor, Android project
comment, banner lifecycle design note, and production-hardening checklist all
state API 31. A release-guard test prevents those values from drifting apart.

## Verification

- The release-guard test fails when any of the project, README, or FFmpeg API
  floor values is not 31, then passes after the change.
- Surface-composition contract tests retain the original supported path and no
  longer require the rejected legacy workaround.
- Full Noctra and billing API test suites remain green.
- The next Play release must be built with a version code greater than the
  current Internal Testing release; Play will report Android 9–11 as
  unsupported, while Android 12+ remains eligible.
