# Imported Playlist Lifecycle Design

## Problem

Desktop and Android copy a selected local M3U/M3U8 playlist into the app-managed Imports directory. Profile deletion removes database content but leaves that copy behind. Replacing the local playlist while editing a profile also leaves the previous copy behind.

## Scope

This change covers immediate profile deletion, delayed-deletion purge, legacy child-profile cleanup, and an existing profile source change. It applies in Noctra.Core so desktop and Android share the same behavior.

It does not delete audio or video files referenced from inside an M3U playlist. It also does not delete arbitrary user-selected files outside the app-managed Imports directory.

## Considered Approaches

1. Delete the old path directly in each file picker. This is rejected because pickers do not know whether another profile still references the path, and UI-specific cleanup would diverge between desktop and Android.
2. Run only a periodic orphan scan at startup. This is rejected as the primary solution because cleanup would be delayed and a failed scan would keep accumulating files.
3. Perform reference-aware cleanup in ProfileService after successful database commits. This is selected because ProfileService owns profile and playlist lifecycle and is shared by both platforms.

## Selected Behavior

Before a destructive database operation, ProfileService collects candidate paths from the profile's Playlist.FilePath values and its provider account URL. A candidate is eligible only when its normalized parent directory is exactly the app's Imports directory.

After the database transaction commits, ProfileService reloads all remaining Playlist.FilePath and ProviderAccount.Url references. It deletes only eligible candidates that have no surviving reference. Paths are compared case-insensitively on Windows and case-sensitively elsewhere.

Profile save and destructive profile lifecycle operations share a process-wide lifecycle gate whenever the managed path service is available. This closes the reference-query/delete race. A save that starts after deletion and targets a managed copy already removed by that deletion is rejected before its database transaction, rather than persisting a dangling path.

Physical cleanup is best-effort. Root resolution and reference lookup failures are logged without reporting an already-committed profile operation as failed. Each file deletion is isolated, so one locked file does not prevent later candidates from being attempted.

## Data Flow

- Profile deletion: collect candidates, delete database content, commit, remove unreferenced managed copies.
- Delayed purge: collect candidates for all expired profiles, commit the batch deletion, remove unreferenced managed copies.
- Legacy child-profile cleanup: collect candidates for child profiles, commit, remove unreferenced managed copies.
- Profile source replacement: collect the previous account URL and playlist paths, save the new account source and remove old playlist rows, commit, remove unreferenced old managed copies.

## Safety Invariants

- Never delete a path outside the direct Imports directory.
- Never delete a path still referenced by any Playlist or ProviderAccount row.
- Never delete the new source merely because it belongs to the edited profile.
- Never delete a candidate before the database transaction succeeds.
- Missing files are treated as an idempotent success.
- Never allow reference creation to overlap the reference-check/delete section.
- Never persist a managed Imports path that disappeared while waiting for the lifecycle gate.

## Tests

Regression tests prove that profile deletion removes an unreferenced managed copy, preserves a file outside Imports, preserves a managed copy referenced by another profile, and that source replacement removes only the old managed copy. Delayed purge is covered because it is the normal endpoint of scheduled profile deletion. Additional tests cover root-resolution failure after commit, per-file failure isolation, and deterministic serialization of concurrent deletion and managed-source creation.

## Deferred

Abandoning a create/edit form after picking a new file can create a separate unsaved-selection orphan. That lifecycle requires explicit edit-session ownership and is intentionally outside this focused fix.
