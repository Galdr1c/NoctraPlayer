# Provider-Neutral Browse and Series Detail Performance Design

## Purpose

Improve the post-import browsing path for Xtream, Stalker Portal, and M3U URL profiles without changing provider semantics. The profile shell and content-detail shell must become visible independently of provider calls, SQLite maintenance, metadata enrichment, or poster decoding.

The measured baseline on Huawei DBY-W09 is:

- category sheet: about 2.1 seconds and 45 skipped frames;
- Series `All`: about 2.2 seconds and 82 skipped frames;
- uncached Xtream Series detail: about 2.1 seconds before the first detail frame;
- longest observed main-thread blockage: about 683 ms;
- process total PSS after browsing and paging: 870,671 KB;
- native heap PSS after browsing and paging: 425,936 KB.

The design applies to all three providers. Provider-specific behavior is limited to the background detail loader:

- Xtream calls `get_series_info` and persists authoritative seasons and episodes.
- Stalker calls the portal series-info endpoint and persists authoritative seasons and episodes.
- M3U uses locally grouped episodes first, then optionally enriches metadata through TMDB.

## Options Considered

### Option A: only open Series detail before the provider request

Set `IsSeriesDetailVisible` before `LoadSeriesWithProfileProgressAsync` and leave every other path unchanged.

Advantages: smallest diff and immediate improvement to perceived Series-detail latency.

Disadvantages: stale tasks can overwrite a newer selection, provider errors have weak UI state, category and `All` stalls remain, and non-virtualized cards still drive native-memory growth.

### Option B: staged provider-neutral read-path optimization (recommended)

Introduce a common cancellable detail-load state machine, show the shell immediately, virtualize the simple vertical lists, retain paging, and remove full-cache auxiliary recomputation from the first-page barrier. Optimize the Series `All` source without changing provider import formats.

Advantages: addresses every measured post-import bottleneck while preserving existing provider services and database schema. Each stage can be tested and measured independently.

Disadvantages: touches ViewModel state, mobile XAML, and cache-refresh coordination. Grid virtualization requires a row adapter because the current responsive `WrapPanel` is not virtualized.

### Option C: replace the entire browse layer with a new repository/reactive paging architecture

Move all content screens to provider-independent query objects, keyed async caches, and a new virtualized grid control.

Advantages: clean long-term architecture and strong control over memory.

Disadvantages: too large for the current performance patch, high regression risk for playback and navigation, and slower to validate on the physical device.

Option B is selected. It delivers the measured improvements incrementally and does not couple UI responsiveness to a provider rewrite.

## Architecture

### 1. Immediate Series-detail shell

Selecting a `Series` performs only synchronous, bounded state setup before the first UI frame:

1. Increment the existing media-selection generation and cancel the previous detail load.
2. Assign the lightweight selected Series object already present in the card collection.
3. Clear old season/episode continuation state.
4. Set a detail-specific loading state.
5. Make the detail view visible and raise `OnMediaSelected`.
6. Yield control so Avalonia can render the shell.
7. Start the provider-neutral background loader.

The shell displays known title, poster, group, and locally available metadata immediately. Play/continue actions remain hidden until a playable episode exists.

The loader returns a complete snapshot or applies a snapshot on the UI dispatcher. It must not mutate the selected UI object from a worker thread.

### 2. Cancellation and stale-result protection

The detail load uses a linked cancellation token owned by the current profile/media-selection scope. It is cancelled when:

- another media item is selected;
- Series detail is closed;
- the user changes profile;
- navigation leaves the Series detail context;
- the application scope is disposed.

Both cancellation and generation equality are checked before UI publication and before starting nonessential persistence work. A completed request for Series A can never overwrite Series B.

Provider services receive the token where their current contracts support it. Where a provider service cannot cancel an in-flight HTTP operation yet, the result is discarded by generation and the contract is extended in a test-first follow-up.

### 3. Provider adapters

The existing provider-specific methods remain authoritative. The patch changes orchestration rather than provider parsing:

- **Xtream:** resolve the provider series ID, fetch the series detail, create normalized seasons/episodes, publish the snapshot, and persist after publication when safe.
- **Stalker:** resolve the Stalker series ID, fetch portal detail, create normalized seasons/episodes, publish the snapshot, and persist after publication when safe.
- **M3U URL/local M3U:** load grouped local episodes from SQLite first. Publish playable local seasons immediately. TMDB search/detail/season metadata is enrichment and must not delay the playable detail state.

Errors produce a nonblocking detail error/empty state and retain the shell. Closing and reopening retries according to the existing metadata timestamps; no global error dialog is used for a single failed detail request.

### 4. Detail rendering and episode virtualization

The detail backdrop blur is not part of the first-frame barrier. The initial shell renders a solid/gradient background and poster. The blurred backdrop is enabled only after the first detail frame and only when a decoded image is available.

