# Mobile Scroll Smoothness Design

## Problem

On the Huawei DBY-W09 (120 Hz), category scrolling and the Live, Movies, and
Series card grids do not remain consistently smooth. The first card-grid paging
transition is the worst case.

## Evidence

- Android reported 71-101 skipped frames during the first card-grid paging
  transition.
- Once the same pages were already loaded, Live scrolling no longer produced
  comparable main-thread stalls.
- The category list produced no large Choreographer stall, but SurfaceFlinger
  measurements showed a 16.7 ms p95 presentation interval and 18 missed
  8.3 ms frame budgets in a 126-frame sample.
- `MobileVirtualizingCardGrid` recycles row containers, but every recycled row
  clears and recreates all card controls.
- A 30-item paging append raises a collection reset, after which the grid
  rebuilds and resets every row, including rows that did not change.

## Chosen Design

### Incremental row updates

Keep a snapshot of source item references in `MobileVirtualizingCardGrid`.
When a collection reset represents an append-only change and the column count
is unchanged, update only the previously incomplete final row and append the
new rows. Use a full rebuild only for filtering, sorting, replacement, layout
column changes, or card-kind changes.

### Reusable card slots

Each realized row owns a fixed set of card controls. Recycling a row updates
the controls' `DataContext`, size, margin, and visibility instead of clearing
and recreating the XAML trees. Slots are rebuilt only if the column count or
card kind changes.

### Category look-ahead

Give the category `VirtualizingStackPanel` a bounded cache ahead of and behind
the viewport. This moves a small amount of row construction outside the active
gesture without materializing the complete category list.

### Image work

Do not change image animation or caching speculatively. Re-measure after the
structural fixes. Change image behavior only if the device trace still shows
scroll-time stalls attributable to image realization.

## Correctness

- Card tap, long-press menus, favorites, and transparent row selection must
  continue to work.
- Paging must preserve item order and scroll position.
- Filtering, sorting, category changes, and column changes must still force a
  correct full rebuild.
- The implementation remains provider-neutral because all providers use the
  same mobile views.

## Verification

- Add regression tests before production changes.
- Build the mobile and Android projects.
- Install the Debug APK on the connected Huawei tablet without clearing data.
- Measure first paging and steady-state scroll on Live, Movies, Series, and the
  category list using Choreographer logs and SurfaceFlinger latency.
- Run focused regressions and the complete test suite.

## Success Criteria

- No multi-hundred-millisecond main-thread block caused by card-grid paging.
- First paging must not produce the prior 71-101 skipped-frame event.
- Category-list p95 should improve from 16.7 ms toward the 8.3 ms display
  budget without realizing the full list.
- No regression in card interaction, paging, selection visuals, or content
  ordering.
