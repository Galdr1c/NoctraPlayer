# Android Performance and Long-Session Stability Design

Date: 2026-08-11  
Status: Approved for specification; implementation awaits written-spec review

## Goal

Remove the two highest-confidence causes of progressive Android jank and freezing without changing navigation UX or replacing the current mobile view architecture:

1. resume-time layout recovery fan-out across retained, inactive grids;
2. image work that survives card detachment and accumulates behind concurrency gates.

Android is the primary acceptance platform. Shared `Noctra.Core` work will be handled in later patches and may also benefit desktop, but the desktop-specific image control is outside this first patch.

## Evidence and Root Cause

The consolidated report is directionally correct for the first failure cluster. The current source confirms:

- `MainView.axaml` constructs Home, Live, Movies, Series, Search, Favorites, My List, History, Downloads, and Settings inside one retained `Grid`; navigation changes `IsVisible` rather than detaching the inactive views.
- Every attached `MobileVirtualizingCardGrid` subscribes to the static `MobileAppLifecycle.Resumed` event.
- One Android resume can therefore call `RefreshAfterResume()` on grids belonging to inactive pages.
- Each grid can post up to eight `DispatcherPriority.Render` repair callbacks.
- Every repair currently invalidates measure and arrange from the grid through its complete parent chain.
- `RemoteImage.CancelPendingLoad()` cancels only the consumer's `WaitAsync` call. The shared task continues through the download semaphore, HTTP request, stream copy, and decode semaphore without that consumer lifetime.
- `DownloadGate` and `DecodeGate` limit active concurrency but do not cap the number of distinct queued URLs.

This produces two reinforcing backlogs: resume-time UI/layout work and scroll-time network/decode work. Background/foreground cycles let stale image completions and retained-grid recovery compete for the same first foreground frames.

The current baseline has 1,927 tests: 1,921 pass and six unrelated tests fail. The six known failures concern promo formatting, a mobile selection-style assertion, and download behavior. They are not acceptance failures for this patch unless their failure signature changes.

## Chosen Approach

Use a staged, failure-cluster-first repair. This patch will harden resume/layout and the mobile image pipeline. Navigation collection ownership, TMDB generations, Search ranking, and DB/EPG scheduling remain separate follow-up patches so each root cause can be reproduced, tested, and measured independently.

The patch deliberately keeps the retained-page architecture for now. Inactive work is gated at its source. Moving all pages to a lazy active host remains a possible later change after telemetry shows whether retained view memory itself is still a material problem.

## Scope

### Included

- Active/foreground eligibility policy for mobile grid recovery.
- Generation-safe recovery with a maximum of three attempts.
- Grid-local layout invalidation at `DispatcherPriority.Loaded`.
- A ref-counted, same-key image request coordinator.
- End-to-end cancellation from the last consumer through queue admission, HTTP, stream copy, and decode admission.
- A hard cap of 48 distinct non-cached image loads, including running and queued loads.
- Stale-result and overflow handling that leaves the image placeholder intact.
- Focused unit/contract tests, Android/mobile builds, and performance counters needed for verification.

### Excluded

- Lazy `ContentControl`/active-page-host conversion.
- Desktop `RemoteImage` changes.
- Search ranking and collection-diff changes.
- Navigation collection-reset ownership changes.
- TMDB, SQLite, EPG, or player scheduling changes.
- Bitmap cache ownership/disposal redesign.
- Decode-size profile changes and visual fade removal.

## Resume and Layout Design

### Eligibility

`MobileVirtualizingCardGrid` may begin or continue recovery only when all of the following are true:

- it is attached to a visual root;
- `IsEffectivelyVisible` is true;
- the application is in the foreground;
- its recovery generation is still current.

`MobileAppLifecycle` will retain foreground state in addition to publishing lifecycle signals. Android resume marks foreground before notifying the visual tree. Android pause marks background and invalidates outstanding recovery work. A hidden retained grid may still receive the global event in this patch, but its handler must return before setting rebuild state or posting dispatcher work.

### Recovery sequence

One eligible resume creates a new recovery generation. That generation performs at most three attempts separated by 50 ms:

