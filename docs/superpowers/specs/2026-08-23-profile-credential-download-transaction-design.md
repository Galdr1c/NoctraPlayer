# Profile Credential Change and Download Transaction Design

## Root cause

`ProfileService.SaveProfileCoreAsync` opens a SQLite write transaction, writes
the profile/provider and playlist data, then calls
`IContentDownloadService.FailActiveDownloadsForProfileAsync`. The current
download method creates a second `AppDbContext` and calls `SaveChangesAsync`.
When active/paused/queued rows exist, this is a second writer against the same
SQLite file while the profile transaction is still open and can produce
`SQLITE_BUSY`/`database is locked`.

## Selected design

The profile transaction owns only profile/provider/playlist state. It commits
first. Credential-change download invalidation then runs post-commit through the
existing service boundary, so it uses its own context only after the profile
writer has released its lock. A download invalidation failure is logged and does
not report the already-committed profile change as rolled back.

`ContentDownloadService` coordinates the post-commit operation with workers:

1. Read active/paused/queued items and mark their IDs as credential-failure
   requested in process memory.
2. Cancel and await active workers before writing their final status.
3. Queued workers check the marker and skip starting a row already invalidated.
4. Persist `Failed` status/error for all selected rows with the existing SQLite
   busy retry pattern.
5. Clear queue/pause/auto-resume state and publish one `DownloadsChanged` event.

Workers that observe cancellation while the marker is present do not overwrite
the final `Failed` state with `Paused`. The marker is removed after the post-
commit write. If the profile transaction itself fails, the post-commit phase is
never invoked and downloads remain untouched.

## Error handling

- Profile transaction failures roll back profile and playlist changes and do not
  invalidate downloads.
- Download invalidation failures cannot roll back an already-committed profile;
  they are logged for diagnostics while the Failed/active row state remains
  inspectable.
- Worker cancellation is bounded by the existing worker wait policy; a worker
  that does not stop promptly is prevented from overwriting the credential-fail
  result by the in-memory marker and SQLite busy retries.

## Verification

- A transaction-order contract regression test proves the download service call
  appears only after the profile transaction commits; existing ProfileService
  integration scenarios continue to cover the credential-change data reset.
- Worker-state tests prove cancellation cannot change the final status back to
  `Paused`, and queued credential-invalidated items do not start.
- Full test suite and Android/Desktop builds must remain clean.
