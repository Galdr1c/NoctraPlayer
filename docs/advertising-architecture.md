# NoctraPlayer Advertising Architecture

This document describes the advertising placement/policy foundation: a persistent
banner above the bottom navigation bar (free users only) and a playback-exit
interstitial. Feed/domain collections never receive ad objects and navigation
never depends on an ad network.

## Placements

| Placement | Surface | Audience |
|---|---|---|
| Banner | `MainView` — `BottomNavigation`'un hemen üstü, tüm sayfalarda persistent | Free users |
| Playback exit interstitial | Player kapanışında | Free users, eşikler sağlandığında |

### Persistent banner

`MobileBannerAdControl` lives in the content column of `MainView.axaml`
(`Grid.Row="1" Grid.Column="1"`, `RowDefinitions="*,Auto"`), pinned to the bottom
of that column. Behavior:

- Rendered on every shell page (Home, Live, Movies, Series, Search, Downloads,
  More) because it lives in the shell, not in a page.
- In portrait it sits directly above the bottom navigation bar; in landscape/
  tablet mode (width ≥ 720, navigation rail active) it stays at the bottom of
  the content area, to the right of the rail.
- Hidden while the player is open — the player hides `ShellLayer`, which
  collapses the banner with it.
- Collapses to zero height (`IsVisible=false`) for premium users, when consent/
  SDK is not ready, or when the provider returns no ad — it never reserves
  blank screen space.
- `MainView.LoadBannerAdIfEligible()` asks `IMobileAdvertisingService.CreateBannerAd(host)`
  once the startup flow completes; `CanServeAds` gates free entitlement + consent
  + SDK initialization. `MobileBannerAdControl.IsSuppressed` (driven by
  `UpdateNavigationMode`/overlay openers) hides a loaded ad while keeping it
  alive during overlays (profiles, category selection, player).

The Android provider embeds a Google `AdView` (fluid size) into the Avalonia
`NativeControlHost` inside the control and returns an `IDisposable` handle that
destroys the view on release. No-fill is handled by the native SDK (zero-height
view); the app never blocks on it.

## Premium

`IMobileAdvertisingService.CanServeAds` must be false for Premium users. The
banner and interstitials are gated through the same `CanServeAds` so a Premium
user never sees either surface. On Premium activation the Android provider
destroys preloaded interstitial objects; the banner collapses on the next
eligibility change.

## Interstitial playback-exit policy

`MainView` records only meaningful playback time (time while `IsPlaying` is true).
On player close it passes a deterministic `InterstitialAdContext` to the provider.

In-app policy (`AdvertisingOptions.ConservativeDefault` — not remotely
configurable):

- session age >= 5 minutes,
- meaningful playback >= 10 minutes,
- cooldown >= 18 minutes,
- max 2 impressions / rolling hour,
- max 4 impressions / rolling 24 hours,
- no Live content,
- no playback failure,
- no PiP session,
- no blocking overlay,
- no Premium,
- consent must permit requests,
- ad must already be ready.

The UI is hidden before the provider is called. A production provider must return
immediately when no preloaded ad is ready; it must never load synchronously while
the user is leaving the player.

`InterstitialAdPolicy` is pure and unit-tested. The provider owns consent/readiness
and impression history, evaluates this policy, records a successful impression,
then preloads the next ad.

Interstitial caps are fail-closed in `InterstitialAdPolicy` too:
`maxPerHour: 0` / `maxPerDay: 0` means **no interstitials at all**, never
"unlimited".

## Provider boundary

`IMobileAdvertisingService` is the only interface the Mobile UI knows:

- `CreateBannerAd(Control host)` — creates the banner, attaches it to the host,
  returns a disposable handle (null when no banner can be served).
- `PrimeInterstitial()` / `TryShowInterstitialAsync(InterstitialAdContext, ...)` —
  playback-exit preload/show.
- `ShowPrivacyOptionsAsync()` — UMP privacy-options form (GDPR/US state choices).
- `InitializeAsync()` — consent flow + Mobile Ads SDK initialization, invoked
  once at startup for entitled users.

Registered providers:

- `NoOpMobileAdvertisingService` by default (store-safe, fail-closed).
- `PreviewMobileAdvertisingService` in DEBUG when `NOCTRA_ADS_PREVIEW=1` —
  exercises entitlement gating without requesting real ads (banner/interstitial
  both return null/no-op).
- `AdMobMobileAdvertisingService` on Android (production).

## Startup pipeline (`MobileAdvertisingBootstrapper`)

Ad startup is provider-independent and runs fire-and-forget from
`MainActivity.OnCreate` (never on the UI thread):

1. **Entitlement** — providers expose `IsAdsEligible` (premium subscribers never
   see ads). A provider that cannot serve ads short-circuits the pipeline here.
2. **Consent + provider initialization** — `InitializeAsync()` only for entitled
   users: UMP consent, then Mobile Ads SDK initialization.

Eligibility changes (subscription expiry, consent state) are pushed through
`EligibilityChanged`, so the banner/interstitial surfaces re-evaluate
immediately instead of waiting for the next startup.

## Manual preview

Launch a Debug Android build with:

```powershell
$env:NOCTRA_ADS_PREVIEW = "1"
dotnet build .\Noctra.Android\Noctra.Android.csproj -c Debug
```

Validate:

1. Free entitlement shows the persistent banner above the bottom nav on every
   page; Premium hides it.
2. Opening the player hides the bottom nav and the banner; closing restores.
3. No banner area is reserved when the provider serves no ad.
4. Short playback / Live / PiP / playback failure never produces an eligible
   interstitial.