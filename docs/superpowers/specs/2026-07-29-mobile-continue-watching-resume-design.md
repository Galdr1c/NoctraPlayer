# Mobile Continue Watching Resume Design

## Scope

This change completes the missing mobile bridge between a Continue Watching card
selection and the existing player resume dialog. It covers VOD and series
episodes, premium gating, stale-selection cancellation, mobile back navigation,
and the player-local premium upsell.

Live playback remains unchanged and never offers resume.

## Resume Source of Truth

Mobile playback must not rely only on the `WatchedPosition`, `Duration`, and
`IsCompleted` values carried by a card. Those values are useful fallbacks but
may be stale after refresh or import.

The resolver therefore uses the same evidence order and safety rules as the
desktop player:

1. For a real series episode whose stream matches the selected channel, query
   episode watch history first and fall back to the episode fields.
2. For a virtual series Continue Watching channel, query channel history and
   fall back to the channel fields only when it represents the same stream.
3. For VOD, query channel watch history first and fall back to channel fields.
4. Require a real watch signal: a history row or a non-null `LastWatched`.

A resume offer is suppressed when:

- the content is live;
- no active profile exists;
- the position is at most 120 seconds;
- the item is marked completed;
- fewer than 30 seconds remain;
- at least 95 percent of the duration has been watched.

The shared decision rules live in a small Core resolver/policy so they can be
tested without constructing the Avalonia view.

## Playback Selection Flow

`MainView.PlaySelectedChannelAsync` starts a new playback intent immediately
after resolving `PlayerViewModel`, before showing the native video surface or
awaiting the resume dialog. This invalidates older dialogs, URL resolution,
pending seeks, and delayed playback paths.

The selected episode context is prepared before resume resolution. The player
host is then shown so the resume sheet can be displayed over the player.

For resumable VOD or episode content:

1. Resolve the saved position from watch history.
2. Await `ShowResumeDialogAsync`.
3. Verify that the original playback intent is still current.
4. Map the result to either the saved position or zero.
5. Call `PlayChannelAsync(channel, startPosition, playbackIntent)`.

If the dialog is cancelled, playback does not start. The player host is hidden,
the native video surface is hidden, and normal application chrome is restored.

If a newer content selection invalidates the intent, the older flow returns
without starting or overwriting the newer playback.

## Premium Rules

Starting from the beginning is available to every user.

Continuing from the saved position requires the `resume_playback` feature.
`ResumeFromPositionCommand` rechecks the license when tapped because the user
may have completed a purchase while the dialog remained open.

For a free user:

- the resume task remains pending;
- playback does not start;
- `PremiumUpsellRequested` is raised;
- the premium upsell opens above the resume sheet;
- closing the upsell reveals the still-active resume sheet.

For a premium user, the resume task completes with `true`.

## Resume Sheet

The two-column action row becomes a vertical hierarchy:

1. Primary accent button: Continue From Where I Left Off.
2. Secondary button: Start Over.
3. Tertiary transparent button: Back.

The continue button remains enabled for free users and displays a Premium badge
only when `IsPremiumResume` is false. This makes the restriction discoverable
and provides a direct path to the upsell.

The existing translation key `Player.Resume.Continue` is used for the primary
action. A new `Player.Resume.Back` key is added to every supported translation.

## Player-Local Upsell

`MobilePlayerView` hosts a `MobileUpsellView` above `MobilePlayerSheets` with a
higher Z-index. The view subscribes to `PremiumUpsellRequested` only while bound
to a `PlayerViewModel`, and removes the subscription when the data context
changes.

The upsell exposes a small close/back API so Android back navigation can dismiss
it without reaching into its visual children.

## Back and Scrim Priority

Back navigation follows this order:

1. Close the player premium upsell.
2. Cancel the resume dialog.
3. Dismiss the next-episode prompt.
4. Navigate from a child player sheet to its parent.
5. Close the remaining player sheet.
6. Close the player.

Cancelling the resume dialog completes its task through cancellation, never
through `false`, because `false` means Start Over.

The player sheet scrim uses the same cancellation path when the resume dialog is
visible.

## Failure Handling

- A cancelled resume dialog is an expected flow and does not show an error.
- A stale playback intent returns silently.
- History lookup failure falls back to the channel or episode state only when a
  real watch signal exists.
- A normal playback startup failure continues to use the existing user-friendly
  error path.
- Closing the player or selecting another item cancels any pending resume task.

## Testing

Automated tests cover:

- resume eligibility for VOD, real episodes, and virtual series cards;
- live, completed, short-progress, near-end, and stale-stream rejection;
- history precedence over card fields;
- premium resume completing with `true`;
- free resume raising upsell without completing the dialog task;
- license recheck after purchase;
- Start Over completing with `false`;
- Back cancelling the task;
- player back priority and player-local upsell subscription cleanup;
- mobile XAML action hierarchy, Premium badge, and translation keys;
- playback intent creation before dialog await and reuse by `PlayChannelAsync`.

The complete .NET test suite and Android build must pass. Physical Android
verification covers VOD and episode cards, all three resume actions, free-user
upsell/back behavior, rapid selection of two cards, and confirmation that live
channels never show the dialog.
