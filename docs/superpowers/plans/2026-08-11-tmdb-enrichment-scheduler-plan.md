# NoctraPlayer TMDB Enrichment Scheduler Implementation Plan

**Design:** `docs/superpowers/specs/2026-08-11-tmdb-enrichment-scheduler-design.md`  
**Method:** Root-cause verification, red-green-refactor, code review, then Android acceptance  
**Scope:** P0-11 and P0-12 only

## Phase 1 — Prove the scheduler invariants in red tests

1. Add `Noctra.Tests/TmdbEnrichmentSchedulerTests.cs`.
2. Write a global-concurrency test that schedules work from multiple logical batches, blocks
   the workers, and asserts observed peak concurrency is at most three.
3. Write a bounded-capacity/latest-work test using a one-worker/two-pending configuration.
   Block the active item, enqueue three pending items, and assert the oldest pending item is
   dropped while the newest executes.
4. Write cancellation tests for both a queued item and a started item.
5. Run only the new fixture and observe the expected compile/red failure because the scheduler
   contract does not exist.

## Phase 2 — Implement the singleton bounded scheduler

1. Add `ITmdbEnrichmentScheduler` and immutable `TmdbEnrichmentSchedulerOptions` under
   `Noctra.Core/Services/Interfaces`.
2. Add `TmdbEnrichmentScheduler` under `Noctra.Core/Services` with:
   - lock-protected linked pending queue;
   - identity-aware key dictionary covering queued and active work;
   - three long-lived workers by default;
   - pending capacity 64 and drop-oldest admission;
   - immediate pending cancellation removal;
   - cooperative started-work cancellation;
   - safe disposal and per-item exception isolation.
3. Add monotonic `PerformanceTrace` counters for schedule/start/complete/cancel/drop/duplicate/
   failure and active/pending high-water marks.
4. Register the scheduler once with singleton lifetime in `ServiceCollectionExtensions`.
5. Run the scheduler fixture until green, then repeat it under contention.

## Phase 3 — Put every list TMDB batch behind the global scheduler

1. Add scheduler-focused tests around `TmdbSyncService.EnrichSeriesBatchAsync` proving two
   overlapping batch calls cannot exceed the configured worker count.
2. Inject `ITmdbEnrichmentScheduler` into `TmdbSyncService`.
3. Remove the per-call `SemaphoreSlim(MAX_CONCURRENT)` and per-batch task fan-out.
4. Schedule each eligible Series item with a stable key and await the returned admitted tasks.
5. Move the 750 ms rate delay into the scheduler so every list-enrichment source shares it.
6. Preserve M3U-only filtering, provider metadata precedence, and existing database fields.
7. Re-throw expected cancellation rather than logging it as a generic item failure.
8. Run the focused service and metadata tests.

## Phase 4 — Bind Movies/Series/Search work to navigation ownership

1. Add a MainViewModel regression fixture that gates a Movies metadata request, navigates to
   Series, and asserts the Movies token is cancelled and no stale in-memory commit occurs.
2. Add a playlist-switch case proving the same view with a new playlist creates a new scope.
3. Add an immutable enrichment-scope record to `MainViewModel` containing generation, view,
   playlist id, CTS, and pre-captured token.
4. Replace/cancel the scope on `ActiveView`, playlist/profile replacement, and app closing.
5. Remove `_channelVisualEnrichmentSemaphore`, `_seriesVisualEnrichmentSemaphore`, and their
   per-page `Task.Run` loops.
6. Schedule visible VOD and Series items individually through the global scheduler.
7. Pass the scope token into every metadata HTTP call and EF Core async operation.
8. Validate the scope before tracked-entity mutation, `SaveChangesAsync`, and again inside the
   dispatcher callback.
9. Keep Series no-poster caching and current pending-key cleanup semantics generation-safe.
10. Run the new navigation tests and existing incremental/navigation fixtures.

## Phase 5 — Regression and review gates

1. Run the scheduler and TMDB fixtures at least 10 times.
2. Run MainViewModel navigation, pagination, profile reload, Search, metadata, and mobile lazy
   host regression groups.
3. Run the full `Noctra.Tests` suite and compare failures with the known baseline.
4. Build `Noctra.Core`, `Noctra.Mobile`, and `Noctra.Android`.
5. Run `git diff --check`, inspect the complete scoped diff, and request a Critical/Important
   code review before device installation.

## Phase 6 — APK and Android acceptance

1. Build the signed debug APK from `D:\IPTVPlayer` and record SHA-256.
2. Record the existing package `firstInstallTime` and `lastUpdateTime`.
3. Install only with `adb install -r`; do not uninstall or clear application data.
4. Run at least 50 `Movies -> Series -> Search -> Movies` transitions with repeated scrolling.
5. Run at least 10 background/resume cycles.
6. Confirm the process remains alive, the destination content is correct, and logcat contains
   no ANR, crash, OOM, or stale-commit signal.
7. Record scheduler active and pending high-water telemetry; require active `<= 3` and pending
   `<= 64`.
8. Update the P0-11/P0-12 report sections with exact automated/build/device evidence and mark
   them complete only after every gate passes.
