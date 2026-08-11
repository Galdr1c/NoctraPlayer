# NoctraPlayer Navigation Single-Reset Design

Date: 2026-08-11  
Status: Implemented, reviewed, and Android-accepted on 2026-08-11  
Source finding: `P0-06` in `docs/NoctraPlayer_Performance_Stability_Full_Report.md`

## Goal

Eliminate duplicate collection resets during mobile content navigation without changing the
visible navigation behavior. A `Live`, `Movies`, or `Series` transition must immediately show
the existing loading/empty surface, preserve the bound collection object, and publish at most
one reset for the target content lane before the first incremental page is added.

## Confirmed Root Cause

The current flow has two reset owners:

1. `PrepareContentSurfaceForNavigation` begins a content generation and calls
   `ResetIncrementalState` or `ResetSeriesIncrementalState`.
2. The same preparation method then assigns another new `FilteredChannels` or
   `SeriesViewItems` instance.
3. `ScheduleImmediateFilter` starts the coalesced filter flow.
4. `ApplyFiltersAsync` calls the reset method again before loading the first page.

For a single navigation this can replace one visible collection more than once and can also
raise multiple reset/property-change waves. Avalonia then reevaluates the item presenter,
virtualized rows, recycled cards, and bindings even though only one target dataset is needed.

## Chosen Approach

Use one in-place reset prepared by navigation and consumed by the matching filter pass.

- The `BatchObservableCollection` instance remains stable.
- Navigation clears the target lane once so stale cards cannot flash on the new page.
- Navigation records which target view owns that prepared reset.
- The first current filter pass for that view reuses the prepared state and does not clear it
  again.
- A same-page filter change has no prepared navigation reset, so `ApplyFiltersAsync` performs
  one in-place reset itself.
- A superseded generation cannot consume or commit work for a newer view.

This keeps the current immediate-loading behavior while removing collection replacement and
duplicate reset ownership.

## Alternatives Considered

### Filter-only reset

Removing all navigation preparation would leave a short interval before the coalesced filter
runs. During that interval a newly constructed page could bind to the previous destination's
items. The implementation would be smaller, but the stale-content flash is not acceptable.

### Atomic replacement after the first query

Keeping the previous items until a complete first page is available can avoid an empty frame,
but the new destination may temporarily display the wrong media type. It also retains both
datasets during the transition and expands the change into query/commit orchestration. That is
outside P0-06.

## State and Ownership

`MainViewModel` will retain one small piece of navigation-reset state identifying the prepared
destination. It must contain only value state; it must not retain views or controls.

The state has these rules:

1. `PrepareContentSurfaceForNavigation(view)` supersedes incremental content work.
2. It resets paging flags and clears the target collection in place.
3. It records `view` as the prepared reset owner and marks navigation content reset pending.
4. `ApplyFiltersAsync` snapshots `ActiveView` and checks whether the pending prepared owner
   matches it.
5. A matching pass skips its normal target-lane reset and proceeds to incremental loading.
6. A non-matching or ordinary filter pass performs one in-place target-lane reset.
7. Completion, cancellation, preparation of a newer destination, or a non-content navigation
   clears or replaces the prepared-owner state deterministically.

The pending state is not a reusable cache. It authorizes only the filter pass for the currently
prepared destination.

## Collection Mutation Rules

### Channel lane

`ResetIncrementalState` continues to reset `_currentPage`, `_hasMoreChannels`, and
`_isLoadingMoreChannels`, but it clears `Channels` and `FilteredChannels` in place instead of
assigning new collections. If both properties ever reference the same collection, it is cleared
only once.

The existing dummy-channel count predicate is preserved because the original
`FilteredChannels` object is preserved.

### Series lane

`ResetSeriesIncrementalState` continues to reset `_currentSeriesPage`,
`_hasMoreSeriesItems`, and `_isLoadingMoreSeriesItems`. It clears `_seriesFilteredSource` and
`SeriesViewItems` in place instead of allocating replacements.

### Inactive lane

Navigation does not reset an unrelated inactive lane merely to hide it. The lazy active-page
host already detaches inactive views. That lane is reset when it next becomes the target or when
an explicit profile/global data reset requires it.

Profile removal, playlist replacement, logout, and other global data-lifetime operations remain
allowed to clear multiple collections. They are not navigation-filter ownership paths and are
outside this change.

## Navigation and Filter Data Flow

