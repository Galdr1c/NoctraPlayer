# NoctraPlayer TMDB Enrichment Scheduler Design

Date: 2026-08-11  
Status: Approved for direct implementation  
Source findings: `P0-11` and `P0-12` in
`docs/NoctraPlayer_Performance_Stability_Full_Report.md`

## Goal

Prevent scroll-driven TMDB work from accumulating after the originating page is no longer
active. All list enrichment must share one application-wide concurrency limit, retain a
bounded amount of queued work, and stop obsolete HTTP, database, and UI commits when
navigation or playlist ownership changes.

## Confirmed Root Cause

The current system has three independent forms of admission:

1. Every visible Movies or Series page starts a new fire-and-forget `Task.Run` loop.
2. `MainViewModel` owns separate three-slot semaphores for channel and Series visual work, so
   those paths can already reach six simultaneous requests in one view model.
3. `TmdbSyncService.EnrichSeriesBatchAsync` creates a new three-slot semaphore for every
   batch, so multiple pages multiply the intended limit again.

Those loops use the profile-load token. Changing `ActiveView` does not cancel it, and the
direct visual calls omit the available metadata cancellation token. Old work can therefore
continue through HTTP, EF Core, and dispatcher commits after navigation.

## Considered Approaches

### A. Global bounded scheduler plus navigation scope — chosen

Use one singleton scheduler with three workers and a bounded pending queue. Every list TMDB
operation is admitted through it and receives the originating navigation token. This closes
both the global-limit and stale-lifetime failures.

### B. One shared semaphore only

This caps active requests but leaves an unbounded number of retained tasks and models waiting
behind the semaphore. It does not solve P0-11.

### C. A bounded queue only inside `MainViewModel`

This limits one shell instance, but `TmdbSyncService` batches and any other enrichment caller
can still multiply concurrency. It does not establish an application-wide invariant.

## Scheduler Architecture

Add `ITmdbEnrichmentScheduler` and a singleton `TmdbEnrichmentScheduler` in `Noctra.Core`.
The public operation is keyed and non-blocking at admission:

```csharp
Task ScheduleAsync(
    string key,
    Func<CancellationToken, Task> work,
    CancellationToken cancellationToken = default);
```

The returned task represents the admitted work. Duplicate live keys share the existing task.
Production defaults are:

- pending capacity: 64 items;
- worker count/global active limit: 3;
- per-worker post-request delay: 750 ms.

The scheduler uses a lock-protected linked pending queue rather than an unbounded `Task.Run`
or a channel with opaque drop behavior. Before admission it removes cancelled pending items.
When full, it drops and completes the oldest pending item, because newly discovered items are
closer to the user's current scroll position. Active work is never forcefully removed from the
scheduler; its navigation token cooperatively cancels the HTTP/DB path.

Cancellation registration removes pending work immediately. Key removal is identity-aware so
an old active item completing cannot remove a newer replacement with the same key. Scheduler
disposal cancels workers and completes all retained tasks.

## Navigation Ownership

`MainViewModel` owns an immutable visual-enrichment scope containing:

- monotonically increasing generation;
- `AppView`;
- selected playlist id;
- a `CancellationTokenSource` and its pre-captured token.

Changing `ActiveView`, changing the selected playlist/profile, or closing the app replaces or
cancels the scope. Queue methods capture the current scope once and reject work if its view or
playlist no longer matches. Re-entering the same page gets a new generation and cannot be
confused with a still-unwinding older request.

The old channel/Series semaphores and per-page `Task.Run` loops are removed. Movies, Series,
and Search visual work is scheduled item-by-item through the singleton scheduler. Series batch
enrichment uses the same scheduler, so all list enrichment shares the three-worker limit.

## Commit Protocol

Each admitted item follows this sequence:

1. Validate the captured scope and cancellation token.
2. Pass the token to `FetchMetadataAsync`, `SearchSeriesAsync`, or detail fetch.
3. Validate again before opening/loading the EF Core entity.
4. Apply changes to the tracked entity, validate again, and call `SaveChangesAsync(token)`.
5. Validate again inside the dispatcher callback before mutating the in-memory model.

Once a scope is observed as stale, it cannot start a new DB or UI commit. A database operation
that had already entered its provider commit at the exact navigation boundary is allowed to
finish; its UI callback is still rejected. This avoids blocking the navigation thread on DB
rollback coordination.

`OperationCanceledException` is treated as an expected terminal state and is not swallowed as
a generic enrichment failure. Other item failures are logged and do not stop scheduler workers.

## Telemetry

Record narrow `PerformanceTrace` counters for scheduled, started, completed, cancelled,
capacity-dropped, duplicate, failed, and stale-commit-rejected work. Values are monotonic and
include the work key prefix as scope where useful.

## Test Strategy

1. A scheduler test starts work from multiple logical batches and proves global peak
   concurrency never exceeds three.
2. A capacity test blocks one worker, overfills a small configured queue, and proves the oldest
   pending item is dropped while the newest item executes.
3. A cancellation test proves queued work is removed and started work receives cancellation.
4. A MainViewModel navigation test gates a Movies metadata request, navigates to Series, and
   proves no stale Movies DB/UI commit occurs.
5. A Series batch test proves separate batch calls still share the same scheduler limit.
6. Run existing navigation, incremental paging, TMDB/metadata, profile reload, and full-suite
   regressions.

## Android Acceptance

- Build and install with `adb install -r`; do not clear or uninstall application data.
- Run at least 50 `Movies -> Series -> Search -> Movies` transitions with repeated scrolling.
- Include at least 10 background/resume cycles.
- Confirm no ANR, crash, OOM, stale visual commit, or unrecoverable blank page.
- Confirm scheduler telemetry never reports more than three active workers and pending depth
  never exceeds 64.

## Non-Goals

- Player/detail-screen user-requested metadata fetches are not converted into background list
  enrichment work.
- Image download/decode queue policy and EPG enrichment remain outside this change.
- Provider-supplied metadata precedence and current M3U-only enrichment policy do not change.
