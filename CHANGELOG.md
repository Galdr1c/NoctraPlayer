# Changelog

All notable changes to Noctra Media Player are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Detailed historical engineering notes are archived in [`docs/history/legacy-changelog.md`](./docs/history/legacy-changelog.md).

## [Unreleased]

### Added

- **Search image enrichment**: Search result cards now trigger the same visible-item metadata/image enrichment flow used by category pages.
- **Viewport-aware paging**: Scroll paging now loads more content when the viewport is not filled yet, and starts loading earlier near the end of the page.

### Changed

- **RemoteImage simplified**: The image control now has one job: normalize URL, use memory cache, download on demand, or show placeholder.
- **Poster-first VOD cards**: Movie cards prefer `LogoUrl`/provider poster before falling back to backdrop images.
- **On-demand image loading**: MainWindow image warmup and episode/card preload hooks were removed. Cards load their own images when they appear.
- **Post-load background work reduced**: The old startup warmup path no longer reloads home/favorites just to prepare images.
- **Provider poster ownership**: TMDB no longer replaces an existing provider poster just because it may be higher quality.
- **Provider-scoped Series artwork**: Stalker Series list parsing now reads the broader poster field set returned by portal category responses, while visible Series TMDB enrichment remains limited to M3U profiles.
- **Series enrichment deduplication**: Visible Series cards now avoid duplicate in-flight TMDB metadata and no-poster lookup work during repeated scroll/filter refreshes.
- **Git ignore hygiene**: Local Vercel, Upstash, TMDB proxy, environment, database, trace, dump, playlist, and packaging outputs are ignored by default.
- **Refresh UI state rebuild**: Channel-list refresh now reloads playlist, group, channel, and Series UI caches from the database for M3U, Xtream, and Stalker profiles.
- **Localized refresh and provider validation status**: Add-profile provider validation errors and settings refresh progress labels now use translation keys across supported languages.
- **Refresh progress flow**: Channel refresh progress now comes from the active provider load instead of a separate settings-only percentage; M3U uses clear stages, while Xtream and Stalker use category progress.
- **EPG save progress curve**: EPG saving progress now advances with a smoother bounded curve during large XMLTV imports.
- **Live TV responsive grid**: Live channel cards now stretch into adaptive columns so half-width windows can fit three columns and wide screens use the available space more evenly.
- **Personal-list media sections**: Favorites, My List, History, and Search now keep Live TV cards in responsive live grids instead of mixing them into poster-card rows.
- **Search ranking strategy**: Search now uses one local scoring model for Live TV, Movies, and Series, weighting title matches first and local metadata fields second.
- **Similar search results**: Similar results now come from the same scoring model with controlled thresholds, deduplication from primary results, and no TMDB/API calls.
- **Refresh cancellation**: Settings refresh overlay now includes a Cancel action for long-running channel-list refreshes.
- **Application executable name**: The Avalonia app now builds and packages as `Noctra.exe` while keeping the technical project folder/namespace separate.
- **Localized provider diagnostics**: Playlist import preview summaries and Stalker progressive-load status text now use translation keys across supported languages.
- **Stalker service interface cleanup**: Stalker service interface comments were rewritten as clean technical documentation and user-facing progress text was moved out of the DTO.
- **README rewritten**: The project README was rebuilt into a shorter product, architecture, setup, and packaging guide.
- **CHANGELOG rewritten**: The old long-form maintenance log was replaced with a compact Keep a Changelog structure.

### Removed

- **Header search popup**: The old header result popup and its async overlay search state were removed; header search now commits directly to the Search page.
- **Poster preload pipeline**: `RemoteImage.PreloadAsync`, image warmup scheduling, and active-control cache notify code were removed.
- **RemoteImage host escalation complexity**: Per-host failure escalation and known-bad-host state were removed from the control.
- **Provider image probing for TMDB replacement**: Existing provider poster URLs are no longer probed and swapped with TMDB posters.

### Fixed

