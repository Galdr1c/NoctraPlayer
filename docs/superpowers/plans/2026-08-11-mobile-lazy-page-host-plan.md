# NoctraPlayer Mobile Lazy Active Page Host Implementation Plan

**Design:** `docs/superpowers/specs/2026-08-11-mobile-lazy-page-host-design.md`  
**Method:** Red-green-refactor, then Android live acceptance without clearing app data

## Phase 1 — Lock the lazy-host and state contracts with failing tests

1. Add `Noctra.Tests/MobileLazyPageHostContractTests.cs`.
2. Assert that `MainView.axaml` contains empty base/detail hosts and no eager heavy page
   declarations.
3. Assert that one centralized factory covers all ten core destinations.
4. Assert active-page cleanup, typed back/category routing, Settings lease handling, lazy
   Series Detail lifetime, and profile/player release paths in `MainView.axaml.cs`.
5. Add `Noctra.Tests/MobilePageNavigationStateTests.cs` for generation and offset-state
   behavior.
6. Link the pure mobile state file into `Noctra.Tests.csproj` and run the focused filter to
   observe the intended red failures before production implementation.

## Phase 2 — Implement pure navigation state and explicit scroll ownership

1. Add `Noctra.Mobile/Navigation/MobilePageNavigationStateStore.cs` with:
   - destination-keyed immutable offsets;
   - monotonic navigation generations;
   - current-generation checks;
   - clamping and profile-reset behavior;
   - no control/view/view-model references.
2. Add `Noctra.Mobile/Navigation/IMobileNavigationStateParticipant.cs` and an Avalonia
   scroll accessor that operates only on an explicitly supplied primary control.
3. Name the primary grid/feed/scroll owner in every core page XAML where it is not named.
4. Implement capture/restore participation in Home, Live, Movies, Series, Search, Favorites,
   MyList, History, Downloads, and Settings code-behind.
5. Make restore report “not ready” until a valid attached ScrollViewer/viewport exists and
   clamp the target to the current extent.
6. Run the pure state tests and compile the Mobile project.

## Phase 3 — Replace eager XAML with the single active-page host

1. Replace the eleven eager controls in `MainView.axaml` with `ActiveCorePageHost` and
   `SeriesDetailHost` content controls.
2. Add `Noctra.Mobile/Navigation/MobileCorePageFactory.cs` with an explicit ten-destination
   map and fresh construction per completed revisit.
3. Add an active-page record in `MainView` and replace named page switches with that record.
4. Refactor back handling, category selection callbacks, image-load activation, and page
   retrieval to use only the active page.
5. Implement atomic target preparation and commit:
   - keep the old page on preparation failure;
   - capture old state before removal;
   - detach callbacks, clear DataContext, remove Content, and clear the strong reference;
   - attach only a current-generation target.
6. Keep the target's image descendants inactive until the bounded restore reaches a terminal
   result.
7. Implement same-destination no-recreate behavior.
8. Run the new structural/lifecycle tests after each slice until green.

## Phase 4 — Preserve special lifetimes and covering surfaces

1. Preserve the serialized Settings scope release chain and dispose every stale prepared
   lease exactly once.
2. Subscribe once to the core VM detail property while `MainView` is attached.
3. Create `MobileSeriesDetailView` only while detail is open; remove its events, DataContext,
   images, timer ownership, and host reference on close.
4. Release the base page when entering More, full profile selection, or the full player; on
   return, reconstruct the destination and restore its saved offset.
5. On pause, cancel restore and image work without constructing or resuming hidden pages.
6. On hot reload and shell detach, invalidate generations and perform idempotent cleanup.
7. Clear destination scroll states when the active profile changes.
8. Run Settings, detail, back, player, profile, and Phase 1 lifecycle regression tests.

## Phase 5 — Diagnostics and automated verification

1. Emit low-cost `PerformanceTrace` values for:
   - active base page gauge;
   - page created/released;
   - restore requested/completed/cancelled/failed;
   - stale host commits;
   - detail created/released.
2. Ensure diagnostics store numeric values only and never page references.
3. Run focused tests:

   ```powershell
   dotnet test Noctra.Tests\Noctra.Tests.csproj --filter "FullyQualifiedName~MobileLazyPageHostContractTests|FullyQualifiedName~MobilePageNavigationStateTests|FullyQualifiedName~AndroidPerformanceStabilityContractTests"
   ```

4. Run the full test suite and compare failures with the documented baseline.
5. Build `Noctra.Mobile` and `Noctra.Android`; treat warnings separately from errors and
   introduce no new build errors.
6. Run `git diff --check` and review the complete scoped diff.
7. Perform an independent code review using the repository review workflow, address verified
   findings, and rerun affected tests.

## Phase 6 — APK and live Android acceptance

1. Produce the signed/debuggable APK through the repository's established packaging path.
2. Confirm the connected device and existing package data/timestamps.
3. Install with `adb install -r`; never use `pm clear` or uninstall.
4. Run 50+ `Live -> Movies -> Series -> Search -> Live` loops.
5. Run 500–1000-item Movies/Series scrolling and verify destination scroll restoration.
6. Run at least 20 controlled background/resume cycles.
7. Exercise Settings, Series Detail from multiple sources, profile selection, player, and Back.
8. Collect performance snapshots, process memory, and crash/ANR/OOM logcat evidence.
9. Confirm one active base page, zero inactive-grid resume work, zero stale host commits, and
   no one-way linear memory growth.
10. Update the Phase 1 implementation report or add a Phase 2 report with exact automated and
    live results, residual risks, and the next report finding.

