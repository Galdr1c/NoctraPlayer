# Mobile Player Accessibility Semantics Design

## Goal

Make Noctra's custom mobile player controls understandable and operable with
Android TalkBack. Every interactive control in the main player, top overlay,
transport bar, lock affordance, and timeline must expose a concise,
localized accessible name. Controls whose action changes with state must
announce the action that will happen when activated.

## Scope

This change covers:

- the player close, picture-in-picture, and lock buttons;
- transport controls for play/pause, mute/unmute, ten-second seek, previous
  and next live channel, return to live, live favorite, EPG, and More;
- the temporary locked-player unlock affordance;
- the VOD/series timeline and live program progress.

Player detail sheets such as Audio, Quality, Episodes, and More are outside
this focused change. They retain their existing visible text and will be
audited separately if TalkBack testing finds missing semantics.

## Semantics

### Static actions

Static actions use localized `AutomationProperties.Name` values:

- close player;
- enter picture-in-picture;
- seek backward ten seconds;
- seek forward ten seconds;
- play previous live channel;
- play next live channel;
- open the program guide;
- open more player actions;
- return to the live edge.

Tooltips may remain for pointer users, but are not treated as accessibility
names.

### Dynamic actions

Computed properties on `PlayerViewModel` provide localized action names:

- playing: "Pause video"; paused: "Play video";
- audible: "Mute"; muted: "Unmute";
- not favorite: "Add to favorites"; favorite: "Remove from favorites";
- unlocked: "Lock player"; locked: "Unlock player".

The corresponding state-change callbacks raise property-change notifications
for these accessibility properties. A language change also raises
notifications so TalkBack receives names in the newly selected language
without recreating the player.

### Timeline

The timeline receives accessibility focus and exposes one localized,
human-readable name.

For finite media:

`Progress, 18 minutes 32 seconds / 42 minutes`

For live media with EPG progress:

`Live program progress, 63 percent`

When a current program title is available, it is included without duplicating
the visible channel title. Unknown duration is announced as an unavailable
duration rather than as zero.

Timeline accessibility text is refreshed when position, duration, live
progress, live/VOD mode, current program, or application language changes.
Visual progress and touch behavior remain unchanged.

## Localization

New accessibility strings are added to the existing Turkish, English, German,
French, and Spanish translation JSON files. Sentence assembly and time-unit
pluralization stay in `PlayerViewModel`, backed by localized format strings
and unit strings. No TalkBack-visible action name is hard-coded in XAML.

## Implementation Boundaries

- `PlayerViewModel` owns state-derived and localized accessibility text.
- XAML binds `AutomationProperties.Name` to those properties or to existing
  localized static strings.
- `MobilePlayerTimeline` remains the visual/touch control; its root exposes
  the computed timeline text and focusability.
- No Android-specific accessibility service or custom automation peer is
  introduced unless physical testing proves Avalonia does not forward the
  attached automation properties.

## Testing

Automated tests are written before production changes:

1. ViewModel tests verify play/pause, mute/unmute, favorite, and lock action
   names switch with state.
2. Time-format tests cover seconds, minutes, hours, unknown duration, and live
   progress.
3. Static mobile tests verify every scoped button and timeline has an
   `AutomationProperties.Name`.
4. Language-change tests verify computed names are invalidated.
5. The complete existing test suite and Android build must pass.

Physical verification on the connected Android tablet uses `uiautomator`:

- every scoped control has a non-empty `content-desc`;
- play/pause, mute/unmute, favorite, and lock descriptions change after
  activation;
- the timeline exposes the current human-readable progress;
- accessible names do not replace or suppress neighboring controls.

## Acceptance Criteria

- TalkBack does not announce an unlabeled button in the scoped player UI.
- Dynamic controls announce their next action, not a stale state.
- Timeline progress is understandable without looking at the screen.
- Switching application language refreshes accessible player text.
- Playback, seeking, gestures, PiP, locking, and visual layout are unchanged.
