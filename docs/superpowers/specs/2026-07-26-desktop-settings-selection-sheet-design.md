# Desktop settings selection sheet design

## Scope

This change fixes the current review findings and replaces the remaining desktop
settings ComboBoxes with the existing virtualized `DesktopSelectionSheet`.

The implementation covers:

- stale tests introduced by the async-disposal and navigation/UI changes;
- the reduced click target on desktop Live TV cards;
- invalid and duplicated desktop card context-menu actions;
- selection sheets in `SettingsWindow` and `GlobalSettingsWindow`.

## Architecture

`DesktopSelectionSheet` remains the single sheet implementation. Each settings
window owns one instance placed directly in its root Grid:

- `SettingsWindow`: `Grid.RowSpan="3"`;
- `GlobalSettingsWindow`: `Grid.RowSpan="2"`;
- both: `ZIndex="50000"`.

The sheet surface is bottom-aligned, centered, and limited to 720 pixels. Its
scrim fills the entire window, including the header, tabs, footer, and loading
overlay. Existing scrim click, close button, downward drag, pointer-capture
recovery, and Escape behavior are retained.

`DesktopSettingsSelectionCatalog` is the shared source for labels and option
values. It creates:

- data-usage options;
- subtitle and audio languages;
- download quality options;
- channel and EPG refresh intervals;
- 25 EPG timezone options;
- history-retention options;
- application languages.

The 25 timezone rows use the sheet's `ListBox` and
`VirtualizingStackPanel`. Premium refresh options remain selectable UI rows but
are marked locked; choosing one opens the existing Premium upsell without
changing the setting.

## Data flow and localization

The button beside each setting shows the current value. Button clicks build a
fresh option list from the current ViewModel state. Choosing an unlocked option
updates the same ViewModel property previously bound by the ComboBox.

Both windows subscribe to the relevant ViewModel/settings changes and
`LocalizationSource.PropertyChanged`. Labels are refreshed immediately after a
language change. All subscriptions are removed when the window closes.

The imported reference files are treated as behavioral guidance rather than
copied verbatim because their non-ASCII literals contain encoding corruption.

## Regression fixes

- The desktop Live TV card keeps the favorite button separate while restoring a
  full-card playback hit target.
- Live TV cards do not offer My List.
- Favorites and My List presentation modes show one appropriate removal action,
  with the correct localized label.
- Tests are updated to assert current behavior rather than removed control names
  or synchronous disposal.

## Verification

Tests must cover:

- async autosave disposal and flush behavior;
- current navigation-rail style selectors and the five-item bottom navigation;
- settings selection buttons and root-level sheet placement;
- option catalog values, premium locks, and timezone count;
- desktop card hit targets and context-menu visibility rules.

The complete test suite and desktop/Android builds are run after implementation.

## Safety

Before production files are edited, a timestamped copy of every file in the
change set is stored under `.tmp/backups/`. Existing Git history remains the
primary rollback mechanism.
