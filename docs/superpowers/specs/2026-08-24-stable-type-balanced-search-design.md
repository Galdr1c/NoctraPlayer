# Stable, Type-Balanced Search Design

## Goal

Search must publish relevant Live, Series, and VOD results without making the visible list change merely because the user scrolls. A new query starts at the top and cannot activate stale results while its replacement is loading. Returning from a detail/player screen keeps the existing navigation scroll state.

## Confirmed root causes

1. Search currently reads one shared 30-item `Channel` page ordered Live-first. Large Live result sets therefore delay VOD candidates, while `ChannelType.Series` rows can consume pages even though the channel ranker discards them.
2. Reaching the scroll threshold loads another shared page. The ranking session inserts better candidates into earlier positions and `IdentityCollectionSynchronizer` moves existing items. The ScrollViewer keeps its numeric offset, so the visible item changes.
3. Search also runs the generic Series filtering/paging path even though Search ranking already evaluates the complete `_allSeriesCache` snapshot.
4. A changed committed query does not reset the Search ScrollViewer and old results remain interactive during the debounce/query window.

## Considered approaches

### A. Stable type-balanced snapshot — selected

Fetch bounded Live and VOD candidate sets independently, rank them together with the Series cache, and publish one snapshot. Search scrolling performs no generic content paging.

Advantages:

- eliminates Live/VOD starvation;
- excludes `ChannelType.Series` rows from the channel candidate funnel;
- removes scroll-driven ranking and section growth;
- reuses the current ranking, cancellation, document cache, and virtualization code;
- bounded memory and database work.

Trade-off: the bounded DB candidate query still uses literal SQL filtering. A normalized index/FTS migration remains a separate follow-up.

### B. Keep shared incremental paging and preserve a visual anchor

This reduces visible jumping but retains candidate starvation, discarded Series channel pages, repeated ranking, and deep offset pagination.

### C. Append later pages without reranking

This is simple but progressively corrupts relevance order and still allows Live to delay VOD.

## Architecture

### Candidate retrieval

- `LoadMoreChannelsAsync` recognizes a committed Search request on its first page.
- It issues two bounded `ContentPageRequest`s using the current playlist/query/sort/favorite settings:
  - `Type = Live`
  - `Type = VOD`
- Each type receives up to 192 candidates. This is twice the 96-item primary bucket and leaves room for the 18-item similar bucket without materializing the full playlist.
- Results are combined once and passed to `IncrementalSearchRankingSession`.
- Search marks generic channel paging exhausted after that request. Normal Home/Live/Movies paging remains unchanged.

### Series retrieval

- Search ensures `_allSeriesCache` is available.
- It does not call `UpdateSeriesViewItems` or `LoadMoreSeriesAsync` for the Search view.
- The existing ranking session continues to use a hidden-group-filtered Series snapshot.
- Generic Series paging remains unchanged for Home and Series views.

### Stable presentation

- Mobile and desktop Search feeds no longer call generic scroll paging.
- The single ranking snapshot may reorder results while it is being prepared, before normal interaction resumes; scrolling itself cannot trigger another candidate page.
- Background visual enrichment may update image properties but must not initiate a new candidate ranking page.

### Query transition

- When a normalized committed query differs from the previous committed query, Search requests a scroll reset.
- Mobile and desktop Search views reset their internal ScrollViewer to offset zero.
- Search-to-detail/player-to-Search navigation does not commit a new query, so existing navigation-state restoration remains intact.
- While `IsSearching` is true, result feeds are dimmed and hit testing is disabled. Existing results can remain visible without being actionable.
- Explicit Search uses the existing 75 ms coalescing delay rather than the previous 300 ms debounce.

## Error and cancellation behavior

- Live and VOD reads share the current filter cancellation token.
- A superseding query/playlist/view invalidates both reads and rejects stale commits through the existing generation and owner checks.
- If one candidate query throws, the current filter error path remains responsible for clearing the loading state and surfacing the localized status message; no partial mixed-query snapshot is committed.
- Empty candidate sets still rank Series and produce the existing empty/similar states.

## Tests

1. Search candidate retrieval requests Live and VOD independently, never `Type = null` or `Type = Series`.
2. Search performs one bounded candidate load and reports no generic channel page remaining.
3. Search does not run generic Series paging.
4. A second candidate page cannot be triggered by Search scroll handlers on mobile or desktop.
5. A changed query requests scroll-to-top, while recommitting the same normalized query does not.
6. Search feeds disable hit testing and dim while `IsSearching` is true.
7. Existing cancellation, ranking, normal page loading, navigation restore, desktop, and mobile tests remain green.

## Non-goals

- SQLite FTS5 or a persisted normalized `SearchKey` migration;
- changing the current relevance scoring thresholds/capacities;
- adding type chips, `Show all`, or section preview limits;
- changing Live/Movies/Series page pagination outside Search.

