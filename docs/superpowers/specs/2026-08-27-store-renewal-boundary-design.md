# Store subscription renewal-boundary design

## Problem

The local expiry timer currently recomputes the license from the cached Play
expiry immediately. During Google Play test renewals, the next expiry can reach
the client/backend a few seconds later. The timer therefore publishes a brief
Free state, enables ads, and exposes the Premium CTA even though the
subscription is renewing.

## Design

- Promo and lifetime entitlements keep their current expiry behavior.
- When a Google Play subscription reaches its cached expiry, the timer starts
  an authoritative store/backend refresh before publishing a downgrade.
- The last Premium state remains visible only while this bounded refresh is in
  flight. A renewed expiry replaces the old one without an intermediate Free
  notification.
- If Play/backend returns an authoritative inactive entitlement, the normal
  synchronization publishes Free immediately after the refresh.
- If verification fails transiently, the existing cache policy remains in
  force; the expiry handler must not create concurrent refresh loops or survive
  service disposal.

## Verification

- Add a regression test with an expiring store entitlement whose second Play
  query returns a renewed expiry. The test must prove no Free notification is
  observed and that the timer performs the second query automatically.
- Preserve the existing promo-expiry test, which must still become Free at its
  local expiry.
- Run focused tests, all Noctra tests, Billing API tests, and Android Release
  build. No commit or push is part of this task.
