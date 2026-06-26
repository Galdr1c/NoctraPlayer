# Noctra Mobile vs Desktop Parity Analysis

Date: 2026-06-26
Scope: `Noctra.Mobile`, `Noctra.Android`, `Noctra.Avalonia`, shared `Noctra.Core` ViewModels/services.

This report supersedes older patch-based analyses. It reflects the current repository state after the native Android splash, profile-gated startup, profile UI polish, search/profile fixes, and mobile series-detail navigation fix.

## Executive Summary

Mobile is no longer a raw or early patch. Startup/profile, profile loading, PIN, avatar picker, search basics, player gestures, Android services, and many material icon fixes are already implemented. The remaining parity gaps are concentrated in:

1. Downloads UI richness and converters.
2. Settings/global settings visual parity.
3. Library card context actions and reusable row/card controls.
4. Search/Live duplicate card templates and scroll paging.
5. Legal document/decline UX.
6. Home continue watching layout polish.

## Current Startup Flow

Desktop:
`SplashWindow -> LegalConsentWindow if needed -> ProfilesWindow -> ProfileLoadingWindow -> MainWindow`

Android/mobile:
`Android native splash_screen.xml -> MobileLegalConsentView if needed -> ProfilesOverlay -> ProfileLoadingView -> MainView/Home`

Current status:
- `MainView.axaml` no longer contains `SplashOverlay`.
- Android native splash is configured through `Noctra.Android/Resources/drawable/splash_screen.xml`.
- `ProfilesOverlay` is the initial Avalonia gate.
- Home shell is hidden until a profile is loaded.
- Back while profiles overlay is visible is consumed.
- Settings/More navigation closes stale series detail state.

## A. Startup and Profile Flow

| Area | Desktop | Mobile | Status | Work Remaining |
|---|---|---|---|---|
| Splash | `SplashWindow.axaml` | Android native `splash_screen.xml` | Mobile intentionally diverges | No Avalonia splash work. If needed, align Android 12+ `values-v31/styles.xml` icon/background separately. |
| Legal consent | `LegalConsentWindow` + `LegalDocumentWindow` | `MobileLegalConsentView` + Android native document dialog | Partial | Add mobile decline flow if desired. Add scrollable `MobileLegalDocumentView` for Terms/Privacy parity. |
| Profile selection | `ProfilesWindow` | `ProfilesOverlay` + `ProfileListView` | Strong parity | Optional: add header logo polish and safe-area padding for overlay header. |
| Profile cards | `ProfilesWindow` styles | `ProfileListView` | Strong parity | Check mobile sizing on small phones; otherwise major previous gaps are closed. |
| Profile loading | `ProfileLoadingWindow` | `ProfileLoadingView` | Strong parity | Minor typography/elevation tuning only. |
| Profile setup | `AddProfileWindow` | `ProfileSetupView` | Good, not full parity | Connection health visual converters are still missing; first PIN box still has `PasswordChar="*"` while new PIN fields use bullet. |
| PIN entry | `PinEntryWindow` | `PinEntryView` | Strong parity | Minor spacing/focus polish only. |
| Avatar picker | `AvatarPickerWindow` | `AvatarPickerView` | Strong parity | No immediate backend gap. |

Backend notes:
- `MobileLegalConsentView.ShowConsentFlowAsync()` persists accepted version, privacy version, timestamp, and diagnostic consent.
- There is no decline result path in mobile. Current behavior is a hard gate: accept or stay blocked.
- Profile loading closes overlay only after successful profile load via `ProfileLoaded`.

## B. Main Shell and Navigation

| Area | Desktop | Mobile | Status | Work Remaining |
|---|---|---|---|---|
| Shell host | `MainWindow` | `MainView` | Mobile-specific | Keep divergent. Desktop window shell cannot be copied directly. |
| Navigation | Desktop sidebar/top chrome | Bottom nav + tablet rail | Good | Add selected-state visual parity if needed. |
| Profile gate | Desktop blocks main window until profile | Mobile now hides shell until profile | Fixed | Keep regression tests. |
| Series detail stale state | Desktop core navigation resets | Mobile now calls `CloseSeriesDetailIfOpen` for Settings/More | Fixed | Add device verification after install. |
| Settings path | Global/settings windows | In-shell `MobileSettingsView` | Functional | Visual parity remains a major task. |

