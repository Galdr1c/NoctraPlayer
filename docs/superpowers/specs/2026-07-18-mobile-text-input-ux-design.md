# Mobile text input UX design

## Decision

Keep Avalonia 12.0.4's native mobile text editing and horizontal Cut/Copy/Paste flyout, then add only platform hints and visual tokens that do not replace the framework editing pipeline.

The approved scope is:

- purpose-specific Android keyboards for URL, password, PIN, and search fields;
- appropriate Next, Done, and Search IME actions;
- a minimum 48 dp height for every interactive mobile TextBox;
- Noctra-purple caret and selected-text highlight colors in both themes;
- `adjustResize` on the Android activity so the profile form viewport shrinks above the IME;
- existing Avalonia cursor movement, selection handles, paste replacement, scrolling, and native mobile flyout behavior.

Select All remains intentionally absent from the transient mobile flyout.

## Field mapping

| Field | Content type | IME action |
| --- | --- | --- |
| Profile name | Normal | Next |
| Provider URL / M3U URL | Url | Next |
| Xtream username / Stalker MAC | Normal | Next |
| Password | Password, sensitive, suggestions off | Done |
| PIN / PIN confirmation | Digits, sensitive, suggestions off | Next / Done |
| Global and category search | Search | Search |
| EPG URL | Url | Done |
| User agent / promo code | Normal | Done |

## Keyboard avoidance assessment

`ProfileSetupView` already has the correct fixed-header and bounded `ScrollViewer` layout, including a 40 dp bottom margin. Android `adjustResize` is therefore the missing platform prerequisite.

Do not add the proposed global 250 ms delayed `BringIntoView` behavior in this patch. `ScrollViewer.BringIntoViewOnFocusChange` already defaults to `true`, and a fixed delayed scroll on every TextBox could race keyboard animations, interrupt user scrolling, and move search/settings screens unnecessarily. Add custom IME-inset or delayed behavior only if a reproducible device trace still shows overlap after `adjustResize`.

## Verification

- Source regression tests cover the field mappings, touch targets, accent styling, native flyout preservation, and Android activity mode.
- Build the Android project to validate XAML attached-property and Android activity syntax.
- Run the full test suite.
- When ADB is available, smoke-test profile name, URL, username, password, and both PIN fields with keyboard show/hide, cursor movement, selection, and paste replacement.
