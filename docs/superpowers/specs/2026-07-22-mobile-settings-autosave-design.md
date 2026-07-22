# Mobile Settings Auto-Save Design

## Problem

`MobileSettingsView` currently behaves like a staged form. Most controls only update fields on `SettingsViewModel`; the real `ISettingsService` is updated only when the user scrolls to the bottom and presses **Save Settings**. Theme selection is an exception and applies immediately. This inconsistency makes language, playback, download, EPG, and privacy settings appear broken.

The connected-device reproduction confirmed that selecting Turkish changes only the selection label. Pressing **Save Settings** later persists the form, raises `SettingsChanged`, and changes the application language successfully.

## Considered Approaches

1. **ViewModel-owned auto-save (selected).** Track editable form-property changes in `SettingsViewModel`, apply runtime-sensitive values immediately, and persist through one coalescing save pipeline. This keeps behavior testable and independent of the mobile view.
2. **Code-behind auto-save.** Have `MobileSettingsView` call the save command after every interaction. This is simpler initially but misses keyboard/binding changes, duplicates behavior across views, and couples persistence to one UI.
3. **Keep explicit save with a sticky footer.** Making the button permanently visible would improve discoverability but would preserve the inconsistent theme behavior and allow settings to be lost when leaving the screen.

## Approved Behavior

- Language, selection-sheet options, radio buttons, sliders, and toggle switches are applied and queued for persistence when changed.
- Application language is switched immediately through `ILocalizationService`; persistence then raises the existing `SettingsChanged` event so culture-dependent services and the application shell update through the current path.
- Theme keeps its existing immediate-apply behavior and participates in the same persistence guarantees without duplicate saves.
- Text fields use a short debounce so typing does not write the settings file for every character.
- Rapid changes are coalesced. Saves are serialized so an older asynchronous save cannot overwrite a newer value.
- Loading profile settings, reacting to a settings-service refresh, and resetting the form do not create a save loop.
- Leaving the settings screen flushes any pending valid change through the ViewModel lifecycle; persistence does not depend on the view remaining attached.
- Destructive/action operations such as clearing history, clearing cache, refreshing playlists, applying promo codes, and reporting bugs remain explicit commands.
- The bottom **Save Settings** button is removed. **Reset to Defaults** remains available as a full-width action.
- A short status message reports successful automatic persistence or a save failure without blocking interaction.

## Architecture and Data Flow

`SettingsViewModel` remains the owner of the editable form and persistence:

1. A generated property setter raises a change notification.
2. The ViewModel classifies the property as immediate or debounced.
3. Immediate runtime behavior, such as language or theme, is applied on the UI thread.
4. A coalescing auto-save scheduler captures the latest complete form snapshot.
5. A single serialized writer applies the snapshot to the active profile through the existing `SaveForActiveProfileAsync` path.
6. `ISettingsService.SaveAsync` persists atomically and raises `SettingsChanged`, preserving existing app, player, theme, and localization subscribers.

The scheduler belongs to the long-lived ViewModel and is disposed/cancelled with it. UI code only updates properties and selection labels.

## Error Handling

- No save starts when there is no active profile or while settings are being loaded.
- Cancellation caused by a newer edit is silent and expected.
- Persistence errors keep the latest in-memory form values, surface a localized failure status, and allow the next edit to retry.
- The existing atomic file-write implementation remains unchanged.

## Tests

- A language change applies immediately and persists without invoking `SaveSettingsCommand`.
- A representative toggle and selection-sheet property persist automatically.
- Text changes are coalesced rather than saved once per keystroke.
- Rapid changes cannot allow an older save to win.
- `LoadSettings` and external `SettingsChanged` refreshes do not trigger recursive saves.
- Reset-to-defaults still persists and refreshes the form.
- Mobile XAML no longer exposes **Save Settings**, while **Reset to Defaults** remains.
- Existing settings, localization, mobile regression, and full solution tests stay green.

## Scope

This change fixes settings application and persistence semantics. It does not redesign the settings layout, add new settings, or change the meaning of existing values.