Backend notes:
- `MainView.axaml.cs` is still relatively heavy and coordinates core VM, player VM, profile overlay, and shell navigation.
- A future `MobileShellViewModel` would reduce code-behind, but this is architectural cleanup, not a blocking bug.

## C. Home and Content Screens

| Screen | Desktop | Mobile | Status | Work Remaining |
|---|---|---|---|---|
| Home | `HomeView` + `ContinueWatchingCard` | `MobileHomeView` + `MobileContinueWatchingCard` | Partial | Continue watching uses `ScrollViewer HorizontalScrollBarVisibility=Auto` around `WrapPanel`; revisit for phone layout. Ensure progress/hover transitions fully match mobile intent. |
| Live | `LiveView` + `LiveTvCard` | `MobileLiveView` inline card template | Partial | Extract `MobileLiveTvCard`; add shared responsive columns/scroll paging if needed. |
| Movies | `MoviesView` + `VodCard` | `MobileMoviesView` + `MobileVodCard` | Good | Add history-specific remove action and watched progress visibility parity. |
| Series | `SeriesView` + `SeriesCard` | `MobileSeriesView` + `MobileSeriesCard` | Good | Same card/action parity as movies. |
| Series detail | Desktop detail is inside core main flow | Dedicated `MobileSeriesDetailView` | Mobile-specific | Add tooltip/accessibility, validate layout on small phone, keep stale-state fix. |
| Search | `SearchView` | `MobileSearchView` | Improved partial | Enter key and empty state exist. Still duplicate inline templates and no desktop `ScrollPaging` equivalent. |

Backend notes:
- Mobile live/movies/series scroll handlers are single-screen local handlers, not shared `ScrollPaging`.
- Search currently handles Enter but does not share desktop multi-pass paging.

## D. Library Screens

| Screen | Desktop | Mobile | Status | Work Remaining |
|---|---|---|---|---|
| Favorites | `FavoritesView` | `MobileFavoritesView` | Partial | Add remove/toggle context actions or visible overflow actions; consider `MobileLibraryRow`. |
| My List | `MyListView` | `MobileMyListView` | Partial | Same as Favorites. |
| History | `HistoryView` | `MobileHistoryView` | Partial | Add `RemoveFromHistory` actions for live/VOD/series rows/cards. |

Backend notes:
- Desktop `VodCard` and `SeriesCard` expose `ShowHistoryMenu` and remove handlers.
- Mobile card flyouts currently expose My List/Favorite actions but not history remove parity.

## E. Downloads

| Area | Desktop `DownloadsView` | Mobile `MobileDownloadsView` | Status | Work Remaining |
|---|---|---|---|---|
| Storage card | Rich segmented storage | Simpler storage section | Partial | Add segmented quota bar/legend if desired. |
| Active downloads | Rich status/progress chrome | Functional active list | Partial | Add status-colored progress converter and richer active item cards. |
| Queue | Desktop queue visuals | Mobile queue list | Partial | Add size formatting and queue mini icon parity. |
| Completed downloads | Series/VOD sections exist | Mobile sections exist | Partial | Add mini poster/status badge parity. |
| Converters | Desktop monolithic converters | Mobile lacks several download converters | Partial | Add `DownloadStatusToBrushConverter`, `BytesToHumanConverter`, `DoubleToStarGridLengthConverter` as needed by XAML. |

Backend notes:
- Most download commands/properties already live in shared `MainViewModel`.
- This phase is primarily XAML + converter registration unless new mobile interactions are added.

## F. Settings and Legal

| Area | Desktop | Mobile | Status | Work Remaining |
|---|---|---|---|---|
| Theme picker | Preview tiles with code-behind selection | Toggle/ComboBox style settings | Partial | Add mobile theme preview tiles and `UpdateThemeSelection` logic. |
| ComboBox styling | `ModernComboBox` | Default Fluent/mobile ComboBox | Partial | Port mobile-safe `ModernComboBox` style or create mobile variant. |
| Premium/About | Rich desktop cards | Mobile has premium/about/update sections | Partial | Improve card visual hierarchy and parity. |
| Promo code | Desktop gradient/status styling | Mobile functional promo section | Partial | Add success/warning brush converter or equivalent. |
| Legal docs | Desktop document window | Android native alert dialog | Partial | Add `MobileLegalDocumentView` for scrollable in-app legal docs. |
| Decline consent | Desktop has decline | Mobile has no decline | Deliberate gap | Decide product behavior: hard gate vs explicit decline/exit. |

