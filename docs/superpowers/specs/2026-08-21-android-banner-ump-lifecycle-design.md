# Android banner host and UMP lifecycle design

**Date:** 2026-08-21  
**Scope:** Android/Avalonia banner rendering and UMP consent-state boundaries

## Context and verified root cause

The current UMP flow already performs the important consent operations: it waits
for Noctra's legal consent, requests UMP consent information, refreshes cached
state after consent forms, exposes Privacy Choices only when UMP reports
`PrivacyOptionsRequirementStatus.Required`, and checks form errors.

The remaining banner defect is in the Android native host boundary:

1. `CreateBannerAd` constructs `AdView` with the service/application context.
2. `LoadAd` is posted before Avalonia has created and attached the native host.
3. `BannerNativeControlHost` wraps the raw `AdView`; it does not provide a
   parent-context `FrameLayout` container.
4. `MobileBannerAdControl` treats a non-null disposable handle as equivalent to
   a loaded creative, so a failed or still-loading ad can leave an empty 50dp
   row that still receives touches.
5. `BannerAdHandle.Dispose` and `BannerNativeControlHost.DestroyAd` both destroy
   the same `AdView`.
6. The public `CanRequestAds` property currently combines UMP consent with
   entitlement, although `CanServeAds` is the appropriate combined ad-serving
   gate.

Live device evidence exposed a second Android composition boundary after the
host/load fixes: the banner receives `loaded`, `impression`, `clicked`, and
valid native measurements, but its creative is hidden by Avalonia's
`SurfaceView`. `MainActivity.ConfigureAvaloniaOverlaySurface` currently calls
`SetZOrderOnTop(true)` globally. Android places that surface above the whole
window, so the normal Android `AdView` below it cannot be visible.

## Goals

- Host `AdView` using the Android context supplied by Avalonia's native parent.
- Create the native container and attach the `AdView` before calling `LoadAd`.
- Keep the standard 320x50 banner centered in a full-width native container.
- Keep the Avalonia banner row collapsed until `OnAdLoaded`.
- Collapse it after `OnAdFailedToLoad`, consent withdrawal, premium transition,
  or disposal.
- Make destruction idempotent and owned by one handle/host path.
- Keep UMP consent state independent from the free/premium ad entitlement:
  `CanRequestAds` reports UMP's raw request permission, while `CanServeAds`
  remains the effective entitlement + consent + SDK gate.
- Preserve the existing `PrivacyOptionsRequirementStatus.Required` visibility
  rule and the existing test-device/debug-geography configuration.
- Keep Avalonia above the native video surface while the player is active, but
  keep it behind normal Android window content while the shell/banner is active.

## Non-goals

- No change to AdMob unit IDs, production privacy message configuration, or
  Google UMP geography policy.
- No manual “limited ads” or “non-personalized ads” choice in `AdRequest`.
- No redesign of the Settings Privacy Choices UI.
- No replacement of the existing native video `TextureView` or player activity.
- No popup/overlay-window implementation for advertising.

## Design

### Bootstrap invocation boundary

The Android activity may reach `OnCreate` before Avalonia has finished
constructing `App.Services`. The activity-side bootstrap attempt therefore
remains best-effort, but `MainView.OnAttachedToVisualTree` also starts the same
idempotent bootstrap after the service provider is ready. The provider's
initialization semaphore/state machine makes the two attempts safe. This
guarantees that the UMP refresh is not silently skipped for returning users.

### Native host lifecycle

`BannerNativeControlHost` receives the ad unit ID and a load-state callback,
not a pre-created `AdView`. The provider boundary carries a small
`BannerAdLoadState` value (`Loading`, `Loaded`, or `Failed`) so the Avalonia
control can distinguish an in-flight request from a retryable no-fill. During
`CreateNativeControlCore(parent)` it:

1. Resolves the context from `AndroidViewControlHandle.View.Context`, falling
   back to `Application.Context` only if the parent handle is not Android.
2. Creates a full-size `FrameLayout` using that context.
3. Creates a 320x50 `AdView`, assigns the fixed `AdSize.Banner` and unit ID.
4. Adds the `AdView` with wrap-content layout parameters and centered gravity.
5. Stores the container/view fields and installs the listener.
6. Calls `LoadAd` only after the view is in the native container.

The listener reports `Loaded` only from `OnAdLoaded` and `Failed` from
`OnAdFailedToLoad`. Disposal reports no further load state after the handle has
been invalidated. The `MobileBannerAdControl` callback is posted to the
Avalonia UI thread before changing visibility.

### Avalonia control state

`MobileBannerAdControl` keeps an explicit load state and a generation number.
`LoadAd` sets the state to `Loading` before requesting a new handle.
Because Avalonia does not create a `NativeControlHost` child while its parent
is invisible, the loading host remains attached but is transparent and
non-interactive. `UpdateVisibility` therefore applies these rules:

