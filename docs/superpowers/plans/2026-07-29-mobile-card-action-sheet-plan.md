# Mobile Card Action Sheet Implementation Plan

## Phase 1 - Shared gesture and request contract

1. Extend `MobilePressableCard` with a cancellable 500 ms long-press gesture.
2. Suppress the tap produced by the completed long-press pointer sequence.
3. Add a routed request contract that captures media, card kind, and
   presentation mode at long-press completion.
4. Keep nested buttons and ScrollViewer movement outside the long-press path.

## Phase 2 - Shared action policy and sheet

1. Add a small action-policy model that maps media kind and presentation mode
   to existing `MainViewModel` commands without duplicating Favorites actions.
2. Create the compact `MobileCardActionsSheet` using the established mobile
   sheet scrim, handle, row, dismissal, and accessibility patterns.
3. Close before command execution and clear media/callback references on every
   dismissal path.

## Phase 3 - MainView host and card migration

1. Host one root action sheet in `MainView` at Z-index 47500.
2. Handle routed card requests, resolve localized action rows, recheck
   `CanExecute`, and execute the existing command.
3. Give the sheet first Back priority and close it during navigation, profile
   changes, playback startup, and detach.
4. Remove all native card `MenuFlyout` and `ContextFlyout` declarations.
5. Wire Live, VOD, Series, and Continue Watching cards to the shared long-press
   request while preserving short tap and the Live favorite button.

## Phase 4 - Verification

1. Add regression tests for long-press cancellation/suppression, action
   matrices, root hosting, Back priority, and removal of native flyouts.
2. Run focused mobile regression tests and `git diff --check`.
3. Build Android Debug and install it on the connected device.
4. Verify short tap, long press, scrolling cancellation, every dismissal path,
   repeated long presses, and actions in all participating views.
