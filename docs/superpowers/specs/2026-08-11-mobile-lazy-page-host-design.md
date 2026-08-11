# NoctraPlayer Mobile Lazy Active Page Host Design

**Date:** 2026-08-11  
**Status:** Approved by user; ready for implementation  
**Scope:** Android/Avalonia mobile presentation layer, report findings P0-01 and P0-02

## 1. Problem statement

`Noctra.Mobile/Views/MainView.axaml` currently constructs Home, Live, Movies, Series,
Search, Favorites, MyList, History, Downloads, Settings, and Series Detail in the same
`Grid`. Navigation changes `IsVisible`, but the hidden view instances, their bindings,
collections, image controls, and some event subscriptions remain alive.

This produces two coupled problems documented as P0-01 and P0-02:

- every heavy page contributes to the retained visual/object graph after it has been
  visited;
- hidden pages can continue reacting to collection, layout, lifecycle, or image events.

The Phase 1 lifecycle, image-load, and resume recovery work bounds some of that activity,
but it does not remove the retained page graph. Phase 2 must ensure that only the page the
user can currently interact with owns a live base-page visual tree.

## 2. Goals

1. Keep at most one active base-page view instance in `CoreContentHost`.
2. Construct heavy pages only when their destination becomes active.
3. Fully disconnect the departing page so it is eligible for collection without forcing a
   GC.
4. Preserve the user's vertical scroll position independently for every core destination.
5. Preserve filter, query, selection, and content data in the existing shared view models;
   do not retain view instances for state preservation.
6. Make Series Detail a lazy overlay that can be opened from any applicable destination,
   including Downloads and Search.
7. Preserve existing back, category selection, profile, player, and Settings-scope behavior.
8. Make rapid navigation, app pause/resume, and asynchronous Settings release generation
   safe.
9. Add deterministic diagnostics and automated/live acceptance evidence.

## 3. Non-goals

- This phase does not implement the report's later Search, TMDB enrichment, image decode,
  cache trim, database, EPG, or card gesture findings.
- It does not change provider data, user data, database schemas, or playlist state.
- It does not introduce a retained LRU page cache.
- It does not force garbage collection.
- It does not redesign desktop navigation. Shared controls may benefit, but Android mobile
  behavior is the acceptance target.
- It does not preserve transient sheets, dialogs, category pickers, toasts, or an open
  Series Detail overlay across destination changes.

## 4. Considered approaches

### 4.1 Single active page plus lightweight state snapshots — selected

Destroy the departing visual tree, store only navigation state, then construct the target
page. This provides the lowest retained memory and directly addresses both P0 findings.

### 4.2 Bounded two-page LRU cache — rejected for this phase

This can make immediate back navigation faster, but retains an additional heavy graph and
makes the active/inactive contract harder to prove. It can be reconsidered only if measured
page construction cost is unacceptable after the single-page model is live-tested.

### 4.3 Lazy-create once, then retain all visited pages — rejected

This improves startup only. After the user visits every destination, the application returns
to the current retained-page condition.

## 5. Host architecture

### 5.1 XAML structure

The eager page declarations inside `CoreContentHost` will be replaced by two empty hosts:

- `ActiveCorePageHost`: contains zero or one base page;
- `SeriesDetailHost`: contains zero or one `MobileSeriesDetailView` while
  `MainViewModel.IsSeriesDetailVisible` is true.

The outer `CoreContentHost` remains the navigation transition and visibility boundary. A
Series Detail overlay is separate because a Series can be selected from more than the Series
destination. While detail is open, the one underlying base page may remain attached because
it must be revealed immediately when the detail closes. Closing or navigating away releases
the detail view.

Neither content host owns the shared core `DataContext`. The committed page and lazy detail
view receive their data contexts explicitly. This prevents a newly inserted or supposedly
cleared view from accidentally inheriting a live VM binding graph from the host.

The invariant is:

```text
base page count: 0 or 1
series detail count: 0 or 1, only while detail is open
retained inactive base page count: 0
```

### 5.2 Explicit page factory

An explicit mobile page factory maps the known core destination strings to constructors:

