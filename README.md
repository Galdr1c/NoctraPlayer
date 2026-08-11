<div align="center">
  <br />
  <img src="./Noctra.Avalonia/Assets/Square150x150Logo.png" alt="Noctra" Width="75"/>
  <h1>Noctra</h1>
  <p><strong>Modern IPTV player for M3U, Xtream Codes, and Stalker Portal providers.</strong></p>
  <p>
    <code>.NET 8 / .NET 10</code> | <code>Avalonia UI</code> | <code>LibVLC / ExoPlayer</code> | <code>SQLite</code> | <code>MSIX / AAB</code>
  </p>
  <p>
    <img src="https://img.shields.io/badge/version-1.2.0-7b5fff?style=flat-square" alt="Version 1.2.0" />
    <img src="https://img.shields.io/badge/platform-Windows%20%7C%20Android-0078d4?style=flat-square" alt="Windows & Android" />
    <img src="https://img.shields.io/badge/runtime-.NET%208%20%7C%2010-512bd4?style=flat-square" alt=".NET 8 / 10" />
    <img src="https://img.shields.io/badge/UI-Avalonia-ff3d8b?style=flat-square" alt="Avalonia" />
    <img src="https://img.shields.io/badge/license-Proprietary-5b4b8a?style=flat-square" alt="Proprietary License" />
  </p>
  <br />
</div>

---

## Overview

Noctra is a cross-platform IPTV media player built for real provider workflows: large playlists, mixed content types, series grouping, watch progress, EPG, downloads, and profile isolation.

Available on **Windows** (desktop) and **Android** (mobile).

Noctra does **not** provide content. It only displays IPTV playlists and provider accounts that the user is authorized to access.

### Highlights

- **Provider support**: M3U, Xtream Codes, and Stalker Portal.
- **Cross-platform**: Windows desktop and Android mobile with platform-native playback.
- **Content views**: Live TV, Movies/VOD, Series, Search, My List, Favorites, History, and Downloads.
- **Series-first handling**: season/episode grouping, provider-independent progress, continue watching, and local download playback.
- **Metadata enrichment**: provider poster first, TMDB fallback where appropriate, localized title/overview/cast/rating data.
- **EPG**: playlist EPG, custom XMLTV sources, time offset, matching, refresh, and cleanup.
- **Offline media**: encrypted local downloads for VOD and series episodes.
- **Profiles**: avatar, provider credentials, PIN, favorites, watch history, and settings per profile.
- **Mobile-first features**: Picture-in-Picture, gesture controls, background playback, sleep timer, and subtitle/audio track selection.
- **Store-ready editions**: Free/Premium build metadata and MSIX/AAB packaging flow.

---

## Tech Stack

| Layer | Windows | Android |
|-------|---------|---------|
| UI | Avalonia UI 12 | Avalonia UI 12 |
| Runtime | C# / .NET 8 | C# / .NET 10 |
| Playback | LibVLCSharp + VideoLAN.LibVLC.Windows | ExoPlayer (Media3) |
| MVVM | CommunityToolkit.Mvvm | CommunityToolkit.Mvvm |
| Data | SQLite + Entity Framework Core | SQLite + Entity Framework Core |
| Packaging | MSIX | AAB / APK |
| Tests | xUnit | xUnit |

---

## Architecture

```text
NoctraPlayer.sln
|-- Noctra.Avalonia/      Desktop app (Windows), XAML views, controls, UI services
|-- Noctra.Mobile/        Shared mobile UI, views, controls, converters, styles
|-- Noctra.Android/       Android platform-specific services and MainActivity
|-- Noctra.Core/          Models, ViewModels, provider services, DB, EPG, downloads
|-- Noctra.Tests/         Unit and scenario tests
|-- Tester/               Provider and stream validation CLI
|-- build/                Store packaging helper scripts
`-- docs/                 Planning and release notes
```

### Runtime Data

```text
Documents\Noctra\
|-- noctra_v1.db
|-- settings.json
|-- Settings\profile_{id}.json
`-- Downloads\
```

---

## Core Features

### Provider and Playlist Handling

| Provider | Supported |
|----------|-----------|
| M3U URL/file | Yes |
| Local M3U file import | Yes |
| Xtream Codes | Yes |
| Stalker Portal | Yes |
| Provider preview before save | Yes |
| Safe refresh without deleting old data on failure | Yes |

### Media Experience

- Live TV playback with EPG overlay.
- VOD and series playback with watch progress.
- Search across visible channels, movies, and series.
- Category filtering, sorting, favorites, and My List.
- Continue Watching and History rails.
- Picture-in-picture, fullscreen, audio/subtitle selection, sleep timer, and overlay controls.

### Desktop Features

- VLC-based playback with hardware acceleration.
- Native Windows overlay with airspace handling.
- Microsoft Store integration with review prompts.
- Premium upgrade flow with feature comparison.

### Mobile Features

- ExoPlayer-based playback with DASH, HLS, SmoothStreaming, and RTSP support.
- Gesture controls for volume and brightness.
- Picture-in-Picture mode for multitasking.
- Background playback with notification controls.
- Sleep timer for automatic shutoff.
- Subtitle and audio track selection.
- Stream quality detection.
- Double-tap to exit confirmation.

### Posters and Metadata

Noctra keeps the image flow intentionally simple:

1. Use provider poster/logo URL when available.
2. Use TMDB poster when provider data is missing or unusable and the provider type allows fallback.
3. Show the card placeholder when no valid image exists.