Backend notes:
- `MobileSettingsView.axaml.cs` is still minimal.
- Theme tile parity requires pointer handlers and visual state updates.

## G. Player and Overlay

| Area | Desktop | Mobile | Status | Work Remaining |
|---|---|---|---|---|
| Playback service | Desktop LibVLC/overlay window | Android MediaPlayer + TextureView | Platform-specific | Media3/ExoPlayer remains future improvement for tracks/adaptive streams. |
| Overlay controls | `VideoOverlayView` | `MobilePlayerView` | Good mobile-specific | Check sleep timer premium lock overlays, panel transitions, EPG row sizing. |
| Gestures | Desktop mouse/keyboard | Mobile gesture layer | Mobile-specific complete-ish | Device test required for pinch/seek/volume/brightness. |
| PiP | Desktop window PiP-like behavior | Android PiP service | Platform-specific | Device verification only. |
| Watermark | `WatermarkView` | `MobileWatermarkView` | Parity | No action. |

Backend notes:
- Mobile player code is large but intentionally platform-heavy.
- Android playback service lacks the same track-selection richness as LibVLC. This is not pure UI parity; it is media backend parity.

## H. Resources, Styles, Converters

Already present in mobile:
- `ResponsiveCardMetricConverter`
- `PinDotConverter`
- `ProfileColorConverter`
- `UrgencyToColorConverter`
- `TimeSpanToCountdownConverter`
- `IconGradientBrush`
- `FocusGlow`
- `FillModeToIconConverter`
- Subtitle/player converters

Still missing or not implemented as separate mobile resources:
- `WidthToColumnsConverter`
- `DoubleToStarGridLengthConverter`
- `DownloadStatusToBrushConverter`
- `BytesToHumanConverter`
- `BoolToMaterialIconKindConverter`
- `BooleanToSuccessWarningBrushConverter`
- `WatchedProgressVisibilityConverter`
- `ConnectionHealthToVisibilityConverter`
- `ConnectionHealthToIconConverter`
- `ConnectionHealthToBrushConverter`
- `NullToVisibilityConverter` with invert parameter
- `Resources/Styles.axaml` shared mobile style bundle
- `ModernComboBox` mobile style
- `ScrollPaging.cs` mobile equivalent

## Implementation Plan

### Phase 0 - Verification Baseline

1. Run full tests and Android build before feature work.
2. Install on connected device and capture startup/profile/settings navigation screenshots.
3. Keep current regression tests for startup/profile/settings-series-detail.

### Phase 1 - Blocking Navigation and Shell Correctness

Goal: no screen can trap input behind stale state.

Tasks:
1. Device-verify startup: native splash -> legal if needed -> profiles -> profile loading -> home.
2. Device-verify Settings from every bottom tab and from More.
3. Add regression tests for PlayerHost/SelectedMediaHost visibility when switching shell destinations if symptoms appear.

Expected files:
- `Noctra.Mobile/Views/MainView.axaml`
- `Noctra.Mobile/Views/MainView.axaml.cs`
- `Noctra.Tests/ReleaseSourceCleanlinessTests.cs`

### Phase 2 - Library Card Actions

Goal: Favorites/MyList/History match desktop action capability.

Tasks:
1. Add mobile `ShowHistoryMenu` equivalent to `MobileVodCard` and `MobileSeriesCard`.
2. Add `RemoveFromHistory` flyout item for history context.
3. Add visible overflow/remove actions to live row templates where needed.
4. Add tests for `RemoveFromHistoryCommand`, `RemoveFromFavoritesCommand`, `RemoveFromMyListCommand` presence.

Expected files:
- `Noctra.Mobile/Controls/MobileVodCard.axaml(.cs)`
- `Noctra.Mobile/Controls/MobileSeriesCard.axaml(.cs)`
- `Noctra.Mobile/Views/MobileFavoritesView.axaml(.cs)`
- `Noctra.Mobile/Views/MobileMyListView.axaml(.cs)`
- `Noctra.Mobile/Views/MobileHistoryView.axaml(.cs)`

### Phase 3 - Downloads Parity

Goal: mobile downloads has desktop-level status clarity, adapted to phone layout.

