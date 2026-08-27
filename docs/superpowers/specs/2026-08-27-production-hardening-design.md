# Production hardening design — billing acknowledgement and release compliance

## Scope

This change closes the release-hardening items confirmed against the current
`45c2f9f` HEAD. It does not alter the already verified banner no-fill refresh
path, add RTDN/Firestore state, or change the product catalog.

## Design

1. **Play Billing acknowledgement**
   - Query purchases as today, but do not acknowledge them before verification.
   - Filter to the two Noctra products, verify each token through the secure
     backend, and acknowledge only after a positive verified entitlement is
     received.
   - Keep entitlement state exclusively driven by the verified backend result.
   - Do not acknowledge `PENDING` purchases. A later purchase query retries an
     acknowledgement that failed transiently.
   - The purchase callback only requests an entitlement refresh; it does not
     perform an unverified acknowledgement itself.

2. **FFmpeg LGPL release readiness**
   - Keep the exact source/build recipe and decoder list in
     `THIRD-PARTY-NOTICES.md`.
   - Make the written-offer contact explicit and keep a checklist entry
     requiring the corresponding unstripped object/relinkable artifacts to be
     archived for the public release.
   - Do not claim that artifacts are available until the release package is
     actually produced.

3. **Platform documentation**
   - Align README prerequisites with the deliberate Android API 28 (Android 9)
     minimum declared by `Noctra.Android.csproj`.

## Verification

- Add a source-contract regression test proving acknowledgement does not occur
  in the pre-verification query block or purchase callback, and that the
  verified-product loops acknowledge only after backend verification.
- Run the focused billing tests, then the complete `Noctra.Tests` and billing
  API suites.
- Confirm the working tree has no unrelated changes. No commit or push is part
  of this task.
