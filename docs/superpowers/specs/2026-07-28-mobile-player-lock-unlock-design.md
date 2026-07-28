# Mobile Player Lock/Unlock Design

## Goal

Keep accidental-touch protection while making the unlock action obvious, reliable, and accessible on Android.

## Interaction

1. Tapping the lock action locks the player and immediately shows the unlock affordance.
2. While locked, all normal player controls and playback gestures remain disabled.
3. The affordance contains only a lock icon and uses a minimum 180 × 56 DIP touch target.
4. The affordance hides after 2.5 seconds.
5. Tapping anywhere on the locked player shows it again and restarts the full 2.5-second window.
6. A single tap on the lock icon unlocks the player and restores the normal controls.

Long-press timers, pointer-event workarounds, unlock text, and slide-to-unlock behavior are intentionally excluded.

## Verification

- View-model tests cover immediate affordance visibility and visibility-window restart.
- UI regression tests cover the single command, icon-only content, touch target, and absence of long-press handlers.
- Physical-device testing repeats lock, hide, reveal, unlock, and accidental background-touch scenarios.