1. Recheck foreground, effective visibility, attachment, and generation.
2. Post one callback at `DispatcherPriority.Loaded`.
3. Recheck eligibility on the UI thread.
4. Invalidate only the grid's measure and arrange state.
5. If width is stable, rebuild the grid rows once and complete the generation.
6. If width is unstable, allow the next bounded attempt.

The parent traversal in `InvalidateLayoutChain()` will be removed. A size change after the bounded attempts still uses the existing `SizeChanged` path, so recovery does not need an unbounded timer.

Detaching, hiding, or backgrounding the grid invalidates the recovery generation. A stale callback must return without rebuilding rows.

### Resume invariants

- An inactive grid produces zero repair callbacks and zero row rebuilds.
- One grid has at most one current recovery generation.
- One generation posts at most three callbacks.
- Recovery never invalidates a parent control.
- A normal source reset or width change continues to use the existing rebuild path.

## Image Request Coordinator Design

### Responsibilities

Introduce a focused internal coordinator for non-cached image loads. It owns:

- same-cache-key request deduplication;
- an underlying `CancellationTokenSource` per distinct load;
- consumer reference counting;
- the 48-load admission limit;
- removal of completed, failed, or cancelled entries;
- counters for running, queued, consumer-cancelled, underlying-cancelled, and overflow-rejected work.

The decoded bitmap LRU cache remains the first lookup. Cache hits do not enter the coordinator and do not consume capacity.

### Consumer and shared-load lifetime

For a cache miss, `RemoteImage` acquires a consumer lease for the cache key and awaits the shared task with its own token.

- A second consumer for the same key joins the existing entry and does not consume another distinct-load slot.
- Cancelling one consumer releases only that lease. Other consumers continue to await the shared load.
- Releasing the last consumer cancels the entry's underlying token.
- Completion, failure, or underlying cancellation removes the entry exactly once and releases its admission slot.
- A detached or recycled control cannot apply a result because the control token and current normalized URL are checked again on the UI thread.

The coordinator will not expose bitmaps or Avalonia controls in its policy surface. Its concurrency/lifetime behavior will therefore be unit-testable with a deterministic fake loader.

### Bounded backlog

At most 48 distinct cache-miss entries may be running or queued. Joining an existing key remains allowed at capacity. A new distinct key at capacity is rejected immediately and leaves the control on its placeholder; it does not create another background task.

Overflow is a safety condition, not the normal scrolling path. Real card detachment cancellation should release old entries quickly. A rejected control may retry once after 100 ms only if it is still attached, effectively visible, has the same URL, and its consumer generation is current. The retry uses the same admission rule and cannot loop.

This policy prefers stability and a temporary placeholder over unbounded memory/network pressure.

### Cancellation propagation

The shared entry token must flow through:

```text
coordinator entry
  -> download gate WaitAsync(token)
  -> HttpClient.SendAsync(..., token)
  -> response.Content.ReadAsStreamAsync(token)
  -> stream.CopyToAsync(..., token)
  -> decode gate WaitAsync(token)
  -> cancellation check immediately before decode
  -> cancellation check immediately after decode and before cache commit
```

`Bitmap.DecodeToWidth` is synchronous and cannot be interrupted once entered. Cancellation therefore prevents decode admission and prevents a decoded stale bitmap from entering the cache, but it cannot abort native decode in the middle.

HTTP retry delays also receive the shared token. `OperationCanceledException` caused by an entry token is expected control flow and must not mark the URL as failed.

### Cache and UI commit rules

- A bitmap enters the cache only when the shared entry is current and not cancelled after decode.
- Failed HTTP/decode results keep the existing cooldown behavior.
- Consumer cancellation is not a network failure and does not populate the failure cooldown dictionary.
- UI assignment uses `DispatcherPriority.Loaded`, not `Render`.
- UI assignment requires a current control generation, matching normalized URL, attachment, effective visibility, and a non-cancelled consumer token.
- Existing 180 ms opacity behavior and the 64 MiB decoded cache budget remain unchanged in this patch.

## Error Handling

- Lifecycle races, consumer cancellation, and underlying last-consumer cancellation are silent expected paths.
- Invalid URLs and permanent HTTP failures retain the current placeholder/cooldown behavior.
- Coordinator entry cleanup is placed in one `finally`/completion path so admission slots cannot leak.
- An unexpected loader exception completes all current consumers with a null result, removes the entry, and records the normal failure cooldown once.
- Retry and overflow paths are bounded; no timer or recursive retry can continue after detach, navigation, or backgrounding.

