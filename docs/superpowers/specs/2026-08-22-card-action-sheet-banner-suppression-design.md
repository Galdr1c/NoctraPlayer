# Card Action Sheet Banner-Safe Positioning Design

## Problem

The Android banner is hosted by a native control. Avalonia ZIndex orders Avalonia visuals but cannot reliably cover a native surface, so the shell-owned long-press card action sheet appears behind the banner even though its ZIndex is 47,500.

## Selected Behavior

- The banner remains visible.
- MobileCardActionsSheet moves from the root overlay collection into the same two-row content grid as BannerAd.
- MobileCardActionsSheet occupies content row 0; BannerAd remains in row 1.
- The sheet therefore ends at the banner's top edge, matching page-local selection sheets and avoiding NativeControlHost overlap by layout rather than ZIndex.
- Existing close, back, drag, scrim, navigation, and safe-area behavior remains unchanged.

## Alternatives Rejected

- Raising ZIndex cannot overtake Android NativeControlHost composition.
- Suppressing the banner would hide revenue/UI that other selection sheets intentionally keep visible.

## Verification

An XML regression test will require CardActionsSheet and BannerAd to share one parent, with rows 0 and 1 respectively, and will forbid a root-level RowSpan on CardActionsSheet. Existing back-navigation and card action tests remain green, followed by the full suite and Android Debug build.