Tasks:
1. Add required converters: `DownloadStatusToBrushConverter`, `BytesToHumanConverter`, `DoubleToStarGridLengthConverter`.
2. Improve storage card with segmented usage/legend.
3. Improve active/queued/completed item cards with poster, status color, speed/size text.
4. Keep phone-safe touch targets and no horizontal overflow.

Expected files:
- `Noctra.Mobile/Views/MobileDownloadsView.axaml`
- `Noctra.Mobile/Converters/*`
- `Noctra.Mobile/App.axaml`
- `Noctra.Tests/ReleaseSourceCleanlinessTests.cs`

### Phase 4 - Settings and Legal UX

Goal: mobile settings is functionally equal and visually close to desktop global settings.

Tasks:
1. Add theme preview tiles and code-behind selection state.
2. Add mobile `ModernComboBox` style or style bundle.
3. Polish premium/about/promo/update cards.
4. Add promo success/warning brush converter or equivalent.
5. Decide and implement legal consent decline behavior.
6. Add `MobileLegalDocumentView` if in-app document reading is required.

Expected files:
- `Noctra.Mobile/Views/MobileSettingsView.axaml(.cs)`
- `Noctra.Mobile/Views/MobileLegalConsentView.axaml(.cs)`
- `Noctra.Mobile/Views/MobileLegalDocumentView.axaml(.cs)` if chosen
- `Noctra.Mobile/Resources/Styles.axaml`
- `Noctra.Mobile/App.axaml`

### Phase 5 - Search, Live, and Reusable Cards

Goal: reduce duplicate inline templates and align interactions.

Tasks:
1. Extract `MobileLiveTvCard`.
2. Extract `MobileSearchResultCard` or typed result cards.
3. Add mobile `ScrollPaging` equivalent and wire search/live/movies/series/favorites/mylist/history where appropriate.
4. Re-test pagination with real large lists.

Expected files:
- `Noctra.Mobile/Controls/MobileLiveTvCard.axaml(.cs)`
- `Noctra.Mobile/Controls/MobileSearchResultCard.axaml(.cs)`
- `Noctra.Mobile/Views/ScrollPaging.cs`
- relevant mobile views/code-behind

### Phase 6 - Home Layout Polish

Goal: home continue-watching behaves correctly on phones.

Tasks:
1. Revisit nested `ScrollViewer` + `WrapPanel`.
2. Confirm card widths across 360, 390, 480, tablet widths.
3. Keep pulsing empty logo.
4. Add regression/source tests for no unwanted horizontal overflow pattern if feasible.

Expected files:
- `Noctra.Mobile/Views/MobileHomeView.axaml`
- `Noctra.Mobile/Controls/MobileContinueWatchingCard.axaml`

### Phase 7 - Player Polish and Backend Parity

Goal: avoid UI regressions and document backend limits.

Tasks:
1. Compare mobile panel transitions to desktop overlay.
2. Add sleep timer premium lock overlays if missing.
3. Re-check EPG row sizing and focus behavior.
4. Device test pinch, pan, gestures, lock, PiP, subtitle/audio panels.
5. Record Android MediaPlayer limitations and decide whether Media3/ExoPlayer is a separate project.

Expected files:
- `Noctra.Mobile/Views/MobilePlayerView.axaml(.cs)`
- `Noctra.Android/Services/AndroidVideoPlayerService.cs`
- `Noctra.Android/Services/AndroidVideoSurfaceService.cs`

## Suggested Priority

1. Phase 1: device verification and no-trap navigation.
2. Phase 2: library actions because these are user-facing functional gaps.
3. Phase 3: downloads because desktop has a much richer equivalent.
4. Phase 4: settings/legal because it affects trust and account management.
5. Phase 5: search/live refactor and paging.
6. Phase 6: home polish.
7. Phase 7: player polish/backend parity.

## Verification Commands

```powershell
dotnet test .\Noctra.Tests\Noctra.Tests.csproj --no-restore
dotnet build .\Noctra.Android\Noctra.Android.csproj -c Debug --no-restore -p:AndroidSdkDirectory="$env:LOCALAPPDATA\Android\Sdk" -p:EmbedAssembliesIntoApk=true -p:AndroidUseSharedRuntime=false -p:AndroidFastDeploymentType=
```

For device validation:

```powershell
adb devices
adb install --user 0 -r -d .\Noctra.Android\bin\Debug\net10.0-android36.0\*.apk
adb shell monkey -p studio.kynora.noctra 1
adb logcat -d -t 500
```
