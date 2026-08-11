# NoctraPlayer Navigation Single-Reset Implementation Plan

**Design:** `docs/superpowers/specs/2026-08-11-navigation-single-reset-design.md`  
**Method:** Red-green-refactor, focused regression verification, then Android live acceptance  
**Status:** Complete — automated, build, review, and Android acceptance evidence recorded in P0-06

## Phase 1 — Lock collection identity and reset-count behavior

1. Add a focused `MainViewModelNavigationResetTests` test fixture using the existing
   MainViewModel test construction pattern and an immediate dispatcher.
2. Add a channel-navigation test that retains the original `Channels` and
   `FilteredChannels` references, navigates to a different channel destination, and asserts:
   - both references are unchanged;
   - `FilteredChannels` publishes exactly one reset before the first page add;
   - the new destination receives only correctly typed items.
3. Run the focused test and record the expected red failure caused by collection replacement
   and duplicate reset ownership.
4. Add the equivalent Series identity/reset-count test and observe its intended red failure.

## Phase 2 — Implement stable in-place reset primitives

1. Change `ResetIncrementalState` to reset paging fields and clear `Channels` and
   `FilteredChannels` in place, clearing only once if they share an instance.
2. Change `ResetSeriesIncrementalState` to clear `_seriesFilteredSource` and
   `SeriesViewItems` in place.
3. Remove direct replacement assignments from `PrepareContentSurfaceForNavigation`.
4. Run the identity-focused tests; identity assertions should turn green while duplicate-reset
   assertions remain red until ownership is implemented.

## Phase 3 — Establish one navigation reset owner

1. Replace the boolean-only navigation reset state with an owner record containing the target
   `AppView` and a monotonically increasing reset generation.
2. Have navigation preparation perform the one target reset and publish the owner generation.
3. Pass the captured owner generation through `ApplyFiltersAsync` completion so stale
   `finally` blocks cannot clear newer reset state.
4. When `ApplyFiltersAsync` matches the prepared target, reuse the prepared reset rather than
   resetting the target lane again.
5. For ordinary same-page filters, perform exactly one in-place reset in
   `ApplyFiltersAsync`.
6. Stop clearing/replacing unrelated inactive lanes during a target filter pass.
7. Run the channel and Series tests until both identity and reset-count assertions are green.

## Phase 4 — Prove cancellation and ordinary filtering

1. Add a same-page filter test proving stale items are cleared exactly once and the new query
   results are added normally.
2. Add a rapid `Live -> Movies -> Series` test with controlled query completions proving:
   - superseded channel data does not commit;
   - Series keeps ownership of its prepared reset;
   - stale completion cannot clear the newer pending state.
3. Add narrow numeric telemetry assertions or source-contract coverage for prepared, consumed,
   ordinary, and stale reset counters.
4. Run `MainViewModelIncrementalCancellationTests` and navigation behavior regressions.

## Phase 5 — Automated verification

1. Run the new focused P0-06 tests.
2. Run MainViewModel incremental cancellation, profile reload, mobile navigation, lazy host,
   and Android performance contract tests.
3. Run the full `Noctra.Tests` suite and compare any failures with the documented baseline.
4. Build `Noctra.Core`, `Noctra.Mobile`, and `Noctra.Android`.
5. Run `git diff --check` and inspect the complete scoped diff.

## Phase 6 — APK and Android acceptance

1. Build the signed debug APK from `D:\IPTVPlayer`.
2. Record APK SHA-256 and existing package install timestamps.
3. Install with `adb install -r` without uninstalling or clearing data.
4. Run at least 50 `Live -> Movies -> Series -> Live` transitions.
5. Confirm immediate loading/empty presentation, correct first-page content, and continued
   paging.
6. Inspect reset telemetry and confirm one prepared/consumed reset per navigation with no
   second target reset from the filter pass.
7. Check logcat and window state for ANR, crash, stale commit, or unrecoverable blank page.
8. Update the implementation report with exact automated and live evidence and mark P0-06
   complete only if every acceptance criterion passes.