```text
Home       -> MobileHomeView
Live       -> MobileLiveView
Movies     -> MobileMoviesView
Series     -> MobileSeriesView
Search     -> MobileSearchView
Favorites  -> MobileFavoritesView
MyList     -> MobileMyListView
History    -> MobileHistoryView
Downloads  -> MobileDownloadsView
Settings   -> MobileSettingsView
```

The factory does not use `ViewLocator` reflection because all media destinations share the
same core `MainViewModel` and therefore cannot be resolved reliably from VM type alone. The
factory returns a fresh instance for every completed revisit. Destination strings remain at
the shell boundary to avoid a broad navigation rewrite; the mapping is centralized so they
cannot drift across multiple switches.

### 5.3 Active-page ownership

`MainView` owns one active-page record containing:

- destination;
- page control;
- assigned data context;
- page-specific event detach actions;
- navigation generation.

The record is the single source used by back handling, category event routing, image-load
activation, navigation state capture, and deactivation. Code must not recover the current
page by searching the visual tree or by testing ten named fields.

### 5.4 Page-specific event wiring

Event connections are made only during page activation and removed during deactivation:

- Live, Movies, and Series category selection requests;
- Settings `BackToProfilesRequested`;
- any future page-specific shell callback.

Back handling switches on the active page's actual type and calls its existing
`TryHandleBack` method where supported. No inactive page receives back or category events.

## 6. Navigation state contract

### 6.1 Explicit participant contract

Scrollable pages implement an internal navigation-state participant contract. The contract
captures and restores a small immutable state value containing horizontal and vertical
offsets. Pages with no meaningful scroll state may return the empty/default state.

Each page identifies its primary scroll owner explicitly:

- Live, Movies, Series, and Home use their declared `MobileVirtualizingCardGrid`;
- Search, Favorites, MyList, and History use their declared
  `MobileSectionedCardFeed`;
- Downloads uses its outer page `ScrollViewer`;
- Settings uses `SettingsScrollViewer`.

For templated grid/feed controls, a shared accessor may locate the inner `ScrollViewer`
within that explicitly supplied control. It must not scan the entire page and guess between
nested or modal scroll viewers.

### 6.2 State store

`MainView` owns a destination-keyed state store for the lifetime of the shell. Only small
value records are stored; no page, control, view model, collection, delegate, or cancellation
source may be retained in the store.

Rules:

- first visit starts at offset zero;
- revisit restores the most recently captured position for that destination;
- restore clamps offsets to the current scroll extent;
- a filter or data change that shortens content therefore restores to the nearest valid
  position rather than failing;
- changing profiles clears the state store because the content identity has changed;
- leaving for a temporary player/profile surface captures state before the base page is
  released, so returning can construct the page at the former position.

Shared view-model state continues to own filters, search text, loaded collections, playback
context, and personal-state data. The view snapshot does not duplicate them.

### 6.3 Bounded restore coordinator

Restoration occurs only after the new page is attached and has a valid viewport/extent. It
uses the current navigation generation and at most three background-priority attempts with a
short bounded delay between attempts.

Restore is cancelled when:

- another navigation generation begins;
- the page leaves the host;
- the app is paused;
- the core host is covered or no longer effectively visible.

No unbounded timer and no `DispatcherPriority.Render` loop is permitted. Failure to obtain a
stable extent is non-fatal: the page stays usable at a valid current position and a diagnostic
failure counter is recorded.

Image descendants remain inactive during construction and scroll restoration. They are
activated only after restore succeeds, exhausts its bounded attempts, or determines that no
saved state exists. This avoids downloading and decoding the first viewport immediately
before jumping to the saved viewport.

## 7. Activation and deactivation lifecycle

### 7.1 Same-destination navigation

Selecting the already-active destination closes applicable transient surfaces and refreshes
navigation selection, but does not reconstruct the page or reset its scroll position.

### 7.2 Atomic destination change

For a core destination change:

1. Increment the navigation generation and cancel the previous restore.
2. Hide the overscroll session and close transient card/category sheets.
3. Resolve the target data-context lifetime.
4. Construct the target page while the current page remains available, mark all target image
   descendants inactive, and only then assign the target data context and page callbacks.
5. If preparation fails, dispose any new lifetime, keep the current page/destination, restore
   its image-active state, log the failure, and return.
