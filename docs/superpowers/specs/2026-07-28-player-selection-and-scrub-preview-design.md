# Mobile Player Selection State and Scrub Preview Design

## Scope

This change adds two focused mobile-player improvements:

1. Visible selected-state feedback for audio tracks, subtitle tracks, and playback speed.
2. A lightweight bottom-time preview while dragging the timeline for VOD and series episodes.

Live playback remains non-seekable and receives no scrub preview. Thumbnail preview is explicitly outside this scope.

## Selected Option Appearance

Audio, subtitle, and playback-speed options will use the same selected appearance as `MobileSelectionSheet`:

- `AccentSubtleBrush` selected surface.
- `RadioboxMarked` icon using `AccentBrush`.
- Transparent surface and no selection icon when not selected.
- Existing button press feedback remains intact.

The player panels remain in their current sheet navigation structure. They will not be replaced by a nested `MobileSelectionSheet`, because that would change closing, back-navigation, and panel-transition behavior.

### Audio and Subtitle State

`SelectedAudioTrack` and `SelectedSubtitleTrack` remain the source of truth. Each option compares its `Id` with the corresponding selected identifier through a reusable equality `MultiBinding`.

The special subtitle-off item uses its existing `Id = -1`; it therefore receives exactly the same selected treatment when subtitles are disabled.

Track collections are not rebuilt when selection changes. Changing the selected identifier only reevaluates the visible selection bindings.

### Playback Speed State

`PlayerViewModel` gains an observable `CurrentPlaybackRate` property with a default value of `1.0f`.

`SetPlaybackSpeedCommand` will:

1. Normalize the requested rate to one of the supported values.
2. Assign the normalized rate to the player service.
3. Update `CurrentPlaybackRate`.
4. Preserve the existing auto-hide timer behavior.

The six existing rates remain unchanged:

- 0.5x
- 0.75x
- 1.0x
- 1.25x
- 1.5x
- 2.0x

The selection comparison must be numeric and culture-independent so that `1`, `1.0`, and locale-specific decimal formatting cannot produce a false mismatch.

`CurrentPlaybackRate` is reset to `1.0f` when player state is prepared for new content or cleared after exit, keeping UI state synchronized with the service default.

## Timeline Scrub Preview

The timeline owns the temporary drag position while `PlayerViewModel` exposes only the formatted preview text required by the existing transport-bar time label.

While a seekable VOD or episode timeline is being dragged:

- The existing bottom time label immediately shows the preview position.
- The duration label remains unchanged beside it.
- No second floating bubble duplicates the same information.
- The controls remain visible for the complete pointer-capture lifetime, even when the finger leaves the timeline bounds.

For example, the existing transport row shows `00:24:18 / 00:48:12` during the drag.

The current seek behavior is preserved:

- Pointer movement updates only the visual preview.
- The native player receives one seek when the pointer is released.
- Pointer capture loss commits the latest valid preview once.
- The normal playback time is restored after release, capture loss, detachment, invalid duration, or content change.
- Auto-hide is suppressed while the seek interaction is active and restarted after commit.

Live content continues to show only its existing EPG progress. It cannot start timeline dragging and never activates preview state.

## Layout and Styling

The visual track remains four DIPs high, the current forty-DIP touch target remains unchanged, and the thumb retains its current size. No additional overlay surface is introduced.

## Accessibility

Selected audio, subtitle, and playback-speed options expose their selected state through Avalonia selection semantics where supported, in addition to the visible radio marker.

The timeline keeps its existing natural-language accessible progress name. During drag, the accessible value may follow the preview position, but it must not announce on every pointer-move event. No live timeshift semantics are introduced.

## Failure and Edge Handling

- Unknown track identifiers show no false selection.
- An unavailable playback rate falls back to `1.0x`.
- Zero, unknown, `NaN`, or infinite duration suppresses scrub preview.
- Preview positions are clamped to `[0, Duration]`.
- Empty track lists preserve the current empty-panel behavior.
- Selection commands continue to call the existing player-service methods; failure behavior is unchanged.

## Testing

Automated coverage will verify:

- Audio selection uses `SelectedAudioTrack`.
- Subtitle selection, including `Id = -1`, uses `SelectedSubtitleTrack`.
- Player track and speed options include the same accent surface and marked-radio treatment as `MobileSelectionSheet`.
- `CurrentPlaybackRate` defaults to `1.0f`, changes with the command, and resets for new content.
- Numeric selection comparison is culture-independent.
- The bottom time preview activates only while dragging seekable non-live content.
- Preview position clamps to valid bounds.
- Native seek remains a single commit on release.
- Auto-hide cannot hide controls while seeking is active.
- Live content cannot activate the scrub preview.

The complete test suite and Android build must pass. A physical Android check will cover touch dragging beyond timeline bounds, edge clamping, selected indicators, and the absence of a duplicate floating bubble.
