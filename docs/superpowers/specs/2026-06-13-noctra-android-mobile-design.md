# Noctra Android Mobile Design

## 1. Goal

Build an Android phone and tablet version of Noctra in the existing solution.
The Android app will reuse `Noctra.Core` and Avalonia where practical, while
replacing Windows-specific lifecycle, playback, storage, notification, and
store integrations.

The first release will be distributed as a test APK, then through Google Play.
Android TV, Google TV, iOS, Chromecast, Android Auto, and offline downloads are
outside the first MVP.

## 2. Product Scope

The MVP includes:

- Local profile creation and management
- M3U, Xtream Codes, and Stalker Portal providers
- Live TV, movies, and series
- Content details, search, and favorites
- Basic EPG display
- Portrait navigation on phones
- Automatic landscape orientation during full-screen playback
- Phone and tablet layouts
- Media3 playback with LibVLC fallback
- Android system picture-in-picture
- Existing Free and Premium product rules
- One-time Premium purchase through Google Play Billing

Windows profiles and provider accounts will not synchronize with Android.
Users add their accounts independently on each Android device.

## 3. Architecture

The solution gains a `Noctra.Android` application project. The intended
ownership boundaries are:

### `Noctra.Core`

Owns reusable domain and data behavior:

- Provider clients and parsers
- Models and content grouping
- SQLite data access
- Profile, favorites, history, and EPG behavior
- Shared localization resources
- Free/Premium feature rules
- View-model logic that has no desktop dependency

Platform-specific calls currently embedded in the core must be moved behind
interfaces when required by Android. This is a targeted extraction, not a
general core rewrite.

### Shared Avalonia UI

Owns reusable visual assets and controls:

- Theme tokens, colors, typography, icons, and localization
- Media cards and reusable loading, empty, and error states
- Mobile pages implemented as `UserControl`-based views
- Responsive phone and tablet layouts

Desktop `Window` views will not be reused directly. Mobile navigation uses a
single application shell and page content. Desktop-only window chrome, resize,
position, detached overlay, and keyboard behavior remains in the desktop
project.

### `Noctra.Android`

Owns Android lifecycle and platform adapters:

- Avalonia Android application entry point
- Activity lifecycle and orientation
- Native player host and controls
- Media3 and Android LibVLC engines
- Android picture-in-picture
- Android Keystore-backed credential protection
- Storage Access Framework file selection
- Android notifications
- Network state observation
- Google Play Billing

## 4. Platform Upgrade

Before adding the Android target, the solution will be upgraded from its
current Avalonia 11.2.1 and .NET 8 baseline to versions that support the chosen
Android toolchain. The exact SDK and Avalonia package versions will be pinned
in the implementation plan after a compatibility spike.

The upgrade must preserve the Windows build and existing test suite before
mobile feature work begins.

## 5. Navigation and Layout

Phone navigation uses five bottom destinations:

1. Home
2. Live
3. Movies
4. Series
5. More

Search and profile actions appear in the top app bar. Favorites, settings,
profile management, legal pages, and Premium management are
accessible through More or contextual actions.

Phones use portrait orientation for browsing. Starting full-screen playback
switches to landscape. Returning from playback restores the previous browsing
orientation.

Tablets use the same information architecture but replace bottom navigation
with a navigation rail when space permits. Content grids adjust their column
count based on available width rather than fixed device categories.

## 6. Playback

Playback is isolated behind an engine-neutral service. Media3 is the primary
engine. Android LibVLC is the fallback for streams Media3 cannot open or play
reliably.

The fallback flow is:

1. Build a playback request containing the URL, media type, headers, user
   agent, and resume position.
2. Try Media3.
3. Classify startup failure or an unrecoverable playback failure.
4. Dispose the failed Media3 session.
5. Retry the request once with LibVLC.
6. If LibVLC also fails, stop retrying, log both failures, and show one concise
   user-facing error.

Fallback does not loop between engines. The selected engine is retained for
the active playback session.

### Native Player Surface

The video surface and interactive playback overlay live in the same native
Android container. Avalonia hosts that container but does not place Avalonia
controls over the native video surface. This avoids Android native-view
z-order limitations.

The native overlay owns:

- Play and pause
- Seek and progress for VOD
- Previous and next actions where applicable
- Audio and subtitle selection
- Full-screen exit
- PiP entry
- Sleep timer when Premium
- Free watermark

