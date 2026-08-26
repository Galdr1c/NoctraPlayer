# Play Billing Catalog and Release Gate Design

## Problem

The Internal Testing build shows `—` for both Premium prices even though the
`noctra_premium_monthly` subscription and its Turkish price are active in Play
Console. The application requests the monthly subscription and the optional
`noctra_premium_lifetime` one-time product as one catalog operation. The
lifetime product does not yet exist in Play Console, so an unavailable catalog
entry can suppress the valid monthly result. The UI then reports a completely
empty catalog and does not expose its retry action.

The uploaded Release AAB also contains no `Noctra.BillingVerifyUrl` assembly
metadata because neither `.env` nor the build environment supplied
`NOCTRA_BILLING_VERIFY_URL`. A purchase could therefore open in Play but could
not be verified and promoted to Premium afterwards.

## Considered Approaches

1. Create the lifetime product only. This is quick but leaves the client
   fragile whenever one catalog entry is unavailable, region-ineligible, or
   still propagating.
2. Remove the lifetime plan until its Console setup is complete. This changes
   the intended product offering and requires another UI rollout later.
3. Query each product independently and reject Play bundles that lack the
   verification endpoint. This keeps partial catalog availability useful and
   prevents another unverifiable store release.

Approach 3 is selected. The lifetime product will still be created separately
in Play Console, but its temporary absence will no longer hide the monthly
price.

## Runtime Design

`AndroidStorePurchaseService.GetProductsAsync` performs one product-details
request for `noctra_premium_monthly` (`SUBS`) and one for
`noctra_premium_lifetime` (`INAPP`). Each response is handled independently:

- `OK` results are mapped and combined.
- A missing or ineligible product contributes no entry without discarding the
  other product.
- A concise `NoctraBilling` log records product ID, type, response code,
  diagnostic message, and fetched count. Purchase tokens and account data are
  never logged.

When both queries produce no usable product, `MobileUpsellView` keeps the
existing unavailable message and exposes the retry button.

## Release Gate

`build/package-play.ps1` resolves `NOCTRA_BILLING_VERIFY_URL` from the process
environment first and repository `.env` second. Before `dotnet publish`, it
requires a valid absolute HTTPS URL and passes it explicitly as an MSBuild
property. `NOCTRA_BILLING_API_KEY` remains optional and is passed only when
configured. Signing-only keystore creation remains usable without billing
configuration.

## Verification

- Source-contract regression tests pin independent `SUBS`/`INAPP` queries,
  empty-catalog retry, and the pre-publish billing endpoint gate.
- Focused tests must fail before implementation and pass afterwards.
- The complete test suite and Android arm64 Debug build must pass.
- A Debug APK is installed with `adb install -r` so application data remains;
  on-device logs must show the monthly query independently of the missing
  lifetime product.

## Deliberate Non-Changes

- Product IDs and the active monthly base-plan ID are unchanged.
- No Play Console product, price, tester, or payment setting is modified by
  code.
- Backend entitlement rules and purchase acknowledgement behavior are
  unchanged.
- The Play bundle is not uploaded and no commit or push is created.
