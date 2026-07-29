# Next Episode Countdown and Cancellation Design

## Goal

Complete the series next-episode experience with an eight-second countdown, an explicit cancellation action, and the existing profile-scoped autoplay preference.

The design must guarantee that a cancelled transition cannot be restarted by another credits-position or playback-ended event during the same episode.

## Existing Behavior

- `AppSettings.AutoPlayNext` already exists, defaults to `true`, and is persisted per profile.
- `MobileSettingsView` already exposes the setting through `Settings.Playback.AutoPlayNext`.
- `PlayerEpisodeNavigator` discovers the next episode and opens the prompt in the credits zone.
- `TryShowNextEpisodePromptAtEnd` currently plays the next episode immediately when autoplay is enabled.
- The mobile prompt currently contains only the next episode name and a `Watch Now` action.

The existing persisted setting will be reused. No duplicate setting or storage field will be introduced.

## User Experience

### Autoplay enabled

When the next-episode prompt first opens:

1. The prompt shows the next episode name.
2. An eight-second countdown begins.
3. The countdown text updates once per second:
   - “Next episode will start in 8 seconds”
   - …
   - “Next episode will start in 1 second”
4. When the countdown completes, the next episode opens immediately.
5. If playback ends before the countdown completes, the next episode opens immediately.

The prompt exposes two actions:

- **Watch Now:** immediately opens the next episode.
- **Cancel:** closes the prompt and suppresses every automatic transition for the current episode.

### Autoplay disabled

- The next-episode prompt may still appear so the user can choose `Watch Now`.
- No countdown starts and no countdown message is shown.
- Playback ending does not open the next episode.
- `Cancel` dismisses the prompt for the current episode.

### Cancellation scope

Cancellation is scoped to the current episode playback session:

- Rewinding outside the credits zone while the countdown is active hides the prompt and resets the countdown.
- Re-entering the credits zone opens the prompt again and, when autoplay is enabled, starts a fresh eight-second countdown.
- Rewinding does not set the explicit cancellation latch.
- Pressing `Cancel` does set the cancellation latch, so later credits-position or playback-ended events cannot reopen or auto-transition during the same episode.
- Repeated playback-ended events do not bypass cancellation.
- Loading another episode resets cancellation and permits the next episode’s normal behavior.
- `Watch Now` remains available as a direct user action before cancellation; after the prompt is dismissed the user can use the existing episode UI to navigate manually.

## State and Ownership

`PlayerEpisodeNavigator` owns countdown lifecycle because it already owns next-episode discovery, credits triggering, playback-ended behavior, and transition execution.

`PlayerViewModel` exposes bindable state:

- `NextEpisodeCountdownSeconds`
- `IsNextEpisodeCountdownActive`
- a localized countdown display value
- `CancelNextEpisodeCommand`

Navigator-private state provides:

- a cancellable countdown token/source
- a current-session cancellation latch
- a single-transition guard
- a generation/session identity so stale timer completions cannot affect a newly loaded episode

No timer is owned by the mobile view or sheet code-behind.

## State Transitions

### Episode loaded

`SetCurrentEpisode`:

- cancels and disposes any old countdown
- resets the per-episode cancellation latch
- resets the single-transition guard
- clears countdown state

### Prompt requested

When the credits trigger requests the prompt:

- return without reopening when the current episode was cancelled
- show the prompt
- start the countdown only when `AutoPlayNext` is enabled
- ignore duplicate trigger events while the same countdown is active

### Rewind outside credits

- cancel the active timer without setting the cancellation latch
- clear the countdown state
- hide the prompt
- permit a fresh prompt and eight-second countdown when playback enters the credits zone again

### Watch Now

- atomically claim the single transition
- cancel the timer
- clear the prompt/countdown state
- execute the existing next-episode request immediately

### Cancel

- cancel the timer
- latch cancellation for the current episode
- clear the prompt/countdown state
- remain on the current episode through natural playback end

### Playback ended

- do nothing when autoplay is disabled
- do nothing when the current episode was cancelled
- otherwise claim the single transition and open the next episode

### Countdown completion

- verify the generation still belongs to the current episode
- verify autoplay remains enabled
- verify the current episode was not cancelled
- atomically claim the single transition
- open the next episode

## Concurrency and Lifecycle Safety

- Countdown cancellation is idempotent.
- Countdown completion and playback-ended events share the same atomic transition guard.
- Stale timer callbacks cannot transition after another episode has loaded.
- Closing the player, changing content, or resetting player state cancels the countdown.
- Property updates are dispatched through the existing player dispatcher path.
- Timer cancellation is treated as normal control flow and does not surface an error.

## Mobile UI

The next-episode surface is a compact player overlay in `MobilePlayerView`, aligned to the right immediately above the timeline. It is not part of `MobilePlayerSheets`, does not open a scrim, and leaves the timeline and transport controls visible.

The overlay contains:

1. section label
2. next episode name
3. countdown text, visible only while countdown is active
4. a responsive two-action row:
   - primary `Watch Now`
   - secondary `Cancel`

The card has a bounded responsive width and equal-width actions, without creating horizontal scrolling.

All visible strings use localization keys in:

- `de-DE`
- `en-US`
- `es-ES`
- `fr-FR`
- `tr-TR`

## Settings

The existing `AutoPlayNext` toggle remains in the Playback settings panel:

- it stays profile-scoped
- it continues to autosave through `SettingsViewModel`
- no premium restriction is added
- turning it off means no countdown and no automatic end transition

The UI wording remains “Automatically play next episode” or the locale-equivalent translation.

## Testing

Automated coverage must verify:

1. autoplay enabled starts at eight seconds
2. countdown values decrease and completion transitions once
3. playback ending during countdown transitions once
4. `Watch Now` transitions immediately
5. `Cancel` prevents countdown completion from transitioning
6. `Cancel` prevents playback-ended transition
7. rewinding outside credits hides the prompt and clears the active countdown
8. re-entering credits after a rewind opens the prompt and restarts from eight seconds
9. re-entering credits after an explicit cancellation does not reopen or restart the countdown
10. loading a new episode resets cancellation
11. autoplay disabled never starts the countdown
12. autoplay disabled never transitions on playback end
13. stale countdown completion cannot affect a new episode
14. mobile XAML includes countdown, `Watch Now`, and `Cancel`
15. all new localization keys exist in every supported locale
16. the existing settings toggle remains bound to `AutoPlayNext`

Physical Android verification should cover both enabled and disabled settings, early cancellation, natural playback end, and direct `Watch Now`.

## Out of Scope

- Thumbnail previews
- User-configurable countdown duration
- Per-series autoplay preferences
- Server-side autoplay synchronization
- Desktop UI redesign beyond preserving existing settings behavior
