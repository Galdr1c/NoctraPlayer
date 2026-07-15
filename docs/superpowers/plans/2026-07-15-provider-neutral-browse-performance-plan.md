# Provider-Neutral Browse Performance Implementation Plan

## Phase 1 - Immediate Series detail shell

1. Add failing source-contract tests proving that Series selection publishes `SelectedSeries`, loading state, visibility, and `OnMediaSelected` before awaiting provider work.
2. Add failing tests for cancellation/generation guards on close, navigation, profile change, and a second media selection.
3. Refactor Series selection into bounded shell publication plus an async detail-load continuation.
4. Keep Xtream, Stalker, and M3U parsing authoritative; publish only when the selection generation remains current.
5. Verify focused tests and the complete `Noctra.Tests` suite.

## Phase 2 - Category and episode vertical virtualization

1. Add failing XAML contract tests requiring virtualizing vertical lists for categories and selected-season episodes.
2. Add a failing category-refresh contract test requiring a single batch replacement rather than row-by-row collection churn.
3. Replace the category and episode `ItemsControl` paths with virtualizing `ListBox`/`VirtualizingStackPanel` paths while preserving commands and selected state.
4. Change category refresh to build a snapshot and publish it once.
5. Run focused XAML/category tests and the complete suite.

## Phase 3 - First-page barrier and Series ordered-source cache

1. Add failing tests proving that visible-page publication precedes My List, favorite, history, search, EPG, and poster enrichment work.
2. Add failing tests for coalesced auxiliary refreshes scoped by profile generation.
3. Move auxiliary work outside the loading barrier and coalesce it.
4. Add failing tests for Series ordered-source reuse and invalidation by profile, hidden groups, query, and sort order.
5. Implement the smallest ordered-source cache and verify page ordering/counts.

## Phase 4 - Mobile card-row virtualization

1. Add failing row-adapter tests for stable ordering, two/three-column grouping, final partial rows, and append boundaries.
2. Add failing mobile XAML contract tests requiring an outer virtualized row list.
3. Introduce provider-neutral virtualized row models/adapters for Live, Movies, and Series without changing card controls.
4. Ensure recycled rows release `RemoteImage` bindings/subscriptions.
5. Verify scrolling, selection, context actions, and page append behavior.

## Phase 5 - Build and device verification

1. Run focused tests after every red-green cycle, then the full suite.
2. Build Android Debug and install it over the connected Huawei DBY-W09 profile data.
3. Repeat category sheet, `All`, Live/Movies/Series navigation, paging, and two uncached Series-detail runs.
4. Capture Choreographer/SurfaceFlinger timing, managed/native/graphics memory, and DB/WAL/SHM sizes.
5. Compare against the 2026-07-15 real Xtream baseline and record remaining provider-specific work for Stalker and M3U fixtures.
