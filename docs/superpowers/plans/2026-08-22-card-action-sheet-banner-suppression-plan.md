# Card Action Sheet Banner-Safe Positioning Plan

1. Add a failing XML regression test requiring CardActionsSheet and BannerAd to share the content/banner grid in rows 0 and 1.
2. Move MobileCardActionsSheet into the content row directly above BannerAd; remove root RowSpan placement.
3. Preserve all sheet behavior and banner visibility without adding suppression logic.
4. Run focused and full tests, Android Debug build, and a device smoke test with a live banner.
