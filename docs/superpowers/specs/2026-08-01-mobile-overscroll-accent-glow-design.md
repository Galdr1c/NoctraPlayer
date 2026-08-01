# Mobile Overscroll Accent Glow Design

## Goal

Make the existing stretch overscroll easier to perceive without restoring the old full-screen edge overlay or introducing a second animation system.

## Chosen Design

The content stretch remains the primary feedback. A subtle accent-colored glow is drawn only inside the active `ScrollViewer` viewport and only along the edge being pulled.

Both visuals are driven by the same normalized overscroll state:

- Pull distance determines content translation and vertical scale.
- The resulting resisted translation determines glow height and opacity.
- The existing frame-time spring `remaining` value drives content translation, content scale, glow height, and glow opacity during release.
- There is no timer, transition, or independent fade animation for the glow.

The glow is decorative, non-focusable, and non-hit-testable. It cannot capture taps, long presses, nested scrolling, sliders, or card actions.

## Visual Parameters

- Brush: current dynamic `AccentBrush`, with the existing fallback accent color.
- Maximum opacity: `0.22`.
- Maximum edge depth: `24` device-independent pixels.
- Shape: a soft linear gradient that is strongest at the active edge and fades to transparent toward the content.
- Top pull: gradient starts at the viewport top.
- Bottom pull: mirrored gradient ends at the viewport bottom.
- Zero pull or completed release: glow is hidden and occupies no visible area.

These values keep the effect subordinate to the physical stretch and avoid tinting cards or text.

## Architecture

`MobileStretchOverscrollController` owns one reusable, host-level, hit-test-transparent feedback canvas with top and bottom glow borders. The canvas spans the host only as a drawing surface; each active glow is positioned and sized to the exact bounds of the active `ScrollViewer` translated into host coordinates.

The controller updates the glow from `ApplyVisualPull()` and from every spring-release frame. It hides and resets the glow when the gesture is cancelled, the controller is hidden or disposed, the visual tree changes, or the session ends.

The feedback layer remains below modal shell overlays and player surfaces so it cannot tint sheets, dialogs, toasts, or video controls.

## Alternatives Considered

1. Apply a drop-shadow/effect to the transformed content. Rejected because it would affect all four edges, can be expensive on large virtualized lists, and risks replacing an existing effect binding.
2. Restore the old independent accent overlay and fade timer. Rejected because its timing can diverge from the content spring and it previously behaved like a global overlay.
3. Use a per-viewer host overlay driven by the existing physics. Chosen because it preserves nested-scroll targeting and gives one synchronized motion source.

## Interaction and Recovery

- Stationary long press still opens the card action sheet.
- Once overscroll claims the gesture, the pending card long press remains cancelled.
- Glow never participates in hit testing.
- Existing pointer capture, nested scroll, axis lock, reverse-motion consumption, and excluded-control behavior remain unchanged.
- Existing render transforms and bindings continue to be restored exactly as before.

## Tests

- A physics test verifies glow intensity is monotonic, clamped, and zero at rest.
- A source/architecture test verifies the glow uses the existing spring progress and is non-hit-testable.
- Existing overscroll physics, card long-press cancellation, navigation behavior, and mobile build tests must remain green.
- Android verification covers top and bottom pulls over card grids and confirms no card sheet opens during a pull.