- **Stalker progressive resume stalls**: Stalker category loading now times out stuck categories, clears their dummy card, and continues with the remaining queue instead of leaving progress frozen.
- **Progressive load UI stalls**: Cached Xtream/Stalker resume now runs off the UI thread, and Series/Home refresh work is deferred until channel loading completes.
- **Progress status overwrite**: Background aggregation no longer replaces active provider progress with "Channel List Ready" while the playlist is still loading.
- **Stalker resume matching**: Pending dummy categories now expose both group names and provider category IDs, so resume works even when localized or special-character group names shift.
- **Dummy Series aggregate leak**: Progressive `stalker-dummy://` and `xtream-dummy://` rows no longer create stale Series cards such as "Content loading".
- **Duplicated progress status text**: Channel-loading status now renders one primary progress message instead of repeating the same text twice in the status bar.
- **View-switch content bleed**: Live, Movies, and Series navigation now clears the target content surface before the new view renders, preventing old cards from flashing under the new page title.
- **Profile switch background loading leak**: Large Xtream/Stalker progressive loads are now cancelled and prevented from updating progress/status after profile switch or app close.
- **Connection health accuracy**: Connection badges no longer default to “good”; Add Profile and save validation now use the same provider auth/content rules, and M3U health checks are profile-scoped with GET fallback.
- **Category image stall**: Categories that reached the end of visible scroll without creating enough scrollable height can now request additional pages.
- **Search cards without posters**: Search and similar-result sections now queue visible VOD/series image fallback when provider posters are missing.
- **Stalker Series posters only appearing after detail open**: Stalker category-list parsing now captures additional provider poster fields, so cards can populate from the original list response instead of waiting for detail-page metadata.
- **120-image ceiling behavior**: Image loading is no longer tied to a fixed warmup count; visible cards decide their own image load.

### Verification

- `dotnet build NoctraPlayer.sln` passes.
- `dotnet test Noctra.Tests\Noctra.Tests.csproj --no-build` passes: 906 tests.

## [1.0.0] - 2026-06-01

### Added

- **Avalonia desktop app** for Windows with Live TV, Movies, Series, Search, My List, Favorites, History, and Downloads views.
- **Provider support** for M3U, Xtream Codes, and Stalker Portal.
- **LibVLC playback** for live streams, VOD, and series episodes.
- **Profile system** with avatar, provider account, PIN support, child profile rules, favorites, history, and isolated settings.
- **Series aggregation** with season/episode grouping and provider-independent progress tracking.
- **Download center** for VOD and series episodes with queue, pause/resume, encrypted local files, offline playback, and integrity repair flows.
- **EPG system** with playlist EPG, custom XMLTV sources, time offset, matching, refresh, and cleanup.
- **TMDB metadata enrichment** for posters, backdrops, overview, cast, ratings, trailers, and episode details.
- **Localization** for English, Turkish, German, Spanish, and French.
- **Free/Premium edition infrastructure** with Store packaging metadata, feature limits, and remote promo-code support.
- **Provider test CLI** for M3U, Xtream, Stalker, deep stream tests, and playlist audit reports.

### Changed

- **Default app language** moved to `en-US` while keeping multilingual UI support.
- **Provider-first metadata policy**: provider data is trusted when valid; TMDB fills missing or unusable visual/text metadata.
- **M3U grouping rules** were tightened to avoid noisy category creation from arbitrary prefixes.
- **Refresh safety**: provider refresh validates the new source before replacing existing playlist data.
- **Series progress model** now survives provider changes by using normalized series identity instead of only episode IDs.
- **Overlay and PiP logic** were stabilized for focus, resize, fullscreen, and Windows airspace behavior.
- **Download persistence** was optimized to reduce DB/event spam during long downloads.

### Removed

- **Remote image disk cache**: card images are no longer written to `%LOCALAPPDATA%\Noctra\ImageCache`.
- **Unused Avalonia image cache service** and related cleanup-directory entries.
- **Unused LibVLCSharp.Avalonia package reference** after moving to the custom video surface.
- **Legacy PiP window files** after consolidating PiP into the main window flow.

### Fixed

- **M3U series screen empty state** when aggregation was missing or delayed.
- **Provider refresh data loss risks** for failing or empty new sources.
- **Series progress loss** after provider credential or playlist changes.
- **Download resume/finalize edge cases**, including partial `.nctra` repair and stale temp files.
- **Offline/downloaded playback actions** so unavailable online-only actions are hidden.
- **Network status display** so Ethernet, Wi-Fi, and offline states are detected more accurately.
- **Overlay focus and visibility issues** across video close, layout hide, fullscreen, and Alt-Tab flows.
- **HiDPI overlay positioning** for multi-monitor setups.
- **Profile add/edit connection analysis** for M3U, Xtream, and Stalker flows.
- **User-facing error messages** across provider, playback, settings, download, and dialog flows.

### Verification

- Parser, metadata, overlay, download, history, profile, and provider scenarios are covered by the `Noctra.Tests` project.
- Store packaging helper scripts exist for Free and Premium MSIX builds.

[unreleased]: ./CHANGELOG.md
[1.0.0]: ./CHANGELOG.md
