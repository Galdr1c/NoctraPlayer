# Mobile Sectioned Feed Virtualization Design

Date: 2026-07-17  
Status: Proposed for written-spec review

## Goal

Extend the mobile card virtualization introduced in `43d99a631b7aad8dfc011a74be0a67aee41c2138` and refined in `b429ddeda64f27270fad58f3672ef38182a9cbb5` to the remaining high-card-count supplementary views without changing their visible section order or card actions.

This first subproject covers My List, Favorites, History, and Search. Continue Watching remains unchanged because the view model deliberately caps it at ten cards. Downloads is a separate follow-up because it mixes completed-media cards, storage summaries, active downloads, queued downloads, and different item templates in one screen.

## Current-state findings

- Live, Movies, and Series use `MobileVirtualizingCardGrid` and own their vertical scroll viewport.
- Category selection and Series episodes already use `VirtualizingStackPanel`.
- My List, Favorites, and History each place three `ItemsControl + WrapPanel` sections inside one outer `ScrollViewer`. Every loaded card remains realized.
- Search places six `ItemsControl + WrapPanel` sections inside one outer `ScrollViewer`. Primary results can contain up to 96 items per media type and similar results up to 18 per type.
- History already loads database pages of 30, but each new page leaves all earlier card controls realized.
- My List and Favorites currently load their saved rows in full. This design bounds UI realization first; saved-list database paging is deliberately a separate measured data-pipeline change so command, count, and empty-state semantics are not partially rewritten.
- The existing My List/Favorites scroll handlers call general channel/series paging, but those targets do not page the saved-list collections and therefore provide no saved-list paging behavior.

## Considered approaches

### A. Replace every `ItemsControl` with `MobileVirtualizingCardGrid`

Rejected. Multiple self-scrolling grids inside an outer `ScrollViewer` receive effectively unbounded height and either realize too much content or create nested-scroll conflicts.

### B. Give every section an independent scrolling grid

Rejected. It preserves virtualization but creates three to six competing vertical scroll areas on one mobile screen and changes the current interaction model.

### C. One flattened, section-aware virtualized feed

Selected. One control owns the vertical viewport. Section headers and card rows are flattened into one virtualizing list, so only visible rows are realized while the page still appears as one continuous scroll surface.

## Architecture

### `MobileCardSection`

An Avalonia object declared directly in view XAML. Each section supplies:

- localized header text;
- `MobileCardGridKind` (`Live`, `Vod`, or `Series`);
- an observable source collection;
- optional visibility behavior for empty sections.

Views declare sections without code-behind collection adapters:

```xml
<controls:MobileSectionedCardFeed>
  <controls:MobileSectionedCardFeed.Sections>
    <controls:MobileCardSection Header="..."
                                CardKind="Live"
                                SourceItems="{Binding ...}" />
  </controls:MobileSectionedCardFeed.Sections>
</controls:MobileSectionedCardFeed>
```

### Pure section-row projection

A UI-independent projection component owns the flattened row sequence. Its output has two row kinds:

- section header row;
- card row containing the small number of cards that fit the current width and card kind.

The projection subscribes to range-add notifications. A contiguous append updates only the affected section's incomplete tail row and adds newly required rows. Existing rows in other sections retain identity. Reset, removal, replacement, section-order changes, or column-count changes use a deterministic rebuild.

This component is tested without Avalonia controls using real row identity and 10,000-item synthetic sections.

### `MobileSectionedCardFeed`

The visual control is a single `ListBox` with one `VirtualizingStackPanel` and transparent, non-selecting containers. It:

- owns the only vertical `ScrollViewer`;
- renders header rows and heterogeneous Live/VOD/Series card rows;
- reuses the existing mobile card controls and responsive card metrics;
- clears and suppresses transient ListBox selection, preserving the transparent touch behavior;
- exposes one `ScrollChanged` event for History paging;
- observes section sources and queues UI projection work at loaded/background priority.

