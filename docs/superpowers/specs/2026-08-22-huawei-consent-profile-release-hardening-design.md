# Huawei consent and release hygiene hardening

**Date:** 2026-08-22  
**Scope:** Existing Android APK with GMS AdMob and HMS Petal Ads fallback

## Context

The current HEAD is `83823ab56cdea509d19bcd4c9b80dd6bff0d3c46`. The AdMob native
banner host and player surface composition work is already in place. The Huawei
provider added in `7ad0c26` compiles and routes correctly, but its consent state
machine discards `ConsentStatus` and the provider list, does not reliably
initialize the SDK after a Settings choice, and treats a transient consent
network failure as a completed dead-end. The AdMob test-device default is
currently not restricted to Debug builds.

## Goals

- Preserve Huawei `ConsentStatus`, `NeedConsent`, and `AdProvider` data through
  the provider boundary.
- Show the consent UI only when the returned status is `Unknown` and consent is
  required; reuse an existing personalized/non-personalized choice otherwise.
- Display the returned provider name, service area, and privacy-policy URL in
  the Huawei privacy UI.
- Apply the selected personalization mode explicitly through Huawei global
  `RequestOptions` before requesting banners or interstitials.
- Keep consent/network failures retryable and allow a non-personalized fallback
  where Huawei permits it.
- Ensure a consent choice made in Settings initializes Huawei Ads in the same
  process when the SDK was not initialized at startup.
- Keep AdMob and Huawei test identifiers out of production defaults unless a
  release build explicitly supplies formal values.
- Verify each behavior with failing-first tests and a final Android smoke run.

## Non-goals

- No AppGallery billing, review, update, or distribution flavor in this change.
- No replacement of the existing `IMobileAdvertisingService` contract.
- No change to the existing AdMob production unit IDs or Google UMP flow.
- No redesign of the general Settings navigation or profile card visuals.

## Design

### Huawei consent state

The Huawei provider owns a small consent snapshot containing:

```text
ConsentStatus status
bool needConsent
IReadOnlyList<AdProvider> providers
bool canRequestAds
bool privacyOptionsRequired
string? error
```

`ConsentUpdateResult` carries `Status`, `NeedConsent`, `Providers`, success, and
error information. The Java callback adapter copies the provider list instead
of dropping it.

The startup decision is:

```text
update failed
  -> keep initialization retryable
  -> request only non-personalized ads when policy allows

update succeeded and NeedConsent == false
  -> use the returned status
  -> no consent dialog

update succeeded and NeedConsent == true and Status == Unknown
  -> show consent dialog with provider details

update succeeded and NeedConsent == true and Status != Unknown
  -> reuse the stored personalized/non-personalized choice
  -> no duplicate startup dialog
```

Closing the dialog without a choice leaves the status `Unknown`. If Huawei
allows an ad request in that state, the request is explicitly
non-personalized; otherwise the provider remains hidden but retryable.

### Provider disclosure UI

The Huawei privacy dialog keeps the two existing choices and adds a provider
details action/list. Each provider row can show `Name`, `ServiceArea`, and
`PrivacyPolicyUrl`; missing URLs are rendered as non-clickable text. Opening a
URL uses the existing Android activity/browser boundary and never blocks the
consent completion task.

The UI is intentionally provider-neutral: Huawei’s returned list is the source
of truth, and no ad technology provider is hard-coded into the dialog.

### RequestOptions and SDK lifecycle

The provider maps the consent status to Huawei’s `NonPersonalizedAd` values:

```text
Personalized     -> AllowAll
NonPersonalized  -> AllowNonPersonalized
Unknown          -> AllowNonPersonalized when a fallback request is allowed
```

It updates `HwAds.RequestOptions` before any `BannerView.LoadAd` or
`InterstitialAd.LoadAd` call. Consent choice and request-option updates happen
on the main thread.

`EnsureHuaweiAdsInitializedIfEligibleAsync` is idempotent and guarded by the
existing initialization semaphore. It runs from:

- the first successful consent/fallback path,
- `InitializeAsync` when called again after startup,
- `ShowPrivacyOptionsAsync` after a choice changes,
- the free-entitlement transition when consent is already usable.

If consent update fails, initialization state is not permanently completed in a
way that prevents retry. A later eligibility notification or explicit privacy
choice can retry the update/SDK initialization.

Premium transitions dispose preloaded interstitials and suppress banners as
before. Returning to the free tier reuses the stored consent/request options and
initializes Huawei Ads if necessary.

### Release configuration

`NoctraAdMobTestDeviceIds` receives its Xiaomi test hash only under Debug by
default. Release metadata contains no test device unless a build explicitly
passes the property.

Huawei banner/video-interstitial/image-interstitial test slots remain Debug
defaults. Release values remain empty and the provider fails closed until formal
Petal Publisher slot IDs are supplied through build properties.

The provider factory keeps this order:

```text
Debug override (only when explicitly enabled)
  -> HMS-only / Huawei-family device with no genuine Google signature
  -> AdMob when Google Play Services is genuine and available
  -> NoOp when neither provider is available
```

Android package visibility declarations remain present for both GMS and HMS
package checks. The existing Google signature check remains the primary GMS
identity check; availability validation is additive and fail-closed.

## Tests and acceptance

Tests are written before each production change and observed failing. The
focused contract suite must cover:

1. Consent result preserves status and provider metadata.
2. A known personalized/non-personalized status does not reopen the dialog.
3. Unknown + required status opens the provider-aware dialog.
4. Settings consent choice applies `RequestOptions` and initializes the SDK.
5. Consent network failure is retryable and uses only non-personalized fallback.
6. AdMob test-device metadata is Debug-only and Huawei test slots are not
   defaulted in Release.

Verification then consists of:

- focused advertising tests,
- the full existing regression suite with unrelated baseline failures recorded,
- a zero-error Android Debug arm64 build,
- a GMS smoke run proving AdMob selection is unchanged,
- an HMS smoke run proving provider selection, consent choice, request options,
  banner failure collapse, and process stability.

## Rollout boundary

This design hardens the existing single APK. AppGallery billing/update/review
abstraction is deliberately documented as a later project because it requires
separate store capabilities and release configuration.
