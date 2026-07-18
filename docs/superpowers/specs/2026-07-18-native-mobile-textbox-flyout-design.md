# Native mobile TextBox flyout design

## Decision

Use Avalonia 12.0.4's built-in mobile TextBox context flyout. Remove Noctra's custom vertical `ContextMenu` behavior and do not add Select All.

## Scope

- Remove the behavior attachment from the profile setup TextBox style.
- Remove the now-unused `MobileTextBoxMenuBehavior` implementation and XAML namespace.
- Keep the existing TextBox appearance and text-selection behavior unchanged.
- Let Avalonia supply its horizontal transient Cut, Copy, and Paste actions, including command availability and framework localization.

## Verification

- Add a regression guard proving profile TextBoxes no longer override `ContextFlyout` through the custom behavior.
- Confirm the project still targets Avalonia 12.0.4 and uses `FluentTheme`.
- Run focused tests, the full test suite, Android build, and a device smoke test when ADB is available.