## Telemetry

The patch will expose internal counters through the existing performance tracing mechanism or debug diagnostics:

- `GridResumeRequested`
- `GridResumeSkippedInactive`
- `GridResumeAttempted`
- `GridResumeCompleted`
- `GridResumeGenerationCancelled`
- `ImageDistinctActive`
- `ImageDistinctQueued`
- `ImageConsumerCancelled`
- `ImageUnderlyingCancelled`
- `ImageOverflowRejected`
- `ImageStaleCommitDropped`

Counters must not allocate per frame or emit one log line per image in release builds.

## Test-Driven Implementation Sequence

Production code changes are prohibited until the corresponding focused test is observed failing for the expected reason.

### 1. Resume eligibility and bounded recovery

Add tests that fail against the current source/policy and prove:

- hidden or background grids cannot start recovery;
- detach/background invalidates a generation;
- at most three attempts are allowed;
- recovery uses `Loaded`, not `Render`;
- parent-chain invalidation is absent.

Pure lifecycle/recovery decisions will live in a small policy component where practical. Avalonia-specific wiring will also have narrow source-contract tests, matching the repository's existing mobile regression-test style.

### 2. Image consumer lifetime

Add deterministic coordinator tests that prove:

- two consumers of one key execute one loader;
- cancelling one of two consumers does not cancel the loader;
- cancelling the last consumer cancels the loader token;
- completion removes the entry and releases capacity;
- cancellation does not become a failure-cooldown result.

### 3. Bounded admission

Add tests that fill 48 distinct entries and prove:

- the forty-ninth distinct entry is rejected;
- joining an existing key at capacity succeeds;
- cancelling/completing an entry admits a later distinct key;
- retry is performed at most once and only for a current visible control.

### 4. End-to-end token and commit guards

Add contract/behavior tests proving token propagation to the download gate, HTTP send, stream copy, retry delay, and decode gate, plus cancellation checks before cache and UI commits.

Then implement only enough production behavior to pass each red test before proceeding to the next group.

## Verification

Automated verification:

1. Run each new focused test red, then green.
2. Run all performance/lifecycle/image focused tests.
3. Build `Noctra.Mobile` and `Noctra.Android`.
4. Run the full `Noctra.Tests` suite and compare results with the six-failure baseline.
5. Run `git diff --check` and confirm the user's untracked consolidated report remains untouched.

Android device verification:

1. Navigate `Live -> Movies -> Series -> Search -> Live` at least 50 times.
2. Fast-scroll at least 500 items in Movies and Series, then reverse direction.
3. Background and foreground the app 20 times after the scroll/navigation sequence.
4. Confirm inactive-grid recovery remains zero.
5. Confirm distinct image work returns toward viewport scale shortly after scrolling stops and never exceeds 48.
6. Confirm there is no crash, ANR, permanently blank active grid, or stale image assignment.
7. Compare frame-time/skipped-frame and managed/native-memory trends with the pre-patch run on the same device and playlist.

## Completion Criteria

This first patch is complete only when:

1. Every new regression test was observed failing before its production change and passes afterward.
2. Mobile and Android builds succeed.
3. The full suite introduces no new failure beyond the recorded six-failure baseline.
4. Inactive-grid resume recovery is zero in the torture scenario.
5. One active grid performs no more than three recovery attempts per resume.
6. Image distinct work never exceeds 48, same-key deduplication remains intact, and last-consumer cancellation reaches the underlying loader.
7. Scroll position, card interaction, paging order, image placeholders, and navigation behavior do not regress.
8. Device testing shows the post-scroll/post-resume workload drains instead of increasing across cycles.

## Follow-up Order

After this patch is verified, separate designs/patches will address:

1. navigation collection reset ownership and active-surface lifecycle;
2. TMDB global concurrency and navigation/profile generation cancellation;
3. cancellable single-pass Search ranking and identity-aware result updates;
4. SQLite/EPG scheduling and foreground-aware refresh;
5. image decode profiles, native bitmap ownership, and Android trim-memory handling;
6. lazy active-page hosting only if retained-view telemetry still justifies the architectural change.
