# Mobile Watermark Stable Position Design

## Verified Context

`MainView` sets `PlayerViewModel.IsFullScreen = true` whenever the mobile
player opens. `MobilePlayerView.ApplyWatermarkInsets` therefore selects the
fullscreen branch for normal mobile playback, making the normal-controls and
detail-panel bottom constants ineffective. The current `120/180/560` values
also make the watermark jump and can push it outside small or landscape
surfaces. The player z-index contract already places controls above the
watermark, the detail sheet above both, and the EPG panel above the video
surface.

## Decision

Use one stable watermark position for normal and fullscreen mobile playback:

- right inset: `24 + safeArea.Right`;
- bottom inset: `72 + safeArea.Bottom`;
- picture-in-picture keeps its compact `14/18` insets.

Controls remain responsible for covering the watermark through their higher
z-index. The watermark does not jump when controls appear or disappear.

When the detail sheet is open, hide the watermark temporarily. When the sheet
closes, the normal visibility is restored; the `WatermarkViewModel` still
controls whether free-tier watermark content exists. EPG behavior is unchanged:
the video surface (including the watermark) is already hidden while EPG is open.

## Scope and Compatibility

- Remove the unused controls/detail bottom magic numbers.
- Keep the existing `ApplyWatermarkInsets` call contract and safe-area handling.
- Keep z-index values unchanged.
- Do not change watermark text, opacity, premium eligibility, player controls,
  or EPG layout.

## Regression Coverage

- Source contract requires the stable `72` normal bottom and rejects the old
  `120/180/560` branches.
- Source contract requires detail-open visibility suppression and the XAML
  default bottom margin of `72`.
- Existing mobile z-index and EPG visibility contracts remain green.
