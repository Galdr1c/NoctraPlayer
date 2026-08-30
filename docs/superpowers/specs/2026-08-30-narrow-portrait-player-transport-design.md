# Narrow portrait player transport design

## Problem

On a 1080×2400, 420 dpi phone (about 411 logical pixels wide), the VOD
transport row does not fit. Four 48-logical-pixel transport targets, the current and
total time labels, the 48-logical-pixel Tune action, margins, and column spacing need
more width than the roughly 379 pixels available inside the control. The star
column cannot become negative, so the rightmost Tune button is clipped.

Reducing only the 24-pixel glyphs would not solve the overflow because the
buttons would still reserve their 48-pixel accessible touch targets.

## Design

- Use the transport control's measured logical width as the responsive source
  of truth. Widths at or below 420 logical pixels use the narrow layout; wider
  portrait, landscape, tablet, and desktop-sized hosts retain the current
  layout.
- Preserve the existing 48×48 logical-pixel button targets and the 24-pixel
  action glyphs; Play/Pause keeps its existing 28-pixel glyph.
- In the narrow VOD layout, move playback timing out of the button row into a slim
  row directly below the timeline and above the transport buttons:
  - current position is left aligned;
  - total duration is right aligned;
  - the decorative middle dot is omitted;
  - the existing typography and muted-duration hierarchy are preserved.
- The narrow VOD button row is Back 10, Play/Pause, Forward 10, Mute, flexible
  space, and Tune. Tune remains pinned to the right edge and is always fully
  visible.
- The narrow live row is Previous channel, Play/Pause, Next channel, Mute,
  flexible space, EPG, and Tune. It uses the same target and glyph sizes and
  does not introduce a timing row, matching the current live behavior.
- Apply a local responsive class from `MobilePlayerTransportBar` when its bounds
  cross the threshold. XAML styles switch the wide and narrow timing hosts;
  playback commands and ViewModel state do not change.
- Re-evaluate the class on initial attach and every meaningful width change so
  rotation and split-screen resizing update the layout without recreating the
  player.

## Accessibility and visual rules

- Do not shrink or overlap touch targets.
- Do not introduce horizontal scrolling or hide Tune in an overflow menu.
- Keep the current outer margins, icon language, colors, focus visuals, and
  pressed feedback.
- The layout change is width-driven rather than tied to a specific device name,
  DPI, or physical screen size.

## Deliberately unchanged

- Timeline behavior, seek gestures, playback commands, action-sheet contents,
  live/VOD visibility rules, and landscape layout remain unchanged.
- No icon asset or localization change is required.

## Verification

- Add a regression test that asserts the 420-pixel compact threshold and its
  boundary behavior.
- Add a XAML contract test proving that narrow timing has separate left/right
  labels, Tune remains in the right-aligned action group, and button targets
  remain 48×48.
- Run the focused regression tests and the complete `Noctra.Tests` suite.
- Build the Android project and verify on the 1080×2400 phone profile in both
  portrait and landscape, for VOD and live playback.
- Confirm large system font scaling does not clip Tune or overlap the time
  labels.
- No commit, push, AAB publication, or Play Console update is part of this task.
