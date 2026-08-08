# Mobile Series Detail Episode Virtualization

## Goal

Keep the existing mobile series-detail header, season tabs, episode row layout,
play actions, and download actions while preventing a long season from realizing
every episode at once. The episode list must own a bounded viewport so Avalonia
can measure and recycle only the visible rows plus a small cache.

## Scope

- Change the mobile series-detail view and its focused regression test, plus the
  desktop series-detail episode list so the same performance guarantee applies
  on both UI surfaces.
- Preserve the outer detail-page scroll for the header, metadata, season tabs,
  and content above the episode list.
- Keep the existing `ListBox`/`VirtualizingStackPanel` episode implementation;
  do not replace it with a new card-grid control.
- Keep current row commands, transient-selection clearing, slide transition,
  poster loading, and download feedback unchanged.
- Keep desktop episode-row commands, hover/pressed visuals, and downloaded-mode
  delete behavior unchanged.

## Layout and scrolling

`EpisodeListBox` will be measured inside a bounded viewport using the existing
`VirtualizingStackPanel`. It will have a conservative mobile maximum height,
vertical scrolling enabled, and scroll chaining enabled so a gesture at the
inner list's boundary can continue through the outer page without measuring the
episode list with infinite height.

The season selector remains a horizontal, non-virtualized list because the
number of seasons is small and its current custom NavPill selected-state theme
must remain intact. The outer page retains inertia and chaining for the header
and surrounding content.

The desktop `MainWindow` series-detail episode list will use the same bounded
virtualized `ListBox` pattern. Its existing row template and command bindings
are preserved; only the unbounded `ItemsControl` host is replaced.

## Interaction and lifecycle

- Existing episode-row click/play/download behavior remains unchanged.
- The inner list may scroll independently when the pointer is over episodes;
  at its boundaries, the page remains available through scroll chaining without
  realizing the entire season.
- Changing `SelectedSeason` keeps the existing slide transition and resets the
  list's transient selection; it must not rebuild unrelated page content.
- No new asynchronous loading or data-source behavior is introduced.

## Validation

Add/update focused regression assertions that:

1. `EpisodeListBox` exists and uses `VirtualizingStackPanel`.
2. The episode list declares a bounded viewport (`MaxHeight`).
3. Vertical scrolling and scroll chaining are explicitly enabled for that
   viewport.
4. The view does not regress to an `ItemsControl` over
   `SelectedSeason.Episodes`.
5. The desktop series-detail view has the same bounded virtualized host and does
   not retain an `ItemsControl` over `SelectedSeason.Episodes`.

Run the focused mobile regression test first (expected red before the XAML
  change), then the full `Noctra.Tests` suite and Android Debug build.

## Non-goals

- No changes to Live/Movies/Series card-grid virtualization beyond sharing the
  same bounded-list principle.
- No changes to episode data queries, paging, imports, posters, or downloads.
- No framework upgrade or player changes.
