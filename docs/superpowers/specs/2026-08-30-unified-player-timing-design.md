# Unified player timing layout design

## Scope change

The previously approved narrow-portrait layout moved VOD timing below the
timeline only when the transport bar was at most 420 logical pixels wide. The
same presentation is clearer and more stable in every orientation, so the
timing row is now shared by all VOD layouts.

## Design

- Keep one VOD timing row directly below the timeline and above the transport
  buttons in portrait, landscape, split-screen, and tablet-sized hosts.
- Align the current position to the left and total duration to the right. Keep
  the existing typography and muted-duration hierarchy; omit the decorative
  middle dot.
- Keep the transport row dedicated to actions. VOD uses Back 10, Play/Pause,
  Forward 10, Mute, flexible space, and Tune. Live keeps Previous, Play/Pause,
  Next, Mute, flexible space, EPG, and Tune, with no timing row.
- Remove the width threshold and `IsNarrowLayout` state because no orientation
  switch is needed. The ViewModel commands and live/VOD visibility bindings
  remain unchanged.
- Keep 48×48 logical-pixel touch targets and 24/28-pixel glyphs. Tune remains
  the rightmost action and must never be clipped.

## Deliberately unchanged

- Timeline/seek behavior, playback state, action sheets, safe-area handling,
  colors, focus/pressed states, localization, and native video surfaces are
  unchanged.
- No horizontal scrolling, icon shrinking, or device-specific branching is
  introduced.

## Verification

- Update the transport layout contract tests to reject orientation-specific
  threshold state and require one shared VOD timing row.
- Run the focused and complete `Noctra.Tests` suites plus Mobile and Android
  builds.
- Verify VOD and live controls in portrait and landscape on the 1080×2400
  emulator, including a larger system font scale.
- No commit, push, AAB publication, or Play Console update is part of this
  change.