6. Capture the departing page's navigation state.
7. Mark the departing page inactive and disable descendant image loads.
8. Detach page-specific events, clear its `DataContext`, remove it from the host, and remove
   the last strong shell reference.
9. Attach the prepared target page, assign the committed active-page record, and update shell
   visibility/navigation selection.
10. Start generation-safe scroll restoration.
11. Enable image loading only after restoration reaches a terminal result and only when the
    app, host, and generation are still active.

There may be a very short construction-time overlap, but only the committed target is
attached after the atomic swap. A failed constructor can never leave a blank host.

### 7.3 Settings lifetime

Settings retains its scoped view-model lease semantics:

- entering Settings awaits the serialized release of any older Settings lease;
- a new lease is never created before the old one has flushed and disposed;
- the generation is checked after each await;
- a lease prepared by a superseded navigation is disposed exactly once and never attached;
- leaving Settings synchronously clears its page `DataContext`, removes its callbacks, and
  queues the existing asynchronous release chain.

The old page remains visible while an entry into Settings is waiting for a previous lease, so
the user does not see an empty host.

### 7.4 Startup, hot reload, and shell teardown

`InitializeComponent` creates empty hosts only. Initial Home construction is driven by the
existing `RunStartupFlowAsync` path after the resolver, current profile, database readiness,
and selected destination are known. Completing profile selection also enters Home through the
same navigation path; it does not create a page directly.

Hot reload invalidates the active generation and reconstructs only the current destination,
then reapplies navigation, safe-area, player, and overscroll state. It must not reconstruct
every destination.

`MainView.OnDetachedFromVisualTree` invalidates navigation/restore work, disables image loads,
captures no further state, disconnects and clears the active base page, removes the lazy
Series Detail view, unsubscribes the core VM detail observer, and queues the existing Settings
lease release before calling the base implementation. Cleanup is idempotent because Avalonia
may attach/detach the shell more than once.

### 7.5 Non-core and covering surfaces

Navigating to More or another non-core shell destination captures and releases the active
base page. Showing full profile selection or entering the full player does the same before
covering `CoreContentHost`. Returning reconstructs the current destination and restores its
state. This prevents a covered page from continuing collection/layout work.

App pause is different: the single active page may remain attached, but it is marked
inactive through the existing lifecycle gates, image descendants are disabled, and pending
restore work is cancelled. Resume recovery is therefore limited to that one foreground page;
there are no hidden retained grids to recover.

### 7.6 Series Detail lifetime

`MainView` has exactly one active subscription to
`MainViewModel.IsSeriesDetailVisible` while attached and marshals host changes to the UI
thread. It unsubscribes when the shell detaches or its core VM changes.

- `false -> true`: create one detail view lazily, assign the core VM, attach it to
  `SeriesDetailHost`, and activate its images only when foreground/visible;
- `true -> false`: disable its images, clear `DataContext`, remove it from the host, stop its
  existing toast timer through visual detachment, and clear the strong reference;
- destination change away from a compatible detail state continues to invoke the existing
  `CloseSeriesDetailCommand`;
- a construction failure leaves the base page usable, closes the failed detail presentation,
  and records/logs the error.

Detail scroll is transient and is not restored after the detail is closed.

## 8. Error and race behavior

- Unsupported destinations are not passed to the core factory; shell navigation continues to
  handle them through `ShellContent`.
- A target page is never committed if its navigation generation is stale.
- Every uncommitted target disconnects events, clears `DataContext`, and disposes any owned
  Settings lifetime.
- Scroll restore exceptions do not fail navigation.
- A stale restore cannot reactivate images or change the current page's offset.
- Pause, player/profile coverage, and detach invalidate restore work before removing the
  page.
- Page construction/activation failure keeps the former committed page instead of showing a
  blank surface.
- Cleanup paths are idempotent so repeated cancel/detach signals cannot double-dispose a
  Settings lease or double-unsubscribe a page.

## 9. Diagnostics

The existing performance snapshot will be extended with low-cost counters sufficient for
live proof:

- active base page count;
- core page created/released counts;
- restore requested/completed/cancelled/failed counts;
- stale navigation commit count;
- Series Detail created/released counts.

