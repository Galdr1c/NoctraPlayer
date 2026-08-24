# Player Lifecycle Resume Recovery Design

## Problem

Free-tier Android playback is intentionally paused when the app leaves the
foreground. After Huawei hibernates the process for a longer background stay,
ExoPlayer can still report that media is loaded while its playback pipeline no
longer reacts to `Resume()`. The shared play/pause controller treats
`HasLoadedMedia` as sufficient, calls `Resume()`, and returns without verifying
that playback actually started. The UI remains paused; seeking still changes
position because the loaded player object is still present.

The issue affects live streams, streamed VOD/series episodes, and downloaded
content because all of them use the same resume fast paths.

## Decision

Keep the existing background-pause and premium PiP behavior unchanged. Add a
single verified-resume path in `PlayerPlaybackController`:

1. Call `IVideoPlayerService.Resume()` for loaded media.
2. Wait for a short bounded interval, checking the backend's authoritative
   `IsPlaying` value and the current playback request identity.
3. If playback starts, return without reopening media.
4. If it remains paused, perform one recovery play:
   - live content: stop the stale pipeline and open the stream at the live edge;
   - VOD, series, and downloaded content: reopen the stream at the best known
     current position.

The existing play/pause in-progress guard prevents duplicate recovery attempts.

## Position Selection

For non-live recovery, choose the greatest valid value from:

- backend `CurrentTimeMilliseconds`;
- the view model's current `Position`;
- the last explicit paused position;
- the last known valid playback position.

This preserves the user's place even when lifecycle pause did not run the
manual-pause position capture path.

## State and Error Handling

- Backend `IsPlaying`, not a potentially delayed UI snapshot, determines
  whether resume succeeded.
- Every delayed step rechecks the playback request/channel identity so a stale
  recovery cannot reopen content after the user changes channel or closes the
  player.
- Recovery is attempted once. Normal resume remains the fast path.
- Existing `PlayAsync` error propagation and player error UI remain unchanged.

## Regression Coverage

- A loaded non-live player that ignores `Resume()` must reopen at its current
  position.
- A loaded live player that ignores `Resume()` must reopen at the live edge.
- A loaded player whose normal `Resume()` succeeds must not be reopened.
- Existing Android lifecycle contracts must continue to enforce background
  pause outside PiP.

## Scope

No background playback, premium/PiP eligibility, seek behavior, content
selection, database, or ad behavior changes are included.
