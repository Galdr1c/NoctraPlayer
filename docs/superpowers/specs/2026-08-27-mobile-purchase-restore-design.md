# Mobile purchase restore design

## Goal

Replace the MobileUpsellView secondary "Continue with Free" action with an
explicit purchase-restore action. Users who bought Premium with the current
Google Play account can recover the entitlement without starting a new
purchase.

## Data flow

1. The user taps **Already purchased? Restore**.
2. The mobile view resolves `IStorePurchaseService` and checks `IsSupported`.
3. On Android, `RestorePurchasesAsync` queries Play purchases, verifies every
   recognized token through the existing secure billing backend, and raises
   `EntitlementChanged`.
4. The view awaits `ILicenseService.RefreshSubscriptionStatusAsync` so the
   shared entitlement state is authoritative before deciding what to show.
5. Active Premium emits the existing notification/toast feedback and closes
   the sheet. No active purchase shows an informational message; unsupported
   store or a transient failure shows a localized retry message.

## UI and state

- The X button remains the only dismiss action; the former free-continuation
  text is removed from the mobile sheet.
- A restore-in-progress state disables plan and restore buttons and reuses the
  existing spinner, preventing duplicate Play queries.
- A successful restore uses the existing notification/toast service; the
  spinner is the only in-sheet progress animation.
- Restore does not launch a purchase flow and does not acknowledge anything on
  the client before backend verification.

## Localization and verification

- Add `Upsell.Action.Restore` plus localized unavailable/not-found/failed
  messages for all supported languages.
- Add source-contract tests for the XAML action, restore/backend call ordering,
  unsupported-store handling, and translation coverage.
- Run focused tests, the complete `Noctra.Tests` suite, and an Android Release
  build. No commit or push is part of this task.
