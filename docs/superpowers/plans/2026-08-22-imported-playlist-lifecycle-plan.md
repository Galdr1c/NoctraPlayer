# Imported Playlist Lifecycle Implementation Plan

1. Add failing ProfileService regression tests for deletion, shared-reference protection, outside-Imports protection, source replacement, and delayed purge.
2. Run only the new tests and confirm they fail because managed copies remain.
3. Inject IAppPathService into ProfileService as an optional final constructor dependency so existing tests and callers remain source-compatible while production dependency injection supplies the configured platform path service.
4. Add private candidate collection, normalization, remaining-reference lookup, per-file best-effort deletion, and release logging helpers.
5. Serialize managed-source saves and destructive profile lifecycle operations through a shared gate; reject a managed source that disappeared while waiting.
6. Call cleanup only after successful commits in SaveProfileAsync, DeleteProfileAsync, PurgeExpiredProfilesAsync, and DeleteChildProfilesAsync.
7. Re-run the focused tests, then the complete Noctra.Tests suite, and verify the working tree diff contains no unrelated changes.
