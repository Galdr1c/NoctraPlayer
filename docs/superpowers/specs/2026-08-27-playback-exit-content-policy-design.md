# Playback-exit interstitial content policy

## Goal

Allow the playback-exit interstitial placement for live playback as well as
streamed VOD/series playback, without showing ads for downloaded content or
Premium users.

## Decisions

- `AllowLiveContent` is enabled in the shared conservative policy.
- Live playback is preloaded through the same interstitial provider path as
  streamed VOD, so an ad can be ready when the player closes.
- If consent/SDK initialization finishes after playback has already started,
  the active non-downloaded playback is re-primed when ad eligibility changes.
- Downloaded playback remains excluded from both preloading and presentation.
- The existing safeguards remain unchanged: consent, SDK/ad readiness,
  established playback, playback-error and PiP vetoes, five-minute session age,
  ten minutes of actual playback, 18-minute cooldown, two impressions per hour,
  and four impressions per day.
- Series episodes continue to use the streamed VOD path. Short episodes may
  still be denied by the ten-minute actual-playback threshold.
- The same shared policy applies to AdMob and Huawei Petal Ads providers.

## Data flow

1. Playback starts and the content/download flags are captured.
2. An interstitial is preloaded for any non-downloaded playback, including
   live playback.
3. On player close, the accumulated playing time and content flags are
   evaluated by `InterstitialAdPolicyCoordinator`.
4. A loaded ad is shown only when every policy gate passes; successful
   impressions are recorded for the shared cooldown and rolling caps.

## Verification

- A live context is allowed after the normal eligibility thresholds.
- A live context is still denied when a provider explicitly disables live ads.
- The mobile MainView preloads playback-exit ads for live and VOD playback but
  not downloaded playback.
- Existing Premium, consent, readiness, failure, PiP, cooldown, cap, and
  downloaded-content tests remain green.
