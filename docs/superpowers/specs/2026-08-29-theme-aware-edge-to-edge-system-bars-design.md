# Theme-aware edge-to-edge system bars

## Problem

Commit `9608d3e` correctly removed duplicated Android 15/16 top and bottom
safe-area padding, but it also made the non-player window permanently black.
That hides launcher-wallpaper leakage in dark theme while producing black
status/navigation areas and landscape gutters in light theme. System-bar icon
appearance is also fixed to the dark-theme assumption in `styles.xml`.

## Considered approaches

1. Keep the hard-coded black backdrop. Rejected because it cannot support the
   light theme or full-bleed landscape backgrounds.
2. Avoid the issue by taking screenshots on Android 14. Rejected because the
   same defect would remain on real Android 15/16 devices.
3. Keep safe-area normalization and make the backdrop/system chrome follow the
   active Noctra theme. Selected because it preserves cutout safety, player
   composition, and Android 15/16 edge-to-edge behavior.

## Design

- `GetContentSafeArea` remains unchanged: API 35+ hosts already consume top and
  bottom system insets, while left/right cutout insets continue protecting
  interactive controls.
- `MainView` resolves `Bg0Brush` for the active `ActualThemeVariant` and uses it
  as the opaque shell/profile top-level background. It remains transparent only
  while the native player surface is active.
- `IPlayerWindowService` gains a system-bar theme operation. Android applies the
  matching native backdrop and status/navigation icon appearance:
  - light shell: `#FAFAFA`, dark icons;
  - dark shell: `#0A0A0A`, light icons;
  - player: black fallback, light icons, existing immersive behavior.
- Theme changes, orientation/size changes, player enter/exit, focus, and resume
  reapply the same state. No new Activity or player surface is created.
- Background fills the entire edge-to-edge window. Only content padding retains
  cutout left/right insets, so landscape has no black gutters while controls do
  not enter the camera cutout.

## Failure handling

If the Activity/window is temporarily unavailable, the platform operation is a
no-op and is retried by the existing attach, focus, resume, size-change, or
player-state paths. Resource lookup falls back to white for light theme and
black for dark theme.

## Verification

- Source-contract tests cover removal of hard-coded shell black, theme brush
  resolution, Android icon appearance, and reapplication triggers.
- Build the x86_64 debug APK and install it without clearing emulator data.
- Verify portrait, landscape-right, landscape-left, and portrait-return in both
  themes; confirm no launcher wallpaper, unreadable clock/icons, black gutters,
  Activity recreation/crash, or player transparency regression.

## Deliberately unchanged

- Banner retry behavior from `74eabb2`.
- API 35+ top/bottom safe-area normalization from `9608d3e`.
- Player TextureView/Avalonia transparent composition and immersive policy.
