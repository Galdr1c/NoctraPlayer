# Mobile Performance Hardening Design

Date: 2026-07-17  
Status: Implemented and verified; one measured Live-scroll warning remains documented

## Goal

Harden the mobile browsing changes introduced in `b429ddeda64f27270fad58f3672ef38182a9cbb5` without replacing the current UI architecture. The result must preserve the measured Huawei DBY-W09 scrolling improvement while removing deep-paging quadratic work, making content queries cancellable, fixing My List menu ambiguity, and making logo and bitmap resources deterministic in clean builds.

## Scope

This change includes:

1. Track all four theme-aware mobile logo assets in Git and cache their decoded bitmaps for the process lifetime.
2. Replace reset-based page notifications with one range-add notification and update the virtualized grid incrementally.
3. Ensure My List cards show exactly one My List action: remove inside My List, add elsewhere.
4. Cancel superseded channel page queries through every layer down to EF Core/SQLite.
5. Bound decoded image cache retention by an approximate 64 MiB pixel budget while avoiding unsafe disposal of bitmaps that visible controls may still reference.
6. Add behavioral tests for range append identity, stale/cancelled paging, menu visibility rules, asset tracking, and cache budgeting.

This change does not rewrite published Git history, introduce a generic paging framework, or add reference-counted bitmap leases.

## Design

### 1. Deterministic logo assets and cache

The four assets referenced by `LogoThemeConverter` will be explicitly unignored:

- `Square150x150LogoTPLight.png`
- `Square150x150LogoTPDark.png`
- `Square150x150LogoTPFullLight.png`
- `Square150x150LogoTPFullDark.png`

The converter comments will describe these exact assets. Each variant will be represented by a `Lazy<Bitmap?>`, so each file is opened and decoded at most once per process. Failure remains non-fatal and returns `null`, matching current behavior.

A repository guard test will require all four files to exist and require matching `.gitignore` exception rules. Android verification will inspect the built APK/resource output so a local ignored file cannot mask a clean-checkout omission.

### 2. Range-aware collection notifications

`BatchObservableCollection<T>.AddRange` will continue to mutate the backing collection in one operation, but it will publish one `NotifyCollectionChangedAction.Add` event containing the added list and its starting index. `ReplaceAll` will continue to publish `Reset`.

This preserves one UI notification per page while allowing consumers to distinguish append from rebuild. Existing count-property notifications remain unchanged.

Tests will verify:

- one range-add event is emitted;
- the event contains the correct starting index and items;
- `ReplaceAll` still emits reset;
- predicate-backed `CountedItemCount` stays correct.

### 3. Incremental virtualized grid

`MobileVirtualizingCardGrid` will consume the range-add event directly. For a contiguous append at the tracked source count it will:

1. update only the previous incomplete row, when one exists;
2. add only newly required rows;
3. preserve all earlier row instances;
4. advance a tracked source count without enumerating or comparing the old prefix.

Insertions in the middle, removals, moves, replacements, resets, source changes, card-kind changes, and column-count changes will use a full rebuild.

The row mutation algorithm will be isolated from Avalonia controls in a small internal component. Tests will use real row objects and assert that a 10,000-item sequence appended in pages of 30 preserves earlier row identity and performs no full rebuild after initial setup.

### 4. My List action exclusivity

The generic My List action on VOD and Series cards will be visible only when `ShowRemoveMyListMenu` is false. The explicit remove action will remain visible only when it is true.

The existing commands remain separate:

- normal browse cards execute `AddToMyListCommand`;
- My List cards execute `RemoveFromMyListCommand`.

Tests will verify the visibility policy independently of localized text and cover both VOD and Series card configurations.

### 5. End-to-end content cancellation

`MainViewModel` will own a content-generation `CancellationTokenSource`. Starting a new incremental generation will atomically replace and cancel the previous source. `LoadMoreChannelsAsync` will link the generation token with any caller token.

The effective token will flow through:

```text
MainViewModel
  -> IContentQueryService.GetChannelPageAsync
  -> IPlaylistService.GetChannelsFilteredPageAsync
  -> CreateDbContextAsync(token)
  -> repair query when applicable
  -> ToListAsync(token)
```

Generation checks remain in place as a correctness barrier even when cancellation is delayed or ignored by a provider. Scroll-triggered paging, which currently uses the default token, will automatically inherit the current content-generation token.

Tests will use a cancellable delayed query double to verify that navigation cancels outstanding work and that a cancelled page cannot update page/loading state. Service-level tests will verify token propagation to the query boundary.

### 6. Byte-budgeted bitmap cache

The decoded bitmap cache will retain at most approximately 64 MiB, estimated as:

```text
pixel width * pixel height * 4 bytes
```

The existing entry-count cap remains as a secondary guard. Each cache entry stores the bitmap and estimated decoded byte count. LRU eviction reduces the retained-byte total before a new entry is accepted.

Eviction removes the cache's strong reference but does not immediately call `Dispose()`, because a realized card may still display the bitmap. Once controls and in-flight operations release their references, normal bitmap finalization can reclaim the native resource safely. This design bounds cache retention without introducing lease/reference-count infrastructure.

Tests will cover byte accounting, LRU eviction, replacement/no-duplicate accounting, and oversized single-image behavior using a pure cache-budget policy component rather than network or UI mocks.

## Error handling and compatibility

- Cancellation is expected control flow and must not surface as a user error.
- Generation checks remain the final protection against stale results.
- Asset decode failures remain non-fatal.
- Non-append collection changes deliberately fall back to full rebuild.
- The collection notification change will be validated against all current `BatchObservableCollection.AddRange` call sites through the full test suite and Android smoke testing.
- No published commit is rewritten; fixes are delivered as new working-tree changes.

## Verification results (2026-07-17)

- Focused range, cache, cancellation, service-boundary, and view-model behavior tests pass.
- Full regression suite: **1115 passed, 0 failed, 0 skipped**.
- `Noctra.Mobile`: **0 warnings, 0 errors**.
- `Noctra.Android`: **0 errors**; 63 pre-existing nullable/platform compatibility warnings remain.
- The Android package includes the four theme-aware logo resources, and the latest APK was installed successfully on Huawei DBY-W09 (`5VLBB21A18202444`).
- Real Xtream smoke testing passed profile entry, Home, Live, Movies, Series, category/card rendering, Series detail/episodes, single My List action, and rapid Live → Movies → Series navigation. The final screen contained Series data only and logcat contained no crash, ANR, or stale-request exception. A dedicated behavior test also proves that an old 500 ms throttled import reload cannot cancel the newly selected playlist generation.
- `git diff --check` is clean. Line-ending notices are informational only.
- Movies and Series scrolling produced no skipped-frame warning in the measured runs. Live scrolling improved materially and the warmed SurfaceFlinger sample had 127 frames with zero intervals over 16.7 ms (p95 8.38 ms, max 8.52 ms), but one `Choreographer: Skipped 33 frames` main-looper warning was still observed. Therefore the stronger “no warning in every Live run” target is not claimed as complete.

## Completion criteria

The implementation is complete only when:

1. New focused tests fail before production changes and pass afterward.
2. Existing unit/regression tests pass.
3. Mobile and Android projects build from the checked-in asset set.
4. The built Android package contains all four logo assets.
5. Huawei DBY-W09 smoke tests confirm:
   - profile and Home open;
   - Movies, Series, and category scrolling produce no Choreographer skipped-frame warning; Live retains the explicitly documented residual warning;
   - rapid navigation does not append stale cards;
   - My List VOD and Series cards expose only the remove action.
6. `git diff --check` is clean and unrelated user changes are preserved.
