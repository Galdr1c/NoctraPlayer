# Next Episode Countdown and Cancellation Implementation Plan

## Objective

Implement the approved eight-second next-episode countdown, direct `Watch Now`, per-episode `Cancel`, rewind reset/re-entry restart, and the existing profile-scoped autoplay preference without introducing duplicate transitions or stale timer callbacks.

## Constraints

- Work only in `D:\IPTVPlayer`.
- Reuse `AppSettings.AutoPlayNext` and the existing mobile Settings binding.
- Keep countdown ownership out of mobile code-behind.
- Preserve direct next-episode navigation and downloaded-playback checks.
- Do not stage or commit.
- The user has explicitly waived pre-change RED execution; add regression coverage and verify after implementation.

## Step 1: Add countdown and cancellation state

Files:

- `Noctra.Core/ViewModels/PlayerViewModel.cs`
- `Noctra.Core/ViewModels/Player/PlayerEpisodeNavigator.cs`

Changes:

- Add bindable countdown seconds and active state.
- Add a localized countdown text property.
- Add `CancelNextEpisodeCommand`.
- Give `PlayerEpisodeNavigator` ownership of the countdown cancellation token, episode generation, explicit-cancel latch, and atomic transition guard.
- Add an internally callable countdown tick operation used by both the real timer and deterministic tests.

Acceptance:

- Prompt starts at eight when autoplay is enabled.
- No timer starts when autoplay is disabled.

## Step 2: Implement lifecycle and transition rules

File:

- `Noctra.Core/ViewModels/Player/PlayerEpisodeNavigator.cs`

Changes:

- Reset countdown and cancellation state when a new episode is assigned.
- Start the timer when the credits prompt first opens.
- On rewind outside credits, hide the prompt and reset the timer without setting explicit cancellation.
- On credits re-entry, reopen and restart from eight.
- On explicit cancellation, hide and latch cancellation for the current episode.
- Route countdown completion and playback-ended transition through one atomic claim.
- Keep `Watch Now` immediate.
- Cancel timers during content transition, playback exit, and disposal.

Acceptance:

- Repeated credits/end events cannot open the next episode twice.
- Stale callbacks cannot affect a new episode.

## Step 3: Update the mobile next-episode panel

File:

- `Noctra.Mobile/Views/MobilePlayerView.axaml`
- `Noctra.Mobile/Views/MobilePlayerSheets.axaml`

Changes:

- Bind a countdown message visible only while active.
- Add a responsive two-button row.
- Keep `Watch Now` as the primary action.
- Add `Cancel` as a secondary action bound to `CancelNextEpisodeCommand`.
- Remove the next-episode surface from the bottom-sheet stack.
- Place a compact card right-aligned immediately above the timeline.
- Keep the timeline and transport controls visible; do not show a scrim.

Acceptance:

- Enabled autoplay shows name, countdown, `Watch Now`, and `Cancel`.
- Disabled autoplay shows no countdown and remains manually actionable.

## Step 4: Add localization

Files:

- `Noctra.Core/Localization/Translations/de-DE.json`
- `Noctra.Core/Localization/Translations/en-US.json`
- `Noctra.Core/Localization/Translations/es-ES.json`
- `Noctra.Core/Localization/Translations/fr-FR.json`
- `Noctra.Core/Localization/Translations/tr-TR.json`

Keys:

- countdown format
- cancel action

Acceptance:

- Every supported locale contains the same keys.
- Language changes refresh the countdown text.

## Step 5: Add regression coverage

Files:

- `Noctra.Tests/PlayerViewModelControlsTests.cs`
- `Noctra.Tests/MobileRecentRegressionTests.cs`

Behavior tests:

- starts at eight when enabled
- deterministic tick progression and exactly-once completion
- direct `Watch Now`
- explicit cancel blocks timer and playback-end transitions
- rewind hides/resets without latching cancellation
- credits re-entry restarts from eight
- new episode resets explicit cancellation
- disabled autoplay does not start or transition
- stale generation is ignored
- disposal/content reset cancels countdown

UI/source tests:

- mobile panel contains countdown and both actions
- setting remains bound to `AutoPlayNext`
- all localization keys exist

## Step 6: Verify

Commands:

```powershell
dotnet test .\Noctra.Tests\Noctra.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~PlayerViewModelControlsTests|FullyQualifiedName~MobileRecentRegressionTests" -p:UseSharedCompilation=false --disable-build-servers
dotnet build .\Noctra.Mobile\Noctra.Mobile.csproj -c Debug --no-restore -p:UseSharedCompilation=false --disable-build-servers
dotnet test .\Noctra.Tests\Noctra.Tests.csproj -c Debug --no-restore -p:UseSharedCompilation=false --disable-build-servers
git diff --check
```

Physical/HotAvalonia verification:

- autoplay on: countdown from eight and automatic transition
- `Watch Now`: immediate transition
- cancel: no transition at zero or natural end
- rewind: prompt hides and resets
- re-entry: prompt returns at eight
- autoplay off: no countdown and no automatic transition
