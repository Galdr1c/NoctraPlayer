# Changelog

All notable changes to Noctra Media Player are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Detailed historical engineering notes are archived in [`docs/history/legacy-changelog.md`](./docs/history/legacy-changelog.md).

## [Unreleased]

### Added

- **Play Billing IAP on Android**: Premium can now be purchased directly from Google Play — a monthly auto-renewing subscription and a one-time lifetime package. The upsell sheet shows both plans with live store prices; entitlement is re-verified from Play on every launch and whenever purchases change. Lifetime always wins; otherwise the later of subscription/promo expiry applies. Product IDs (`noctra_premium_monthly`, `noctra_premium_lifetime`) must be created in Play Console.

### Changed

- **Promo card hidden on permanent Premium**: The promo code card is no longer shown on editions that are already permanently Premium (where redemption is meaningless). On timed promo Premium it stays visible — the system supports stacking duration — and the apply button relabels to "Add Duration" ("Süre Ekle") on desktop and mobile.
- **Promo config hardening and error taxonomy**: The promo code list is now validated on load — a 256 KB response cap (enforced both via `Content-Length` and a bounded stream read), `text/html` rejection, optional `schemaVersion` (only v1 accepted), and full rejection of configs with empty codes or duplicate normalized codes (e.g. `AB CD` vs `ABCD`) so JSON ordering no longer decides redemption. Load failures are now categorized (`Offline`, `Timeout`, `ServiceUnavailable`, `ConfigurationInvalid`) with localized user-facing messages instead of a blanket "check your internet" error; the developer-facing "URL must be configured by the admin" message was replaced. Deliberate scope note: the list is fetched with `no-cache` (immediate CDN-bypassing updates), which conflicts with ETag reuse, and has no cryptographic signature — both are deferred until a server-side redemption flow replaces client-side validation.

### Fixed

- **Promo failures no longer leak technical details**: Unexpected exceptions during promo redemption are logged and surfaced as a localized generic message in both mobile and desktop settings instead of `exception.Message`.
- **Corrupted promo grant is no longer silent**: If the stored Premium grant cannot be decrypted/deserialized, the app still fails closed to Free but the settings screen now shows a warning ("Premium entitlement could not be verified — contact support before deleting app data") on desktop and mobile.

### Removed

- **Dead promo translation keys**: `GlobalSettings.Promo.Error.ConfigMissing`, `ConfigLoadFailed`, and `ApplyFailedFormat` were removed from all five languages.
- **Child Mode removed**: The "Child Profile" option is gone from profile creation and editing on desktop and mobile. Provider metadata, category names, and ratings are not reliable enough to guarantee age-appropriate content, so Noctra no longer claims to filter content for children. Since the app has not reached a wide audience yet, existing child profiles are **deleted together with their data** — history, favorites, downloads, playlists, and the provider account when no other profile uses it — during the first launch after this update, and affected users see a one-time notice. The filtering code (`ChildSafetyHelper`, `ApplyChildFilter`, `PurgeNonCompliantSeriesAsync`, related translations, and tests) was fully removed.

## [1.1.0] - 2026-07-08

### Added

- **Premium category hiding gate**: Category eye actions remain visible, but Free users now see the Premium upgrade window instead of hiding Live, Movies, or Series groups.
- **Compact Premium comparison**: The Premium upgrade window now presents an updated Free/Premium feature comparison in a smaller theme-aware layout.
- **Legal and privacy consent gate**: First launch now requires users to accept legal/privacy acknowledgements that Noctra provides no IPTV content, playlists, EPG data, streams, or subscriptions and that users must add lawful sources.
- **Diagnostics and crash-report consent**: Optional diagnostic/crash-report sharing is now represented as explicit consent instead of generic product analytics.
- **Microsoft Store review prompt**: Main app sessions can now show a localized "rate Noctra" prompt after meaningful usage, with Rate, Later, and Don't ask again actions.
- **Search image enrichment**: Search result cards now trigger the same visible-item metadata/image enrichment flow used by category pages.
- **Viewport-aware paging**: Scroll paging now loads more content when the viewport is not filled yet, and starts loading earlier near the end of the page.

### Changed

