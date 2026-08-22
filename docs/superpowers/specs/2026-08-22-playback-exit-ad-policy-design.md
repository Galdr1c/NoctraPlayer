# Playback Exit Ad Policy and Resume Recovery Design

## Problem

Playback-exit interstitial support exists in both the GMS/AdMob and HMS/Petal
providers, but neither provider evaluates the existing `InterstitialAdPolicy`.
Consequently a ready ad can be presented on short playback and Live exits even
though the documented policy forbids it.

On the Huawei test device, a Live exit opened a Petal test interstitial after
roughly 16 seconds. After dismissal, `FilteredChannels` remained populated but
the `MobileVirtualizingCardGrid`'s `VirtualizingStackPanel` had no realized rows.
Changing destination rebuilt the page and restored the cards. The provider task
currently completes on the ad-open callback, so `MainView` has no deterministic
post-dismissal point at which to repair the resumed grid.

## Selected Design

### One policy for both providers

Introduce a shared policy coordinator in Core. Both Android providers must ask
this coordinator before presenting a ready interstitial and must record an
impression only after the SDK confirms that the ad opened.

The coordinator consumes the existing pure `InterstitialAdPolicy` and a small
history-store interface. Android supplies a SharedPreferences-backed store so
the cooldown and rolling caps survive process restarts and provider selection
cannot produce separate counters.

The existing conservative options remain authoritative:

- application session age at least 5 minutes;
- meaningful playing time at least 10 minutes;
- 18-minute cooldown;
- at most 2 impressions in a rolling hour;
- at most 4 impressions in rolling 24 hours;
- no Live content;
- no downloaded/local playback;
- no failed playback, PiP session, blocking overlay, Premium account, missing
  consent, or missing preloaded ad.

History is pruned to the rolling 24-hour window. Malformed individual entries
are ignored. If the store itself cannot be read or a new impression cannot be
persisted, the coordinator denies later interstitials for that process while
allowing playback exit to continue normally.

### Downloaded playback classification

Extend `InterstitialAdContext` with `IsDownloadedContent` and add a dedicated
policy denial reason. `MainView` latches `PlayerViewModel.IsDownloadedPlayback`
for the active playback session before the player resets its current channel on
exit. A session that contains downloaded playback is never eligible. Live and
downloaded sessions also skip interstitial preloading to avoid needless network
and SDK work.

### Full-screen ad completion and UI recovery

For both AdMob and Petal, `TryShowInterstitialAsync` completes only when the ad
is dismissed or fails to show. The ad-open callback records one impression but
does not complete the task.

When a shown ad is dismissed, `MainView` schedules a loaded-priority recovery
for the still-active core page. It restarts page-state restoration, re-enables
image loads, and calls `RefreshAfterResume` on descendant
`MobileVirtualizingCardGrid` controls. This rebuilds the row projection from the
still-populated source collection without re-querying or clearing content.
The existing Android lifecycle recovery remains as a general background/return
guard; the new recovery is the deterministic interstitial boundary.

## Error Handling

- Policy denial returns immediately and leaves navigation unaffected.
- Missing history is treated as empty and malformed entries are ignored. A
  store read/write failure denies later ads but never throws through player
  close.
- A failed SDK presentation records no impression and still triggers no content
  mutation.
- The one-presentation-at-a-time guard in `MainView` remains in place.

## Verification

- Unit tests cover downloaded-content denial, rolling persisted history,
  pruning, and cross-instance cooldown/caps.
- Contract tests require both AdMob and Petal providers to evaluate the shared
  coordinator and complete only on dismissal.
- A mobile regression test requires post-interstitial grid recovery.
- Run focused advertising/mobile tests, the complete test suite, Android Debug
  build, and a Huawei smoke test that confirms Live/short/downloaded exits do
  not show an interstitial and a qualifying VOD dismissal leaves cards visible.