```text
handle exists AND NOT suppressed       -> host participates in layout
load state == Loaded                   -> opacity=1 and hit testing enabled
load state == Loading                  -> opacity=0 and hit testing disabled
load state == Failed/Idle              -> host is collapsed
```

The transient loading row cannot open an ad link and disappears entirely on a
failed/no-fill result; only a loaded creative is visible or clickable.

`ClearAd` invalidates the generation, disposes the handle, clears content and
resets the state. A `Failed` callback invalidates and releases the failed handle
so a later eligibility notification can create a fresh request. A callback
from an older generation is ignored, preventing a late load from making a new
banner visible after navigation, suppression, or consent withdrawal.

### Surface composition mode

Player visibility, not fullscreen state, owns the Avalonia surface order.
`IPlayerWindowService` gains `SetPlayerOverlayActive(bool)`. `MainView` calls it
with `true` before exposing `PlayerHost`, and calls it with `false` on every
player close/startup-failure path.

The Android implementation delegates to `MainActivity`, which stores the
requested composition mode:

```text
Shell / banner:
  Avalonia SurfaceView SetZOrderOnTop(false)
  -> normal Android AdView is visible above the surface

Player active:
  Avalonia SurfaceView SetZOrderOnTop(true)
  -> Avalonia controls remain above native video TextureView
  -> banner is suppressed
```

`ConfigureAvaloniaOverlaySurface` uses the stored mode on activity creation,
resume, failed-PiP recovery, and PiP exit instead of always forcing `true`.
Android 11+ applies the z-order change dynamically. On Android 9/10, the
implementation recreates the backing surface by briefly detaching and posting
its restoration after changing z-order; no player state or view-model is
recreated.

The provider boundary remains usable by NoOp and preview implementations; they
continue to return no handle and never expose a visible banner.

### Consent and entitlement boundary

The Android service retains the existing UMP sequence and Privacy Choices
visibility condition. Only the property semantics are separated:

```text
CanRequestAds = UMP CanRequestAds()
CanServeAds   = free entitlement AND CanRequestAds AND Mobile Ads initialized
```

Premium users still run the UMP refresh and can receive an accurate Privacy
Choices state, but they never create banner/interstitial ads because
`CanServeAds` remains false. Consent withdrawal clears any preloaded
interstitial and emits the existing eligibility/consent notifications.

### Error and disposal handling

- A missing activity, canceled operation, missing ad unit, UMP error, or ad
  no-fill is a non-fatal false/hidden state and is logged at the Android
  provider boundary.
- `DestroyAd` is idempotent and destroys/removes the native view exactly once.
- `BannerAdHandle.Dispose` delegates destruction to the host and does not call
  `AdView.Destroy` independently.
- A late load/failure callback after disposal cannot make the Avalonia control
  visible; the handle's disposed state gates callback delivery.

## Tests and verification

Tests are added before production changes and must fail against the current
implementation. They cover:

1. Native host source contract: `AdView` creation occurs in
   `CreateNativeControlCore`, parent context/container creation precedes
   `LoadAd`, and the host uses a centered wrap-content child.
2. Disposal contract: only the host owns `AdView.Destroy`; the handle does not
   issue a second destroy call.
3. Banner state contract: loading/handle-only is hidden, loaded is visible
   when not suppressed, failed is hidden and retryable, and suppression always
   hides it. A stale callback cannot revive a newer generation.
4. Consent contract: `CanRequestAds` is not gated by premium entitlement while
   `CanServeAds` remains gated.
5. Composition contract: shell mode does not call unconditional
   `SetZOrderOnTop(true)`; player open requests overlay mode before showing the
   player, and every close/failure path restores shell mode.

After the red tests, implement the smallest changes needed for green tests,
then run the focused advertising tests, the full test suite, an Android Debug
build, and a data-preserving ADB smoke test. The smoke test records:

- UMP/Privacy Choices logs and banner loaded/failed/metrics logs,
- banner visible/collapsed behavior on a test device,
- repeated Settings navigation and consent refresh,
- player open/close and background/foreground cycles,
- crash/ANR/OOM logcat scan and process/PID stability.

## Acceptance criteria

- No blank clickable banner row when the request is loading or fails.
- A loaded test creative is visible inside the 320x50 centered native host.
- Privacy Choices remains hidden when UMP says `NotRequired` and available when
  UMP says `Required`, including after a form change.
- Premium users never serve ads but still receive refreshed UMP state.
- The UMP bootstrap is observable and runs after services are ready even when
  the earlier activity hook ran too early.
- A loaded banner creative is visually visible in shell mode, not merely
  loaded/clickable behind Avalonia.
- Opening the player preserves video plus Avalonia controls; closing it restores
  the banner and does not leave a stale native surface.
- Focused and full tests pass; Android build has zero errors; smoke test has no
  new crash/ANR/OOM and no duplicate native-destroy failure.
