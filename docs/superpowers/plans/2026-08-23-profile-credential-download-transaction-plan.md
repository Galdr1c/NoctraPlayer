# Profile Credential Change and Download Transaction Plan

1. Add a failing transaction-order contract test that asserts the profile
   transaction commits before the second-context download service call.
2. Add failing worker-state/contract tests for credential-failure markers,
   queued-worker suppression, and final `Failed` status.
3. Move profile download invalidation after `transaction.CommitAsync`, keeping
   profile failures isolated from download invalidation failures.
4. Add ContentDownloadService marker/cancellation coordination and busy-retry
   persistence.
5. Run focused tests, the full suite, Debug/Release builds, and inspect the
   resulting status/error behavior.