The active base page count must be a gauge, not a monotonically increasing counter. Normal
navigation must keep it at one while core content is visible and zero while the core page has
been released for a covering/non-core surface.

Diagnostics must not hold references to page instances.

## 10. Test strategy

Implementation follows red-green-refactor. Focused tests are written and observed failing
before the corresponding production change.

### 10.1 Structural contract tests

- `MainView.axaml` no longer declares all heavy mobile pages eagerly.
- The base and detail hosts exist and do not contain preconstructed heavy children.
- The destination registry/factory covers every supported core destination exactly once.

### 10.2 Pure lifecycle/state tests

Where Avalonia visual-tree testing is impractical in the existing test project, the state and
generation coordinator remains pure/linkable and is tested with fake pages/scroll surfaces:

- a completed revisit uses a fresh page instance;
- only one base page is committed;
- departure captures state and disconnects the fake page;
- first visit uses zero;
- revisit restores the saved offset;
- restore clamps to a reduced extent;
- a new generation cancels an old restore;
- pause/detach cancels restore;
- stale work cannot activate images;
- image activation occurs only after terminal restore;
- same-destination selection does not recreate the page;
- construction failure retains the previously committed page;
- stale prepared targets are cleaned up.

### 10.3 Integration/source contract tests

- active-page back routing covers Live, Movies, Series, Downloads, and Settings;
- category callbacks are attached/detached only for the active typed page;
- Settings entry preserves serialized lease release and stale-scope cleanup;
- Series Detail is lazy, source-destination independent, and removed when closed;
- profile/player entry captures and releases the active page;
- application pause does not reactivate images, and resume targets only the active page;
- the Phase 1 image-load and lifecycle contract tests remain valid.

### 10.4 Regression suite and builds

Run:

1. the new focused lazy-host/state tests;
2. the existing Android performance stability focused tests;
3. the full `Noctra.Tests` suite, allowing only the documented pre-existing baseline failures
   and no new failures;
4. `Noctra.Mobile` build;
5. `Noctra.Android` build and signed APK packaging.

## 11. Live Android acceptance

Install the new APK over the current application with `adb install -r`; do not clear app data.
Confirm the existing installation/user data remains present.

Run these scenarios while collecting the existing performance snapshots, process memory,
logcat crash/ANR/OOM signals, and new host counters:

### Scenario A — navigation torture

Repeat at least 50 times:

```text
Live -> Movies -> Series -> Search -> Live
```

Expected:

- active base page gauge remains one;
- created/released counts progress together, apart from the one current page;
- inactive grid resume activity is zero;
- stale host commits are zero;
- managed/native/PSS memory does not show one-way linear growth.

### Scenario B — long scroll and restoration

- fast-scroll at least 500–1000 items in Movies and Series;
- leave each page and return;
- verify the former position is restored within the valid extent;
- verify image work settles to viewport-level activity after scrolling stops;
- verify no offscreen image backlog remains.

### Scenario C — background/foreground

Perform at least 20 controlled background/resume cycles, including cycles after navigating and
scrolling.

Expected:

- only the current active grid attempts resume recovery;
- old restore generations never commit;
- image overflow/stale commit counters remain zero;
- no crash, ANR, OOM, permanent blank page, freeze, or navigation lock occurs.

### Scenario D — special lifetimes

- open and close Series Detail from Series, Search, and Downloads where data permits;
- enter/leave Settings repeatedly;
- enter/leave profile selection and player;
- use Android back through page sheets, detail, player, and navigation history.

Expected:

- base page returns at its saved scroll position;
- detail and Settings create/release counts balance after closure;
- no duplicate back/category/profile callback is observed.

## 12. Completion criteria

P0-01/P0-02 are considered implemented only when all of the following are true:

- the eager page collection is removed from XAML;
- one active base page is enforced by construction;
- departing pages lose shell events, `DataContext`, host membership, and strong shell
  references;
- per-destination scroll restoration works and is generation bounded;
- inactive/covered pages cannot perform image, layout-resume, or collection presentation work;
- Settings and Series Detail special lifetimes pass focused tests;
- no new automated regression is introduced;
- signed APK live acceptance passes without clearing existing user data;
- the implementation report records commands, counts, warnings, baseline failures, device
  results, and any remaining memory risk honestly.