The vertical episode `ItemsControl` becomes a virtualizing selector/list with a `VirtualizingStackPanel`. Only visible episode cards and a small cache are realized. Season tabs remain a horizontal list because season counts are small; changing season replaces the virtualized episode source in one notification.

### 5. Category sheet

The category sheet keeps one stable view instance. Opening it must not clear and add categories row-by-row on the UI thread.

- Build an immutable category snapshot from existing group names.
- Replace the collection once, or use the existing batch collection notification.
- Render categories with a vertically virtualized list.
- Preserve selected/hidden state by key rather than recreating commands and closures for every open.

The sheet shell should be visible before category rows finish updating. Existing hide/show behavior remains unchanged for every provider.

### 6. `All`, category selection, and auxiliary caches

Live and Movies continue to use indexed SQLite paging. The first-page operation ends after the visible page is committed to the UI. `My List`, favorites, history, search buckets, EPG, and poster enrichment run as separately coalesced work and cannot hold the loading overlay open.

Series keeps a provider-neutral lightweight catalog, but does not re-sort all 5,157+ entries for every `All` or category click. A filter/sort key identifies a cached ordered source. The source is invalidated only when the playlist generation, hidden groups, search query, or sort order changes. Paging takes slices from that source.

Auxiliary cache refreshes are coalesced per profile generation. Multiple page loads during a scroll produce at most one pending refresh, and an old profile generation cannot publish results.

### 7. Card-grid memory

Mobile poster grids are converted from a non-virtualized `WrapPanel` to virtualized rows:

- the ViewModel exposes rows containing the existing responsive column count;
- the outer list uses `VirtualizingStackPanel`;
- each realized row contains only its two or three card controls;
- paging appends rows in one batch;
- recycled rows release `RemoteImage` subscriptions and decoded-image references.

This preserves the current card appearance and scroll paging while bounding the number of realized cards. Desktop grids are not changed in the first mobile patch unless a shared component requires it.

## Data and State Flow

```text
card tap
  -> cancel previous detail scope
  -> publish lightweight detail shell
  -> first UI frame
  -> provider/local loader
       -> playable season/episode snapshot
       -> UI publication if generation is current
       -> optional metadata enrichment
       -> throttled persistence
```

Category and content navigation follow the same rule: publish the shell and visible page first; schedule derived caches, images, EPG, and metadata afterward.

## Testing Strategy

Production changes follow red-green-refactor. Required tests:

1. Selecting a Series makes the detail shell visible before a blocked loader completes.
2. Play/continue actions remain unavailable until a playable episode snapshot is published.
3. A stale Series A result cannot overwrite a later Series B selection.
4. Closing detail or switching profile cancels/discards the old result.
5. Xtream, Stalker, and M3U select the correct loader behavior.
6. M3U local episodes publish before TMDB enrichment completes.
7. Provider failure leaves the shell responsive and exposes retry/empty state.
8. Category replacement produces one batch notification and preserves selected/hidden keys.
9. Series ordered-source cache invalidates only for relevant filter/sort/profile changes.
10. Auxiliary cache refreshes are coalesced and generation-safe.
11. Mobile XAML contracts use a virtualized vertical list for category and episodes.
12. Virtualized card rows preserve order and page boundaries for two- and three-column layouts.

Existing Series parsing, playback URL, import recovery, category hiding, and navigation tests must remain green.

## Device Verification

Run the patched debug build against the same real Xtream profile on Huawei DBY-W09, then repeat with synthetic/available M3U and Stalker fixtures.

Acceptance targets for the measured read path:

- card tap to Series detail shell frame: at most 500 ms;
- category-sheet shell frame: at most 500 ms;
- first visible page after `All` or navigation: at most 1 second;
- longest main-thread blockage in these interactions: below 100 ms;
- no stale detail publication after selection/profile change;
- paging frame interval p95 within the device frame budget and no Choreographer skipped-frame warning attributable to page insertion;
- total/native PSS must plateau during repeated paging instead of growing with every realized card.

Provider network time is reported separately and is not part of shell-frame acceptance. Playable episode availability is reported separately for Xtream, Stalker, and M3U.

## Rollout Order

1. Immediate Series-detail shell, cancellation, and provider-neutral load state.
2. Episode and category vertical-list virtualization plus batched category replacement.
3. First-page barrier cleanup and coalesced auxiliary caches.
4. Series ordered-source cache for `All` and category changes.
5. Mobile virtualized card rows and image-release verification.
6. Physical-device before/after benchmark.

Each stage must pass its focused tests and the full test suite before the next stage starts. If a stage does not improve its measured metric, stop and re-investigate rather than stacking additional changes.

## Non-Goals

- Replacing SQLite.
- Rewriting Xtream, Stalker, or M3U import pipelines.
- Changing provider-specific episode URL formats.
- Starting poster, EPG, or TMDB enrichment before the visible shell/page.
- Physically filling the user device to test low-disk behavior.
- Redesigning the current visual language.