This design does not carry over the Windows detached overlay implementation.
Android has no `HWND`, owner window, `SetWindowPos`, transparent top-level
hit-testing, or foreground polling state.

### Picture-in-Picture

PiP uses Android's system picture-in-picture API. Entering PiP hides the
in-app overlay and exposes supported media actions through Android PiP
controls. Leaving PiP restores the full player state without recreating the
playback session.

Normal playback pauses when the app moves to the background unless the user is
in an active PiP session.

## 7. Storage and Security

Provider credentials and other secrets are protected with Android Keystore.
The existing Windows DPAPI implementation remains desktop-only.

SQLite, settings, cache, and logs use Android application storage paths.
M3U file import uses Android's Storage Access Framework instead of unrestricted
filesystem paths.

Uninstalling the app removes local profiles and data unless Android backup is
explicitly enabled in a later design.

## 8. Free and Premium

Android keeps the current product distinction:

### Free

- Up to 5 profiles
- Up to 2 custom EPG sources
- Manual EPG refresh
- Ads and Noctra watermark

### Premium

- Up to 12 profiles
- PIN protection
- Up to 10 custom EPG sources
- Automatic EPG refresh
- Resume playback
- Sleep timer
- Category hiding
- Advanced video buffer controls
- No ads or watermark

Android uses one application package. Premium is a one-time Google Play
in-app product. Purchase state is queried through Google Play Billing during
startup and after returning from a purchase flow. A mutable local Premium flag
is not considered authoritative.

The sideloaded APK uses an explicit development billing implementation for
testing and cannot perform a real purchase. Release builds intended for Google
Play do not expose the development override.

## 9. Provider and Data Flow

Provider setup validates input before saving:

- Xtream Codes verifies credentials and server response.
- M3U accepts a URL or a document selected through Android storage.
- Stalker Portal validates the portal and device identity configuration.

Successful setup saves the profile and loads the catalog into the profile's
local database. Refreshes use a replace-on-success strategy: a failed or empty
refresh does not delete the last usable local catalog.

Views query local data first. Provider refresh, metadata enrichment, and EPG
updates run asynchronously and publish state changes back to the UI.

## 10. Error Handling and Lifecycle

Provider errors are classified into authentication, timeout, invalid address,
network unavailable, invalid payload, and empty catalog cases.

Network changes may trigger bounded reconnection for active playback. Retry
logic uses cancellation and prevents stale requests from replacing a newer
playback or refresh operation.

Lifecycle transitions preserve the current destination, selected profile,
filters, and playback request. Recoverable activity recreation must not create
duplicate database refreshes or player sessions.

Diagnostic logs include the active playback engine and sanitized failure
information. Credentials, tokens, MAC addresses, and complete provider URLs
must not appear in logs.

## 11. Testing

Existing `Noctra.Core` tests remain part of the build. Platform abstractions
receive unit tests using fake implementations.

Required Android coverage includes:

- App startup and lifecycle recreation
- Profile creation for all three provider types
- Failed refresh preserving the existing catalog
- Phone and tablet responsive navigation
- Portrait browsing and landscape playback transitions
- Media3 success
- Media3 failure followed by one successful LibVLC fallback
- Both engines failing without a retry loop
- PiP entry, background behavior, and return
- Free limits and Premium feature gates
- Google Play purchase, restore, cancellation, and unavailable-store states
- Keystore credential round trip

Release validation includes physical-device testing on representative low,
middle, and high supported Android API levels using live, VOD, and series
streams with different codecs and container formats.

## 12. Delivery Phases

1. Upgrade and preserve the Windows baseline.
2. Extract platform interfaces and keep existing Windows behavior passing.
3. Add the Avalonia Android shell, mobile theme, navigation, and profile flow.
4. Add providers, local catalog views, search, favorites, and basic EPG.
5. Add the native player host, Media3, LibVLC fallback, orientation, and PiP.
6. Add Free/Premium gates and the development billing adapter.
7. Produce and test the sideloaded APK.
8. Add Google Play Billing verification and prepare the Play Store release.

Each phase must leave the Windows application buildable and tested.

## 13. Explicitly Deferred

- Android TV and Google TV remote-focused UI
- iOS
- Chromecast
- Android Auto
- Offline downloads
- History UI and advanced viewing-history management
- Windows-to-Android profile transfer
- Cloud synchronization
- Advanced EPG source management
- Desktop feature parity outside the approved MVP
