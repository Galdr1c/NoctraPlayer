# Huawei Ads lifecycle and privacy hardening

**Date:** 2026-08-22  
**Scope:** Huawei/Petal Ads provider only; AdMob/GMS behavior remains unchanged

## Context

Commits `92f0b73` and `affa41a` made the HMS-only path functional on the
Huawei test device. Consent lookup returns provider metadata, explicit Huawei
request options are applied, test banners load and report impressions, and a
closed or hidden banner can be replaced.

The remaining work is hardening rather than restoring basic ad delivery:

- the Huawei smart banner can exceed the shared 50 dp host height,
- banner visibility is polled every 1.2 seconds,
- `OnAdLeave` can start a replacement request while Noctra is backgrounded,
- a consent-service failure leaves Settings able to offer personalized ads
  without a verified provider list,
- a comment incorrectly describes the production failure path as fail-closed.

## Safety boundary

This change does not modify:

- `AdMobMobileAdvertisingService`,
- Google UMP flow or AdMob identifiers,
- GMS signature/availability detection,
- the provider selection order,
- `IMobileAdvertisingService`,
- shared banner XAML dimensions,
- interstitial policy or entitlement rules.

The provider routing remains:

```text
genuine and available GMS -> AdMob
HMS/Huawei without genuine GMS -> Huawei/Petal Ads
neither provider -> NoOp
```

Existing AdMob and provider-selection contract tests must remain green. The
final diff must contain no change to the AdMob service or Android provider
factory.

## Design

### Huawei banner size

The Huawei provider will request `BannerSize32050`, matching the existing
50 dp Avalonia banner host. This avoids clipping without changing shared UI or
AdMob behavior.

This decision assumes the current AppGallery target is outside mainland China.
If mainland-China distribution is added later, Huawei's supported 360 x 57 or
360 x 144 sizes require a separate provider-aware host-height design.

### Event-driven banner lifecycle

`HuaweiBannerNativeControlHost` will subscribe to `MobileAppLifecycle.Paused`
and `MobileAppLifecycle.Resumed` while its native control exists and unsubscribe
on destruction.

The lifecycle state machine is:

```text
Paused
  -> BannerView.Pause()
  -> scheduled replacement re-checks foreground/host visibility and exits
  -> never request a new banner

OnAdLeave
  -> mark replacement pending
  -> do not load while backgrounded

OnAdClosed
  -> if foreground and host visible: schedule one replacement
  -> otherwise: mark replacement pending

Resumed
  -> BannerView.Resume()
  -> if replacement pending or banner is detached/hidden:
       schedule one replacement
```

A single reusable main-thread `Handler`, `_reloadScheduled`, and the existing
native generation guard will coalesce duplicate callbacks. Failed ad loads do
not trigger automatic retries, so no-fill or service errors cannot create a
request loop. Every delayed callback re-checks `MobileAppLifecycle.IsForeground`
and native-host visibility before requesting an ad; when either check fails it
records a pending replacement for the next resume.

The recursive `MonitorBannerVisibility` polling loop will be removed.

### Consent failure and Settings privacy choices

Consent lookup failure continues to use Huawei's non-personalized fallback:

```text
ConsentStatus = NonPersonalized
CanRequestAds = true
consent retry pending = true
providers = empty
```

The misleading fail-closed comment will be replaced with text describing the
actual NPA fallback.

When Settings requests Huawei privacy choices while consent retry is pending or
the provider list is unavailable:

1. perform one serialized consent refresh,
2. on success, preserve the returned status/provider list and present the
   provider-aware choices only when Huawei reports they are applicable,
3. on failure, keep NPA and show an informational dialog explaining that
   personalized ads are unavailable until provider information can be
   refreshed,
4. never expose the personalized button with an empty/unverified provider list.

The refresh path is a dedicated private operation under the provider's
initialization lock. It reuses `RequestConsentUpdateAsync` directly rather than
calling the general `InitializeAsync` flow, preventing a second automatic
consent dialog. A successful response with `NeedConsent == false` clears the
privacy-entry requirement and does not open a choice dialog. A successful
response with an empty provider list remains NPA-only. This path does not affect
Google UMP.

## Tests

Tests are written and observed failing before production changes.

Focused Huawei contracts cover:

1. Huawei uses `BannerSize32050` while shared XAML remains unchanged.
2. Recursive banner polling is absent.
3. Pause/resume calls Huawei `BannerView.Pause()` and `Resume()`.
4. `OnAdLeave` only records a pending replacement.
5. `OnAdClosed` reloads only when foreground and visible.
6. Resume coalesces one pending replacement through generation guards.
7. Consent failure retains NPA.
8. Empty provider metadata cannot lead to a personalized choice.
9. The production comment matches NPA fallback behavior.

Regression verification includes:

- all advertising contract tests,
- the full existing test suite with unrelated baseline failures recorded,
- zero-error Android Debug build,
- confirmation that AdMob and provider-factory source files are unchanged,
- HMS device smoke: load, impression, close/hide, background/resume, replacement,
  and no FATAL/ANR.

## Rollout

Huawei test ad IDs remain Debug-only and formal Huawei IDs remain Release build
inputs. This hardening does not change AppGallery identifiers or the AdMob
release configuration.
