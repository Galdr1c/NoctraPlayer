# Update and Settings Lifecycle Hardening

## Scope

This change closes the three review findings in the `e4ce064` through `efbde60` range:

1. Align five stale mobile guard tests with the current class-based input and segmented-selector design.
2. Give each Settings surface an explicit dependency-injection scope so disposable transient view models are released when the surface closes.
3. Add behavioral tests for shared application-update state notifications and the unsupported service.

## Settings ownership

`SettingsViewModel` remains transient. A reusable scoped service lease creates a child DI scope, resolves the view model inside that scope, and disposes the scope exactly once. Desktop Settings resolves its window inside a scope that lives until `ShowDialog` returns. Mobile Settings owns a lease only while the Settings destination is active; leaving Settings, replacing the view model, or detaching the main view releases it and clears the DataContext.

This avoids a singleton view model carrying profile-specific state across sessions and avoids resolving disposable transients from the root provider.

## Update state notifications

A platform-neutral update-state publisher centralizes event-argument construction while preserving the service instance as the event sender. Android and Microsoft Store services delegate their existing status, progress, and error notifications to it. Tests verify ordered progress/terminal transitions, cancellation and error payloads, byte-based progress calculation, and the NoOp service's unsupported behavior.

Platform Store and Play SDK calls remain unchanged; this scope does not introduce mock wrappers around those SDKs.

## Mobile guard contracts

The release guards validate effective behavior rather than obsolete XAML placement:

- common TextBox styles own URL/search/password/PIN keyboard hints and selection colors;
- views opt into those behaviors through classes;
- the provider selector keeps three equal columns and the current transparent segmented theme;
- the category clear action uses the current close-circle icon;
- a zero-height viewport explicitly reports the TextBox as not visible so the caller requests `BringIntoView`.

## Verification

Run focused tests first for each red-green cycle, then the complete `Noctra.Tests` suite. Build the Android project separately if the environment completes the Android workload within the available command window.
