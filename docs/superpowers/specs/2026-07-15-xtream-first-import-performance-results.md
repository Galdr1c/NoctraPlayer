# Xtream First-Import Performance Results

Date: 2026-07-15  
Device: Huawei DBY-W09, Android API 31, arm64  
Build: Debug, `net10.0-android36.0`  
Provider: local synthetic Xtream endpoint over `adb reverse`

## Outcome

The freeze was reproducible and was dominated by import-pipeline work rather than SQLite capacity. The patch removes per-batch full organization work, uses prepared bulk writes inside coordinated transactions, throttles import-job progress writes, separates user-ready state from series aggregation, and adds interruption recovery.

The profile shell target is met on this device (517-556 ms). The first 30 visible rows target is improved but not met: measured values remain 2.0-3.3 seconds rather than approximately one second.

## Synthetic matrix

These runs were collected while the patch was being developed. The 100k run is the last full timing run before the final category-marker/cancellation correctness changes; those changes were separately covered by integration tests and the final kill/recovery device run.

| Items | Tap to shell | First 30 rows | Raw import/user-ready | Rows/s | Peak managed | GC collections (0/1/2) | Series aggregation |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 10,000 | 530 ms | 2,495 ms | 11.74 s | 906 | 79.4 MB | 29 / 1 / 1 | 11.31 s |
| 50,000 | 556 ms | 2,089 ms | 24.55 s | 2,094 | 154.9 MB* | 73 / 3 / 3 | 43.61 s |
| 100,000 | 517 ms | 3,292 ms | 40.65 s | 2,503 | 91.0 MB | 137 / 5 / 5 | measured separately |
| 250,000 | 548 ms | 2,042 ms | 167.50 s | 1,499 | 306.9 MB | 901 / 16 / 16 | continued after user-ready |

`*` The 50k peak includes the aggregation phase and is not directly comparable to import-only peaks.

No instrumented SQLite duration marker ran on the UI thread in these traces. This verifies that the patched batch writer is off the UI thread, but it is not a full Android Looper longest-block measurement; a Perfetto/Looper frame-timeline run is still required to state an exact maximum main-thread stall.

## Storage and native samples

| Items | DB | WAL | SHM | Native / bitmap evidence |
|---:|---:|---:|---:|---|
| 10,000 | 4.42 MB | 4.38 MB | 32 KB | not sampled |
| 50,000 | 23.04 MB | 4.86 MB | 32 KB | not sampled |
| 100,000 | 55.23 MB | 6.21 MB | 32 KB | debug sample: native 289 MB, PSS 658 MB, RSS 758 MB |
| 250,000 | 126.41 MB | 19.80 MB | 64 KB | debug sample: native 241 MB, PSS 854 MB, RSS 907 MB |

The Android debug PSS/RSS samples vary substantially and include runtime/debugger overhead. They are useful as a ceiling signal, not as release-build memory acceptance numbers.

At 250k, Android GC logging showed 19 stop-the-world pauses totaling 3.573 ms (maximum 1.702 ms), plus about 199.8 ms of concurrent GC work.

## Interruption and consistency verification

- The final 100k device run was force-stopped after raw import completed and while series aggregation was active.
- On restart the profile shell and live cards were accessible.
- Final database snapshot: 100,000 channels, 0 dummy markers, 30,000 series rows, 0 active import jobs.
- A partial unique index permits only one active import job per profile; legacy duplicates are canceled transactionally during schema fixup.
- Category replacement is atomic. Injected SQLite write failure rolls back marker deletion and channel-count changes.
- Xtream markers include provider type and category ID, so same-name/same-type categories and empty categories recover independently.
- Profile-scope cancellation reaches discovery queries, staging creation/commit, marker writes/deletes, bulk replacement, counts, and watch-history repair.
- Abandoned refresh staging copies are cleaned before creating a replacement staging playlist.

## Low-disk status

Physical storage exhaustion was not induced on the user's tablet. The equivalent database failure path was tested with an injected SQLite `disk full` failure: the error propagates out of the provider callback, the category is not counted as persisted, and the transaction preserves its recovery marker. A destructive real low-disk device test remains outstanding.

## Acceptance status

- Profile shell independent of import/maintenance: **pass**.
- First visible page around one second: **not yet met** (2.0-3.3 s).
- No measured long SQLite calls on UI thread: **pass for instrumented import path**, exact Looper maximum still unmeasured.
- Kill during import/aggregation and reopen profile: **pass**.
- No multiple abandoned active import jobs: **pass**.
- No multiple refresh staging copies after the next refresh attempt: **pass**; proactive startup cleanup without a refresh is not implemented.
- Real device low-disk behavior: **not physically tested**; transactional failure path passes.

## Trace artifacts

Raw JSONL traces and database snapshots are stored under `artifacts/performance/` and are intentionally git-ignored.
