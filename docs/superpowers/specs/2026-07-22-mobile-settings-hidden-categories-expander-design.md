# Mobile Settings Hidden Categories Expander Design

## Problem

`MobileSettingsView` currently renders every hidden Live TV, Movie, and Series category directly inside the settings page's main `ScrollViewer`. A profile with many hidden categories therefore makes the Channel List card and the whole settings page excessively tall. The three unbounded `ItemsControl` instances also create every row even when the user does not need to manage hidden categories.

## Goal

Keep the hidden-category controls accessible without allowing their size to dominate the settings page. The EPG and later settings panels must remain easy to reach regardless of the number of hidden categories.

## User Experience

- Replace the three always-visible hidden-category lists with three independent expanders: Live TV, Movies, and Series.
- All three expanders start collapsed whenever `MobileSettingsView` is opened.
- Each collapsed header shows the localized category-type label, the current hidden-category count, and a disclosure chevron.
- Expanding a section reveals the existing category rows and `Show` action.
- Expanded content has a maximum height of 260 logical pixels, approximately four to five rows on the target mobile layout. Additional rows scroll inside the expanded section.
- More than one section may be open, but each section remains height-bounded, so the page can never grow in proportion to the full category count.
- Removing a hidden category updates the list and count immediately. Removing the final row leaves the existing localized empty-state message visible inside the open section.
- Expansion state is transient UI state. It is not persisted and does not add a setting or database field.

## Implementation Boundary

The change is limited to the hidden-category portion of `Noctra.Mobile/Views/MobileSettingsView.axaml` plus regression tests. Existing collections, `UnhideGroupCommand`, localization strings, backend persistence, refresh behavior, and channel status messages remain unchanged.

The mobile implementation follows the existing desktop settings pattern: a collapsed `Expander` containing a height-bounded `ScrollViewer` and the current `ItemsControl`. Mobile styling will use existing settings-card, category-row, text, border, and accent resources rather than introducing a new visual system.

## Scrolling and Layout

The outer settings page remains the primary scroll surface while all sections are collapsed. When a hidden-category section is expanded, its internal `ScrollViewer` handles overflow after the visible four-to-five-row window. The height cap prevents the inner list from pushing EPG and later panels arbitrarily far down the page.

## Validation

- Add a regression guard that verifies exactly three hidden-category expanders exist in the mobile settings view.
- Verify every expander explicitly starts with `IsExpanded="False"`.
- Verify each hidden-category list is inside a `ScrollViewer` with `MaxHeight="260"`.
- Verify the three existing collection bindings and `UnhideGroupCommand` remain present.
- Run the focused regression test, the complete .NET test suite, and the Android project build.
- On the connected DBY-W09 device, verify collapse/expand behavior, internal scrolling with a long list, the `Show` action, immediate count updates, closing the expander, and continued access to the EPG panel.

## Out of Scope

- Searching or filtering hidden categories.
- A separate category-management sheet.
- Persisting expander state.
- Changes to hidden-category storage or query behavior.
- Changes to the desktop settings implementation.
