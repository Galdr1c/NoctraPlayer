# Mobile Image Ownership and Cache Commit Implementation Plan

Date: 2026-08-13
Requirements: P1-07, P1-12, P1-13, P1-14
Design: `docs/superpowers/specs/2026-08-13-mobile-image-ownership-design.md`

## Task 1 — RED: resource and LRU ownership

- Add tests for a reference-counted resource that disposes once after owner and consumer release.
- Add tests for eviction callbacks and atomic projected reads.
- Run only these tests and confirm failures are caused by missing behavior.

## Task 2 — GREEN: ownership primitives

- Implement `SharedImageResource<T>` and idempotent consumer lease.
- Extend `ByteBudgetLruCache` with an optional post-lock release callback and projected lookup.
- Emit current/peak owned-byte and consumer-lease telemetry.
- Run Task 1 tests until green.

## Task 3 — RED: consumer-aware shared publication

- Add cancellation-barrier tests where the final consumer cancels before a cancellation-insensitive
  loader returns.
- Prove no publish occurs, the producer value is released once, and a newer same-key load remains
  isolated.
- Add two-consumer tests proving publication happens once after a successful claim.

## Task 4 — GREEN: coordinator publication protocol

- Add a projected-result overload to `SharedImageLoadCoordinator`.
- Keep producer ownership until a successful projection/cache transfer or final abandonment.
- Keep callbacks exactly once and identity-guard old entries.
- Run all shared-load tests until green.

## Task 5 — RED/GREEN: RemoteImage integration

- Add source-contract tests for explicit lease storage/release and consumer-aware cache commit.
- Replace raw bitmap cache entries with shared resources.
- Remove cache writes from the downloader and publish only through the coordinator claim path.
- Return consumer leases from cache/load paths and dispose stale dispatcher results.
- Clear/release old source on URL/bucket change, detach, inactive surface, and empty URL; preserve only
  the same effective key.

## Task 6 — Verification

- Run focused ownership, LRU, load coordinator, image policy, and decode profile tests.
- Repeat the focused set ten times.
- Run the complete test suite and classify only pre-existing unrelated failures.
- Run `git diff --check` and inspect the scoped diff.

## Task 7 — Independent review

- Review concurrency, callback ordering, exact-once disposal, stale dispatcher callbacks, and cache
  identity behavior.
- Resolve every Critical/Important finding with a failing regression test first.

## Task 8 — Android acceptance and reporting

- Publish/sign the Android arm64 APK.
- Install with `adb install --user 0 -r -d` and verify `firstInstallTime` remains unchanged.
- Exercise Movies, Series, Live, and Search scrolling, navigation, and launcher resume cycles.
- Record PID continuity, ANR/crash evidence, and before/after PSS/native/graphics measurements.
- Update the main report with implementation, automated tests, review, APK hash, and live evidence.