- **Proprietary licensing and ownership**: The unintended MIT license was replaced with a Kynora Studio proprietary license; README, security, contribution, collaboration, and package ownership metadata now reflect the closed-source distribution model.
- **Premium feature list refreshed**: The upgrade window now highlights 12 profiles with PIN protection, 10 custom EPG sources with automatic refresh, resume playback, sleep timer, category hiding, advanced video buffering, and ad-free/watermark-free use.
- **Premium upgrade actions simplified**: The purchase button now contains only the localized Buy Premium label, with an explicit Continue with Free action directly below it.
- **Upsell theme integration**: Premium upgrade cards, borders, glow elements, typography, and actions now use shared light/dark theme resources.
- **Global ComboBox styling**: ComboBox selected content, dropdown arrows, text trimming, and filter widths were standardized across settings and content filter surfaces.
- **Global settings statistics wording**: The old analytics/statistics setting was replaced with diagnostics and crash-report wording, and the persisted settings model now uses `diagnosticDataConsent`.
- **Documents-based user data root**: Noctra user data now resolves under `Documents\Noctra` instead of `%LOCALAPPDATA%\Noctra` for settings, database, downloads, logs, and local runtime folders.
- **Default download folder moved**: The default download folder is now `Documents\Noctra\Downloads`; saved references to the old `%LOCALAPPDATA%\Noctra\Downloads` default are normalized to the new location while custom user-selected folders are preserved.
- **Settings file separation**: Global settings JSON now persists only global app state such as language/theme/update/hardware/promo/review data, while profile JSON files persist profile-specific playback, EPG, privacy, hidden category, and download preferences.
- **Encrypted promo grant storage**: Promo state is now stored as a DPAPI-protected `promoGrant` value in the global settings file instead of plaintext active code, expiry, and redeemed-code fields.
- **RemoteImage simplified**: The image control now has one job: normalize URL, use memory cache, download on demand, or show placeholder.
- **Poster-first VOD cards**: Movie cards prefer `LogoUrl`/provider poster before falling back to backdrop images.
- **On-demand image loading**: MainWindow image warmup and episode/card preload hooks were removed. Cards load their own images when they appear.
- **Post-load background work reduced**: The old startup warmup path no longer reloads home/favorites just to prepare images.
- **Provider poster ownership**: TMDB no longer replaces an existing provider poster just because it may be higher quality.
- **Provider-scoped Series artwork**: Stalker Series list parsing now reads the broader poster field set returned by portal category responses, while Xtream/Stalker list cards avoid slow per-card detail artwork requests.
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
- **Provider account display**: Settings account information now adapts to M3U, Xtream, and Stalker profiles with provider-specific labels and visible credential values.
- **README rewritten**: The project README was rebuilt into a shorter product, architecture, setup, and packaging guide.
- **CHANGELOG rewritten**: The old long-form maintenance log was replaced with a compact Keep a Changelog structure.
- **Store review metadata**: Store builds can provide `NoctraStoreProductId` or `NoctraStoreReviewLaunchUri` separately from premium purchase metadata.

### Removed

- **Hard-coded Premium pricing**: Price text, localized price keys, stale source comments, and the unused license-service price API were removed from active application code.
- **Main status bar connection health**: The bottom status bar connection health badge and its M3U playlist URL health-check backend were removed from the main app surface.
- **Header search popup**: The old header result popup and its async overlay search state were removed; header search now commits directly to the Search page.
- **Duplicate provider restriction**: The check that prevented adding the same M3U/Xtream/Stalker provider in multiple profiles was removed. Users can now use the same provider account across different profiles.

### Fixed

