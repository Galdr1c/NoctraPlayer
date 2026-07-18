# Mobile category sheet search design

## Scope

Add local, instant category filtering to the shared mobile category selection sheet used by Live, Movies, and Series. Keep the fixed All action beside the search field and preserve the existing virtualized category list.

## UI

- Replace the single full-width All row with a two-column header row.
- Keep All on the left using the current card styling and selected radio indicator.
- Add a search field on the right using the existing mobile search vocabulary: Magnify icon, localized placeholder, and an in-field Close button.
- Show the Close button only while the effective search query is non-empty.

## Behavior

- Filter as text changes; no submit action is required.
- Match category names with `CurrentCultureIgnoreCase`.
- Trim leading and trailing whitespace before filtering.
- Clearing restores all categories and returns focus to the search field.
- Calling `Show` resets the previous query before categories are refreshed.
- Group collection and selected-group changes continue to refresh through the existing dispatcher-safe path.

## Performance and compatibility

- Do not mutate `MainViewModel.Groups`.
- Preserve `VirtualizingStackPanel`, its cache length, and the single `Categories.ReplaceAll` update.
- Adapt the supplied patch to current styling instead of applying its older XAML context verbatim.

## Verification

- Add behavior tests for case-insensitive matching, whitespace, empty queries, and source-order preservation.
- Add a regression guard for reset, clear/focus behavior, shared-sheet controls, and retained virtualization.
- Run focused tests, the full test suite, and the Android build.