Images are loaded on demand by visible cards. There is no app-wide poster preload or disk image cache.

### Downloads

| Capability | Notes |
|------------|-------|
| VOD downloads | Supported |
| Series episode downloads | Supported |
| Queue / pause / resume / cancel | Supported |
| Encrypted local files | `.nctra` format |
| Offline playback | Downloads view and offline fallback |
| Integrity repair | Handles partial/finalization failures where possible |

Default layout:

```text
Documents\Noctra\Downloads\profile_{id}\Filmler\...
Documents\Noctra\Downloads\profile_{id}\Diziler\...\Sezon 01\...
```

---

## Authorized Development

This repository contains proprietary Kynora Studio source code. The commands
below are intended only for Kynora Studio personnel and contributors who have
received prior written authorization. Repository access does not grant
permission to copy, modify, distribute, publish, sublicense, or sell Noctra.

### Prerequisites

- Windows 10 1809 or newer (desktop) or Android 12+ (mobile)
- .NET 8 SDK (desktop) or .NET 10 SDK (mobile)
- Visual Studio 2022, Rider, or VS Code
- Visual Studio MSIX tooling for Store packaging
- Android SDK with build-tools for mobile builds

### Run the Desktop App

```powershell
dotnet restore .\NoctraPlayer.sln
dotnet build .\NoctraPlayer.sln
dotnet run --project .\Noctra.Avalonia\Noctra.Avalonia.csproj
```

### Run the Android App

```powershell
dotnet build .\Noctra.Android\Noctra.Android.csproj -c Debug
```

### Run Tests

```powershell
dotnet test .\Noctra.Tests\Noctra.Tests.csproj
```

Useful filters:

```powershell
dotnet test .\Noctra.Tests\Noctra.Tests.csproj --filter "FullyQualifiedName~M3UParser"
dotnet test .\Noctra.Tests\Noctra.Tests.csproj --filter "FullyQualifiedName~MetadataService"
dotnet test .\Noctra.Tests\Noctra.Tests.csproj --filter "FullyQualifiedName~Download"
```

---

## Configuration

Optional `.env` values can be placed in the repository root or output directory.

```env
TMDB_API_KEY=your_tmdb_v3_api_key
TMDB_BEARER_TOKEN=your_tmdb_v4_access_token
NOCTRA_PROMO_CODES_URL=https://example.com/noctra-promo-codes.json
DEV_PASSWORD=your_developer_password
```

| Variable | Purpose |
|----------|---------|
| `TMDB_API_KEY` | TMDB v3 metadata lookup key |
| `TMDB_BEARER_TOKEN` | TMDB v4 bearer token |
| `NOCTRA_PROMO_CODES_URL` | Remote promo-code JSON source |
| `DEV_PASSWORD` | Developer mode unlock value |

Promo-code details live in [`PROMO_CODES_README.md`](./PROMO_CODES_README.md).

---

## Store Packaging

Noctra can build Free and Premium editions from the same codebase.

### Windows (MSIX)

```powershell
.\build\package-store.ps1
```

Build one edition:

```powershell
.\build\package-store.ps1 -Editions Free
.\build\package-store.ps1 -Editions Premium
```

Set a version:

```powershell
.\build\package-store.ps1 -VersionPrefix 1.2.0
```

### Android (AAB/APK)

```powershell
dotnet build .\Noctra.Android\Noctra.Android.csproj -c Release
```

---

## Provider Test CLI

The `Tester` project validates provider connectivity and stream behavior.

```powershell
dotnet run --project .\Tester\NoctraProviderTester.csproj -- --type m3u --url "http://example.com/list.m3u" --deep-test
```

```powershell
dotnet run --project .\Tester\NoctraProviderTester.csproj -- --type xtream --host "http://host" --user "username" --pass "password" --deep-test --live 3 --vod 3 --duration 20
```

For playlist classification audits:

```powershell
dotnet run --project .\Tester\NoctraProviderTester.csproj -- --audit-playlist ".\playlist.m3u" --out audit.html
```

See [`Tester/DEEP_TEST_GUIDE.md`](./Tester/DEEP_TEST_GUIDE.md) for detailed stream metrics.

---

## Localization

Translations are embedded from:

```text
Noctra.Core\Localization\Translations\
```

Current languages:

| Locale | Language |
|--------|----------|
| `en-US` | English |
| `tr-TR` | Turkish |
| `de-DE` | German |
| `es-ES` | Spanish |
| `fr-FR` | French |

New UI text should use localization keys instead of hardcoded strings.

---

## Development Notes

- Keep provider data authoritative when it is valid.
- Use TMDB as enrichment, not as a replacement for every provider field.
- Avoid app-wide preloading; load data and images for the view the user is actually using.
- Protect user data during provider refresh. Do not delete old content until the new source is verified.
- Add focused tests when changing parser, progress, download, metadata, or provider refresh behavior.

---

## Changelog

See [`CHANGELOG.md`](./CHANGELOG.md) for the release history.

Current focus:

- Mobile platform stability and performance.
- On-demand poster loading.
- Cleaner search and scroll paging.
- Provider-safe metadata handling.
- Stable downloads and provider-independent series progress.

---

## License

Noctra is proprietary software owned by Kynora Studio. It is not open source.
No right to copy, modify, redistribute, sublicense, sell, or create derivative
works is granted by access to this repository.

Third-party dependencies remain subject to their respective licenses. See
[`LICENSE`](./LICENSE) for the complete Noctra license notice.
