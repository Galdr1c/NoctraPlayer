# Mobile Settings Scoped Status and Custom EPG Design

## Goal

Fix the Custom EPG editor so repeated Add actions cannot create or lose empty rows, preserve editing focus across auto-save, and show mobile settings feedback inside the panel that initiated the operation instead of at the top of the page.

## Confirmed root causes

`AddCustomEpg` currently appends an empty item and immediately queues an auto-save. The settings snapshot omits empty URLs. When `SettingsService.SaveAsync` raises `SettingsChanged`, `SettingsViewModel` reloads the complete form and replaces `CustomEpgUrls`. The draft row therefore disappears. The same self-triggered reload can replace an editor while the user is typing and lose its focus or caret.

`MobileSettingsView` also binds the shared `StatusMessage` directly below the page title. Channel, EPG, privacy, cache, reset, and auto-save operations all write to that property, so unrelated feedback appears in one global location without panel context.

## Custom EPG behavior

- The list may contain at most one incomplete draft.
- Add is available only when every existing row is a valid absolute HTTP or HTTPS URL and the applicable free or premium source limit has not been reached.
- A valid URL requires an absolute URI, an `http` or `https` scheme, and a non-empty host.
- Pressing Add while a draft is empty or invalid does not append another row.
- Adding an empty draft does not queue persistence.
- An empty or invalid draft remains visible while it is edited and displays localized validation feedback inside the Custom EPG panel.
- Once the draft becomes valid, its value is eligible for debounced persistence and Add becomes available again.
- Removing a row persists the removal. Removing the pending draft does not reintroduce it.
- Only valid URLs are written to settings. Existing persisted values are not silently deleted merely because the user temporarily enters an invalid edit.
- Free and premium source limits remain unchanged.

## Auto-save and reload ownership

`SettingsViewModel` will distinguish its own in-flight save notification from an external settings change. Its own synchronous `SettingsChanged` notification will not call `LoadSettings`, because the current form has already supplied the saved snapshot. External changes will continue to reload the form.

This preserves the active Custom EPG editor, text selection, caret, and keyboard focus while retaining external synchronization. Successful auto-saves are silent. A failed auto-save is routed to the panel or panels whose changed properties participated in that save.

## Scoped mobile feedback

The page-level `StatusMessage` binding beneath the Settings title will be removed. Mobile-specific feedback properties will be bound within the relevant cards:

- playback and data usage;
- appearance and language;
- audio and subtitles;
- downloads;
- channel list;
- EPG, including Custom EPG validation and limits;
- privacy/history;
- cache;
- reset-to-defaults action.

Update and promo-code panels keep their existing local status properties. Operation progress shown by the global loading overlay remains tied to the active channel or EPG refresh; it is not used as a general settings success banner. Desktop bindings to the legacy shared `StatusMessage` remain supported.

Panel messages use the existing semantic brushes: errors use `ErrorBrush`, warnings use `WarningBrush`, and successful user-initiated results use `SuccessBrush` or the existing muted style where appropriate. Empty messages collapse through the existing visibility converter or equivalent compiled binding.

## Status routing

Auto-save requests will retain the originating settings area until persistence completes. If coalescing combines changes from multiple areas, a failure is exposed in every affected area; a success remains silent. Explicit commands write directly to their owning area:

- channel scan, refresh, cancellation, and failure -> Channel List;
- EPG scan, refresh, cancellation, URL validation, and source-limit feedback -> EPG;
- clear history -> Privacy;
- clear cache -> Cache;
- reset defaults -> Reset action;
- application update and promo redemption -> their existing local areas.

Starting a new action in an area clears stale feedback for that area. An unrelated panel action does not erase another panel's error.

## Compatibility and scope

- No database schema or persisted settings format changes are required.
- Existing valid Custom EPG sources remain compatible.
- Provider behavior for Xtream, M3U, and Stalker is unchanged; this work affects the shared settings editor only.
- The desktop settings window keeps its current shared status contract.
- This change does not add an EPG source picker, URL probe, or background reachability check. URL validity is syntactic; network availability is verified by the existing EPG refresh flow.

## Test strategy

Implementation follows red-green-refactor:

1. Add failing behavior tests for HTTP/HTTPS validation, invalid schemes, missing hosts, one-draft enforcement, free/premium limits, and Add command availability.
2. Add a failing regression test proving a self-originated settings save does not replace the active Custom EPG collection, while an external settings change still reloads it.
3. Add failing tests showing invalid drafts are not persisted and valid sources/removals are persisted.
4. Add failing mobile view contract tests proving the page-top status binding is gone and local status bindings exist in their owning cards.
5. Implement the minimum ViewModel and XAML changes required to pass each test.
6. Run the focused tests, the full test suite, and the Android build.
7. Install on the connected Android test device and verify rapid Add taps, invalid and valid URL editing, removal, focus/caret retention, panel-local messages, and channel/EPG refresh progress.

## Acceptance criteria

- Ten rapid Add taps with no URL entered create exactly one draft row.
- Add stays unavailable for blank, malformed, non-HTTP, or hostless values.
- A valid HTTP/HTTPS URL persists and enables creation of the next row.
- Auto-save never makes the active editor disappear or lose focus through a self-triggered reload.
- Removing a source cannot cause previously added sources to be restored or unrelated sources to be deleted.
- No routine settings result appears below the page title.
- Each command result or validation error is displayed inside its owning panel.
- Successful auto-save produces no visible message.
- Existing desktop status behavior, the full automated suite, and the Android build remain healthy.