The reusable card-row renderer will be extracted from `MobileVirtualizingCardGrid`; both controls will use the same renderer so card sizing, context menus, touch behavior, and bitmap loading do not diverge.

## View migrations

### My List and Favorites

- Keep the existing three section collections and visible ordering: Live, Series, Movies/VOD.
- Replace the outer `ScrollViewer`, `StackPanel`, and three `ItemsControl` grids with one sectioned feed.
- Remove the ineffective general channel/series paging scroll callback.
- Preserve the existing add/remove commands, empty state, headings, and localized copy.

### History

- Use the same Live, Series, and VOD section collections.
- Keep the existing 30-row database paging pipeline.
- Route the sectioned feed's single scroll event to `LoadMoreHistoryIfNeededAsync`.
- Change history bucket updates for appended pages to range-add where possible; a refresh or retention cleanup still performs a reset.
- A stale or duplicate page must not produce duplicate rows.

### Search

- Keep the search input fixed above the scrolling feed.
- Flatten the three primary-result sections and three similar-result sections into one feed.
- Similar headers remain hidden when their source is empty.
- Preserve the current 96-per-type primary cap and 18-per-type similar cap.
- Continue using existing search paging and generation cancellation; this subproject changes realization, not search ranking.

### Continue Watching

No change. The view model ends the combined rail with `.Take(10)`, so virtualization would add complexity without meaningful memory or frame benefit.

## Data flow

```text
ViewModel BatchObservableCollection range/reset
  -> MobileCardSection observes source
  -> section-row projection updates flattened rows
  -> one ListBox / VirtualizingStackPanel realizes visible rows
  -> shared card-row renderer realizes only visible cards
```

History paging remains:

```text
feed scroll threshold
  -> LoadMoreHistoryIfNeededAsync
  -> SQLite page of 30
  -> history buckets range-add
  -> only affected feed rows append
```

## Error handling and compatibility

- Empty sources do not render orphan section headers.
- Collection mutations received off the UI thread are queued before changing visual rows.
- Unsupported collection actions fall back to a full projection rebuild.
- Source replacement unsubscribes the old collection before subscribing to the new one.
- Width and theme changes preserve content and rebuild only row grouping/visuals as required.
- No provider-specific logic is introduced; the change applies equally to Xtream, Stalker, and M3U-backed content already present in these collections.
- Existing working-tree performance hardening changes and user-owned edits remain intact.

## Test strategy

Test-first implementation will cover:

1. Two or more sections flatten in stable header/row order.
2. Empty sections emit no header.
3. A range append changes only the affected section tail and preserves unrelated row identity.
4. A 10,000-item synthetic section remains incremental with no full rebuild after initial setup.
5. Reset/removal and column-count changes rebuild correctly.
6. The feed has one virtualizing scroll owner and disables selection.
7. My List, Favorites, History, and Search no longer contain card-grid `ItemsControl + WrapPanel` combinations.
8. History still requests pages of 30 and appends without duplicates.
9. Continue Watching remains capped at ten and is not migrated.

## Verification

- Focused tests must be observed failing before production changes and passing afterward.
- Full `Noctra.Tests` suite must pass.
- `Noctra.Mobile` must build with zero errors and zero new warnings.
- `Noctra.Android` must build with zero errors.
- Huawei DBY-W09 real-data smoke tests must cover My List, Favorites, History, Search, long scrolling, context menus, and rapid navigation away during paging.
- Logcat must contain no crash, ANR, stale-page exception, or new skipped-frame burst attributable to these views.
- Realized row/card counts must remain bounded while scrolling a synthetic 10,000-item section.

## Follow-up boundary

After this UI virtualization is measured, a separate design will address:

- independent My List/Favorites database paging and counts;
- Downloads as a heterogeneous sectioned screen;
- provider-independent saved-content query APIs if measurement shows full saved-list loading is a material startup or memory cost.
