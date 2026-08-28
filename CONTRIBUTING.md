# Contributing to Noctra

Noctra is proprietary software owned by Kynora Studio. This repository is not
an open-source contribution project.

## Table of Contents

- [Authorization](#authorization)
- [Development Requirements](#development-requirements)
- [Platform-Specific Development](#platform-specific-development)
  - [Windows Desktop](#windows-desktop)
  - [Android Mobile](#android-mobile)
- [Review Workflow](#review-workflow)
- [Bug and Security Reports](#bug-and-security-reports)

## Authorization

Code, documentation, translation, design, and testing contributions are
accepted only from Kynora Studio personnel or contributors who have received
prior written authorization.

Do not fork, copy, modify, publish, or redistribute the repository unless a
written agreement with Kynora Studio expressly permits it. Access to the
repository does not grant a software license.

Authorized contributors must ensure they have the right to submit their work
and that Kynora Studio may use it under the applicable contributor, employment,
or contractor agreement. Do not submit third-party code or assets without
compatible written permission and required notices.

## Development Requirements

- Follow the existing architecture and `.editorconfig`.
- Keep UI concerns out of `Noctra.Core`.
- Add focused tests for behavioral changes and regressions.
- Run `dotnet test .\Noctra.Tests\Noctra.Tests.csproj` before review.
- Do not commit credentials, provider accounts, playlist URLs, tokens, personal
  data, production secrets, or private signing material.
- Keep user data intact during provider refresh and migration work.
- Document user-visible changes in `CHANGELOG.md`.

## Platform-Specific Development

### Windows Desktop

#### Prerequisites

- Windows 10 1809 or newer
- .NET 8 SDK
- Visual Studio 2022, Rider, or VS Code
- Visual Studio MSIX tooling for Store packaging

#### Build and Run

```powershell
dotnet restore .\NoctraPlayer.sln
dotnet build .\NoctraPlayer.sln
dotnet run --project .\Noctra.Avalonia\Noctra.Avalonia.csproj
```

#### Architecture

- **Noctra.Avalonia**: Desktop app with XAML views, controls, and UI services
- **LibVLCSharp**: Video playback engine
- **Overlay System**: Custom video overlay with airspace handling for Windows

### Android Mobile

#### Prerequisites

- Windows 10/11, macOS, or Linux
- .NET 10 SDK
- Android SDK/platform API 31+ (Android 12+ is the minimum supported mobile OS)
- Android emulator or physical device for testing

#### Build and Run

```powershell
# Debug build
dotnet build .\Noctra.Android\Noctra.Android.csproj -c Debug

# Release build (AAB for Play Store)
dotnet build .\Noctra.Android\Noctra.Android.csproj -c Release
```

#### Architecture

- **Noctra.Mobile**: Shared mobile UI, views, controls, converters, and styles
- **Noctra.Android**: Platform-specific services and MainActivity
- **ExoPlayer (Media3)**: Video playback engine with DASH, HLS, SmoothStreaming, and RTSP support

#### Key Services

| Service | Purpose |
|---------|---------|
| `AndroidVideoPlayerService` | ExoPlayer integration and playback control |
| `AndroidVideoSurfaceService` | Video surface management and transforms |
| `AndroidPictureInPictureService` | PiP mode support |
| `AndroidFilePickerService` | Local M3U file import |
| `AndroidDialogService` | In-app notifications and dialogs |
| `AndroidNetworkService` | Network status detection |

#### Mobile-Specific Features

- **Gesture Controls**: Volume (left swipe) and brightness (right swipe)
- **Picture-in-Picture**: Background playback with floating window (premium)
- **Sleep Timer**: Automatic playback shutoff
- **Subtitle/Audio Track Selection**: Dynamic track switching
- **Stream Quality Detection**: Automatic resolution detection

#### Testing on Device

1. Enable Developer Options on your Android device
2. Enable USB Debugging
3. Connect device via USB
4. Build the APK:
   ```powershell
   dotnet build .\Noctra.Android\Noctra.Android.csproj -c Debug
   ```
5. Install on device:
   ```powershell
   adb install .\Noctra.Android\bin\Debug\net10.0-android36.0\com.companyname.noctra.apk
   ```

#### Troubleshooting

- **Emulator performance**: Use x86_64 system images with GPU acceleration enabled
- **SDK version mismatches**: Ensure Android SDK build-tools 34+ and platform-tools are installed
- **Build-tools not found**: Install via `sdkmanager "build-tools;34.0.0" "platforms;android-34"`
- **Hot reload issues**: Ensure `HotAvalonia` package is installed and configured in `.csproj`

## Review Workflow

Authorized contributors should work in a Kynora Studio-approved branch and
submit changes through the review process designated by the maintainer. A
submission may be rejected, revised, or incorporated at Kynora Studio's
discretion.

## Bug and Security Reports

General bug reports should include reproducible steps, expected and actual
behavior, the Noctra version, platform (Windows/Android), and sanitized logs
where relevant.

Security vulnerabilities must be reported privately according to
[`SECURITY.md`](./SECURITY.md).

Licensing and contribution inquiries: **kynora.studio@gmail.com**
