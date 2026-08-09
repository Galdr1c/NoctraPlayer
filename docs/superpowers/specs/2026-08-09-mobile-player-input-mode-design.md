# Mobile Player Input Mode Design

**Date:** 2026-08-09  
**Status:** Implemented

## Goal

Make the mobile player usable with touch, Android TV remotes, Bluetooth keyboards, and D-pad input without regressing the existing touch gestures or expanding the change to unrelated application screens.

## Scope

This design applies to:

- `MobilePlayerView`
- `MobilePlayerTimeline`
- Player top and transport controls
- `MobilePlayerSheets` and their child sheets
- `MobilePlayerEpgPanel`

The rest of the mobile application and the desktop player are intentionally out of scope. Desktop already has keyboard-oriented interaction, while this problem is specific to the mobile/Android visual tree.

## Architecture

Add an application-lifetime `MobileInputModeService` registered as an Android singleton. It stores the most recent input mode:

- `Touch`
- `Remote`

The service publishes changes so player controls, sheets, timeline, and EPG use the same state. Pointer interaction switches to touch mode. Navigation keys, Enter, Space, Tab, Back, and Escape switch to remote mode.

The service remembers the last mode for the current application process. It does not persist the choice to settings because input hardware can change at any time.

## Player Behavior

When remote input arrives while controls are hidden, the first navigation/activation key reveals the overlay and focuses Play/Pause. That first key must not also seek or activate an unrelated command.

When controls are visible, Avalonia's normal focus navigation moves between real buttons. Touch input removes forced remote focus styling but leaves the current player gestures unchanged.

Back handling follows this order:

1. Close the active child sheet.
2. Close the player sheet or EPG.
3. Hide the player overlay.
4. Delegate to the existing player-exit behavior.

The explicit back button keeps its existing direct player navigation behavior.

## Timeline Behavior

The timeline remains touch-draggable.

For VOD and series episodes, when the timeline has keyboard focus:

- Left seeks backward 10 seconds.
- Right seeks forward 10 seconds.
- Key repeat is handled naturally by repeated key events; no separate acceleration timer is added.

For live content, timeline arrow keys do not seek. They remain available for normal focus navigation.

## Focus Visuals

Player top buttons, transport buttons, timeline, and sheet options receive a clear `:focus-visible` treatment using the existing accent/focus resources. The visual must not alter control dimensions, cause player overlay relayout, or change touch-mode pressed styling.

The implementation may apply a remote-mode class for styling, but must not change mobile density or rebuild the player layout on the first D-pad press.

## Sheet Focus Lifecycle

Before a player sheet opens, the currently focused opener is captured. In remote mode:

- A sheet focuses its selected enabled option when one exists.
- Otherwise it focuses the first visible enabled action.
- A child-sheet transition places focus in the new child sheet after layout completes.
- Closing restores focus to the captured opener when it remains attached and enabled.

Touch mode must not force focus into a sheet.

Escape and Back close only the topmost player sheet layer before affecting the player overlay.

## EPG Integration

Keep the EPG's existing channel/program D-pad navigation, selection, focus capture, and focus restoration.

Replace its isolated default-to-touch state with the shared input mode service:

- On open, use the service's current mode.
- Pointer interaction reports touch mode.
- Keyboard/D-pad interaction reports remote mode.
- Existing remote focus and layout behavior runs only when the shared mode actually changes.

## Registration and Lifetime

Register `MobileInputModeService` as a singleton in Android dependency injection. Player-related views resolve the shared instance through the existing mobile platform/service resolution pattern. Event subscriptions must be detached with the visual-tree/view lifetime so hot reload and repeated player openings do not accumulate handlers.

## Compatibility

- Existing touch gestures, swipe volume/brightness, timeline dragging, lock behavior, PiP, and sheet animations remain unchanged.
- Android TV remote and Bluetooth keyboard paths share the same keyboard event handling.
- Mouse/pointer clicks behave like touch/pointer mode.
- No desktop changes are required for this mobile-only input controller.

## Verification

No broad source-contract or redundant regression tests will be added. Verification consists of:

1. Android project build.
2. Existing targeted test suite where practical.
3. Physical-device touch regression check.
4. D-pad/Bluetooth keyboard check for overlay reveal, focus navigation, activation, timeline seek, and Back order.
5. EPG open/close and focus restoration check.
6. Player sheet open, nested selection, close, and opener-focus restoration check.
7. Touch input immediately after remote input check.

## Delivery

The completed behavior and verification result will be recorded in `CHANGELOG.md`.
