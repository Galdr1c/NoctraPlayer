# Mobile Player Timeline, Gesture and Buffer Design

## Scope

This change applies to the mobile player on the current `main` line, whose
latest reviewed commit is `c9434c1`. It improves VOD/movie/episode timeline
scrubbing, prevents accidental volume and brightness gestures around the
transport controls, and shows the native buffered range.

Live playback keeps its existing EPG-program progress behavior. Live streams
do not expose a seek thumb or buffered-range indicator.

## Interaction Design

The timeline remains visually thin while its transparent hit target becomes
40 device-independent pixels high. Pointer input anywhere inside that target
belongs to the timeline and cannot fall through to the full-screen gesture
zones.

For seekable content:

1. Pointer press starts a local seek preview and captures the pointer.
2. Pointer movement updates only the thumb, played bar and preview position.
3. Native playback is not seeked during pointer movement.
4. Pointer release commits exactly one seek.
5. Capture loss commits the latest valid preview once when a seek is active.

The full-screen left/right gesture zones remain responsible for brightness and
volume. A gesture becomes active only after at least 18 pixels of movement and
only when vertical movement is at least 1.35 times the horizontal movement.
Horizontal or ambiguous movement is rejected for that pointer sequence.
Sensitivity is reduced by scaling the vertical delta by 1.75.

When the bottom transport controls are visible, their actual arranged bounds
are excluded from brightness and volume gestures. This avoids relying on a
fixed-height approximation and remains correct with font scaling, safe areas
and future transport layout changes.

## Buffered-Range Data Flow

`IVideoPlayerService.BufferedPosition` represents seconds from the beginning
of the media. Implementations without native buffered-range support fall back
to the current playback position.

`AndroidVideoPlayerService` reads Media3/ExoPlayer `BufferedPosition` together
with `CurrentPosition` during the existing 500 ms position publication tick.
The value is clamped so it is never behind the played position or beyond a
known duration. It resets when a new item starts and whenever the playback
session is stopped or ended.

`PlayerPlaybackController` publishes the buffered value to
`PlayerViewModel.BufferedPosition` on the UI dispatcher. VOD values are
clamped to the known duration; live playback exposes zero. Player and content
reset paths also clear the value.

Android `IVideoPlayerService.Position` uses seconds, matching the interface,
desktop implementation and all PlayerViewModel callers. It must not expose or
accept a normalized 0–1 ratio.

## Visual Design

The track has three layers:

- Unplayed range: secondary overlay color at low opacity.
- Buffered range: accent purple at approximately 30 percent opacity.
- Played range: full accent purple.

The buffered layer is rendered behind the played layer and appears only when
it extends at least one pixel beyond played progress. The thumb remains 14
pixels and is hidden for live content.

## Error and Lifecycle Handling

No buffered value is allowed to be negative, behind played progress or beyond
a known media duration. A missing/unknown native duration does not block
position publication.

Detaching the timeline unsubscribes from the ViewModel and clears local drag
state. Releasing pointer capture cannot commit a seek twice. A completed or
rejected gesture suppresses the synthetic tap that Avalonia may raise after
pointer release, so transport visibility is not toggled accidentally.

Existing uncommitted `MobilePlayerTopOverlay.axaml` work is preserved and is
not part of this feature.

## Verification

Tests are written before production changes and must initially fail for the
missing behavior. Coverage includes:

- Gesture activation threshold and vertical-intent classification.
- Rejection of horizontal movement.
- Buffer clamping and reset behavior in PlayerViewModel/controller flow.
- Android playback position being measured in seconds.
- Timeline input contract: enlarged protected hit area, preview-only movement,
  one release commit, and buffered visual layer.

After implementation:

1. Run the targeted player and timeline tests.
2. Run the complete `Noctra.Tests` suite.
3. Build `Noctra.Mobile` and `Noctra.Android`.
4. On the connected Android device, verify fine scrubbing, no accidental
   volume/brightness changes near controls, one seek per drag, buffer rendering,
   live timeline behavior, pause/resume and PiP return.