```text
Navigate(target)
  -> suppress property-triggered filter requests
  -> PrepareContentSurfaceForNavigation(target)
       -> supersede old content generation
       -> reset target paging state
       -> Clear target collection in place once
       -> record prepared target
  -> set ActiveView
  -> release suppression
  -> ScheduleImmediateFilter once
       -> coalesce/supersede older request
       -> ApplyFiltersAsync
            -> validate current token and playlist
            -> consume matching prepared target
            -> do not reset target again
            -> query first page
            -> generation-check
            -> AddRange to the stable collection
            -> complete pending navigation state
```

For a same-page group, sort, favorite, or type filter change, the flow begins at
`ScheduleImmediateFilter`; because there is no prepared navigation target,
`ApplyFiltersAsync` performs one in-place reset before the query.

## Cancellation and Race Handling

- The existing `_incrementalContentCancellation` remains the authority for data commits.
- A navigation preparation supersedes earlier page loads before clearing the target lane.
- `ApplyFiltersAsync` may consume a prepared reset only after validating its current token,
  playlist, and active destination.
- Rapid `Live -> Movies -> Series` navigation leaves only `Series` authorized to commit.
- A cancelled pass must not consume a prepared reset belonging to a newer navigation.
- `CompleteNavigationContentReset` clears the pending owner only when it still belongs to the
  completing flow; a stale `finally` must not clear newer state.

The final rule requires owner-aware completion rather than an unconditional boolean reset.

## Notifications

One in-place `Clear` produces one `NotifyCollectionChangedAction.Reset` for the target visible
collection. The first page remains an indexed batch `Add` through `AddRange`.

This patch does not remove the existing explicit `OnPropertyChanged(nameof(FilteredChannels))`
after `AddRange`; that broader notification cleanup belongs to report item `P2-01`. P0-06 is
limited to collection replacement and duplicate navigation reset waves.

## Telemetry

Add low-cost numeric `PerformanceTrace` counters for:

- navigation resets prepared;
- prepared resets consumed;
- ordinary filter resets;
- stale reset completions ignored.

The counters must not retain collection, view, playlist, or channel references and must not log
per item.

## Test-Driven Verification

Production changes will follow red-green-refactor in these slices:

1. A navigation test holds the original `FilteredChannels` reference, navigates between
   channel destinations, and proves the reference remains identical.
2. The same test subscribes to `CollectionChanged` and proves one target reset is raised for
   one navigation before the first batch add.
3. A Series equivalent proves stable `SeriesViewItems` identity and one reset.
4. A same-page filter test proves the filter still clears stale items exactly once.
5. A rapid-navigation test proves a stale pass neither adds data nor clears the newer target's
   prepared reset state.
6. Existing incremental cancellation, profile reload, lazy page host, and mobile navigation
   tests remain green.

Behavior tests are preferred. Narrow source-contract assertions may be used only where an
otherwise inaccessible ownership invariant cannot be observed reliably.

## Build and Android Acceptance

Automated acceptance:

1. New P0-06 tests pass after being observed failing for the intended reason.
2. Relevant MainViewModel navigation/cancellation tests pass.
3. `Noctra.Core`, `Noctra.Mobile`, and `Noctra.Android` build with zero new errors.
4. The full test suite introduces no failure beyond the documented baseline.
5. `git diff --check` is clean.

Live Android acceptance, without clearing application data:

1. Install the new APK with `adb install -r`.
2. Run at least 50 `Live -> Movies -> Series -> Live` transitions.
3. Confirm each new destination immediately shows loading/empty state rather than stale cards.
4. Confirm cards load normally and paging continues after the first page.
5. Confirm no ANR, crash, blank page that never recovers, or stale destination commit.
6. Compare collection-reset telemetry with the baseline; one navigation may prepare and consume
   one target reset but may not publish a second target reset in `ApplyFiltersAsync`.

## Non-Goals

- Search fuzzy ranking or search result diffing (`P0-09`, `P0-10`, `P1-02`).
- TMDB queue and generation ownership (`P0-11`, `P0-12`).
- Removing every explicit collection property notification (`P2-01`).
- Replacing incremental `AddRange` with a general identity-aware diff engine.
- Changing sort, group, favorite, pagination, or loading-state UX.

## Completion Criteria

P0-06 is complete when the visible target collection keeps the same object identity, one
navigation produces exactly one target reset, same-page filters remain correct, superseded work
cannot clear or populate the current destination, automated tests/builds pass, and the Android
navigation torture scenario completes without ANR or unrecoverable blank content.
