# NoctraPlayer P0-09/P0-10 Implementation Plan

**Design:** `docs/superpowers/specs/2026-08-13-search-ranking-engine-design.md`  
**Method:** Root-cause evidence, red-green-refactor, scoped review, Android acceptance  
**Workspace:** `D:\IPTVPlayer`

## Phase 1 — Establish the ranking contract in red tests

1. Add `Noctra.Tests/IncrementalSearchRankingSessionTests.cs`.
2. Define the wished-for session API in tests using real Channel and Series models.
3. Prove a Channel contributes to primary/similar/suggestion selection after one evaluation.
4. Append a second page containing old and new identities; assert only new identities increment
   the evaluation count and that the bounded result ordering merges correctly.
5. Re-supply the same Series snapshot and assert zero additional Series evaluations.
6. Create two query sessions over unchanged items and assert normalized documents are reused;
   mutate a searchable field and assert the document is rebuilt.
7. Cancel a large batch and assert cooperative `OperationCanceledException`.
8. Run only the new fixture and observe the expected compile failure.

## Phase 2 — Implement the pure incremental engine

1. Add Core search types under `Noctra.Core/Search`:
   - normalized Channel/Series documents;
   - identity/fingerprint keys;
   - normalized document cache;
   - bounded ranked bucket;
   - immutable result snapshot;
   - query-scoped incremental session.
2. Move or share fuzzy normalization/scoring primitives without changing thresholds.
3. Evaluate each item once per query session and feed primary, similar, and suggestion state from
   that result.
4. Check cancellation in outer item loops, episode loops, and fuzzy comparison loops.
5. Run the new fixture until green, then refactor only after green.

## Phase 3 — Integrate with MainViewModel ownership

1. Add a MainViewModel regression fixture for incremental page evaluation and stale query commit.
2. Add search-session generation/state to `MainViewModel`.
3. Invalidate the session on short query, view, playlist, profile, and content-generation change.
4. Replace synchronous `UpdateSearchBuckets()` full scans with an async page-aware method.
5. Rank the new Channel page and unseen Series documents on a background task using the active
   request token.
6. Commit only if session, content generation, normalized query, Search view, playlist, and token
   remain current.
7. Preserve all observable collection instances and update them in place.
8. Add monotonic, content-free search ranking telemetry.

## Phase 4 — Verification and review

1. Run new ranking and MainViewModel fixtures.
2. Run existing `FuzzySearchTests` and search/navigation/mobile regression groups.
3. Repeat the focused concurrency/cancellation package 10 times.
4. Run the full `Noctra.Tests` suite and compare failures with the established unrelated baseline.
5. Build Core, Mobile, and Android.
6. Run `git diff --check` and inspect the complete scoped diff.
7. Apply the `requesting-code-review` workflow; resolve all Critical/Important findings with new
   red tests before APK installation.

## Phase 5 — Android acceptance and report

1. Build and hash the signed debug APK from `D:\IPTVPlayer`.
2. Record package install times and install only with `adb install -r`.
3. Run at least 50 Search/Movies/Series transitions with repeated Search result scrolling and
   rapid query replacement.
4. Run 10 background/resume cycles.
5. Require stable process, correct final query results, and zero ANR/crash/OOM/stale commit.
6. Validate search telemetry: page 2 evaluates only new Channel identities; cancelled query work
   cannot commit.
7. Update P0-09/P0-10 in the main report with exact test/build/APK/device evidence.