- **VLC airspace overlay focus recovery**: Video controls no longer disappear permanently after switching to another application and returning to Noctra. The detached overlay window restores its native z-order above the VLC HWND without taking activation.
- **Overlay controls after auto-hide**: Hidden playback controls can be shown again by pointer movement because the transparent overlay and mouse-capture surfaces remain native hit-testable.
- **PiP overlay lifecycle**: PiP controls remain recoverable after auto-hide, and leaving PiP restores normal player controls instead of leaving the overlay behind the video surface.
- **Owner-relative overlay behavior**: The video overlay follows the main window's visibility, minimized state, and PiP topmost state without foreground-process polling or forcing controls above unrelated applications.
- **Watermark text clipping**: Random watermark movement now transforms the complete watermark control instead of its inner border, preventing the first letter of "Noctra - Free" from being clipped at some positions.
- **Series detail empty-provider state**: Series with no provider seasons/episodes no longer keep stale Play/Continue episode actions, and the detail page now shows a no-episodes message instead of an endless loading spinner.
- **Series detail loading scope**: Episode loading UI is now tied to selected-series metadata loading, not global channel-list background loading.
- **Settings ComboBox overflow**: Refresh frequency, EPG timezone, and content filter ComboBoxes no longer clip selected text or resize awkwardly when labels are long.
- **Settings selection animations**: Selecting settings ComboBox values no longer incorrectly triggers unrelated tab/slide transition behavior.
- **Resume dialog time display**: The "Where you left off" time now restores itself while the resume dialog is visible, preventing blank position text when late playback state resets occur.
- **Mute state synchronization**: Restored mute persistence so reopening the app keeps UI mute state and actual player audio aligned; service volume events no longer unmute playback implicitly.
- **Profile settings leakage**: Promo code state and Microsoft Store review prompt state are no longer written into per-profile settings files.
- **Global settings leakage**: Profile-only values such as EPG refresh, custom EPG URLs, watch-history settings, and hidden group lists are no longer written into the global settings file.
- **Tampered promo grant handling**: Invalid or manually edited promo grant values no longer crash settings/license loading and simply leave the app in Free mode.
- **Stalker VOD poster fallback**: Stalker VOD list parsing now ignores blank poster fields such as `pic=""` and falls through to populated fields like `screenshot_uri`, fixing gray VOD cards when the provider already returns poster URLs.
- **Stalker Series episode artwork fallback**: Stalker Series and episode parsing now skips blank image fields and falls back through `screenshot_uri`, `icon`, `cover`, `movie_image`, `screenshot_url`, and the parent Series cover.
- **Xtream Series and episode artwork fallback**: Xtream Series detail, Series list, season, and episode parsing now use the same first-nonblank image fallback behavior across `cover`, `stream_icon`, `cover_big`, `movie_image`, `poster`, `image`, and `screenshot_uri`.
- **M3U account URL layout**: Long M3U playlist links in Settings now wrap inside the account card instead of overflowing or rendering as a form input.
- **Review prompt timing**: The review prompt is global across profiles, waits for an eligible main-menu surface, delays during the active session, never appears over video playback/overlay/PiP/details/global loading or other dialogs, and snoozes for 3 days when the user chooses Later.
- **Review dialog ownership**: The review prompt is no longer topmost, so it stays owned by the main app instead of floating independently over fullscreen video, while existing app dialogs keep their established topmost behavior.
- **Stalker progressive resume stalls**: Stalker category loading now times out stuck categories, clears their dummy card, and continues with the remaining queue instead of leaving progress frozen.
- **Progressive load UI stalls**: Cached Xtream/Stalker resume now runs off the UI thread, and Series/Home refresh work is deferred until channel loading completes.
- **Progress status overwrite**: Background aggregation no longer replaces active provider progress with "Channel List Ready" while the playlist is still loading.
- **Stalker resume matching**: Pending dummy categories now expose both group names and provider category IDs, so resume works even when localized or special-character group names shift.
- **Dummy Series aggregate leak**: Progressive `stalker-dummy://` and `xtream-dummy://` rows no longer create stale Series cards such as "Content loading".
- **Duplicated progress status text**: Channel-loading status now renders one primary progress message instead of repeating the same text twice in the status bar.
- **View-switch content bleed**: Live, Movies, and Series navigation now clears the target content surface before the new view renders, preventing old cards from flashing under the new page title.
- **Profile switch background loading leak**: Large Xtream/Stalker progressive loads are now cancelled and prevented from updating progress/status after profile switch or app close.
- **Connection health accuracy**: Connection badges no longer default to "good"; Add Profile and save validation now use the same provider auth/content rules, and M3U health checks are profile-scoped with GET fallback.
- **Category image stall**: Categories that reached the end of visible scroll without creating enough scrollable height can now request additional pages.
- **Search cards without posters**: Search and similar-result sections now queue visible VOD/series image fallback when provider posters are missing.
- **Provider Series poster latency**: Xtream/Stalker Series list cards no longer perform per-card provider detail artwork requests; only posters present in the provider list response are rendered immediately, keeping large lists responsive.

### Verification

- `dotnet build NoctraPlayer.sln` passes.
- `dotnet test Noctra.Tests\Noctra.Tests.csproj --no-restore` passes.

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
[1.1.0]: ./CHANGELOG.md
[1.0.0]: ./CHANGELOG.md
