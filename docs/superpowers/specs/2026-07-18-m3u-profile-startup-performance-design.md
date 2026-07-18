# M3U Profile Startup Performance Design

## Context

Real-device testing on the Huawei DBY-W09 showed that M3U browsing is responsive once the profile is open, but cached profile entry and cold application startup still contain database work on the user-visible path.

Observed behavior:

- The first M3U profile shell appeared in about one second while import continued in the background.
- Live, Movies, Series, category and first-page navigation remained responsive during import.
- After the database grew to roughly 104 MB, selecting the cached M3U profile left the unchanged profile list visible beyond 2.3 seconds and reached Home at roughly six seconds.
- A cold application launch reached the profile list in roughly 7.3-8.3 seconds.
- No application ANR, crash or OOM was recorded during the test.

The unchanged profile list proves that the cached-entry delay begins before `LoadProfileAsync`: `ProfilesViewModel.SelectProfile` awaits the `LastUsed` database write before publishing the selection event. The selected profile is then queried a second time before the loading host is displayed. Separately, the first channel metadata read in each process runs three wide repair updates and may trigger full series aggregation. Startup schema fixups also rewrite matching Series metadata rows even when all target values are already null.

## Goals

- Display profile-selection feedback immediately and start profile loading without waiting for the `LastUsed` write.
- Remove the redundant selected-profile query.
- Run legacy channel-type repair at most once per playlist and repair version, including across application restarts.
- Avoid rewriting unchanged Series metadata during schema fixup.
- Preserve cancellation, abandoned-import recovery and provider-neutral browsing behavior.

## Non-goals

- Replacing EF Core or SQLite.
- Rewriting the entire application bootstrap sequence in this patch.
- Changing card layout, selection colors or virtualization behavior.
- Changing provider parsing or stream URL classification rules.
- Treating the provider's HTTP 407 playback response as an application performance defect.

## Design

### 1. Profile selection leaves the database-write critical path

`ProfilesViewModel.SelectProfile` will update the in-memory timestamp, publish `OnProfileSelected` and close the selector before persisting `LastUsed`. Persistence will run as a guarded background operation; failure will not cancel profile entry because this timestamp is sorting metadata, not profile integrity data.

The background method will catch and record failures rather than producing an unobserved task exception. It will not mutate credentials, playlist state or the active profile.

### 2. Reuse the already-loaded profile

`ProfilesViewModel.GetProfilesAsync` already loads `ProviderAccount`. `ProfileListView.ViewModel_OnProfileSelected` will therefore use the supplied profile instead of issuing another `Profiles.Include(ProviderAccount)` query.

The loading host will be populated and made visible before awaiting `MainViewModel.LoadProfileAsync`. A missing `ProviderAccount` remains a normal load error handled by `LoadProfileAsync`.

### 3. Persist the channel-type repair version

Add an integer repair-version field to `Playlist`. Schema fixup will add the column with a default of zero for existing databases. Newly constructed playlists will start at the current repair version because current importers assign channel types during parsing; only legacy rows with version zero require the compatibility repair.

`EnsureLinearStreamChannelTypesRepairedOnceAsync` will:

1. Keep the current in-process concurrency guard.
2. Read the playlist's persisted repair version.
3. Skip the three repair updates when the stored version is current.
4. Run the existing repair and conditional aggregation for legacy playlists.
5. Persist the current repair version only after repair and required aggregation complete successfully.

Repair changes and an internal "aggregation pending" marker are committed together. If aggregation is interrupted, the marker remains below the current version so the next entry retries aggregation without repeating the wide repair updates. The current version is persisted only after aggregation succeeds. Refresh staging playlists inherit the current model default and continue using the existing parser/provider classification.

This state is per playlist, so refreshing or replacing one playlist cannot incorrectly suppress repair for another.

### 4. Make Series metadata cleanup write only changed rows

The schema-fixup `UPDATE Series` will retain the existing EU group predicate but add a predicate requiring at least one target metadata column to be non-null. An index on `Series(GroupTitle)` will be created if missing.

The cleanup remains idempotent and backward-compatible, while repeated launches no longer generate writes and WAL growth for rows already in the desired state.

### 5. SQLite write interaction

This patch avoids adding another foreground writer: `LastUsed` is deferred until profile loading has started, repair remains serialized by its existing per-playlist guard, and repair version advancement occurs in the same ordered maintenance flow. A new global database-writer architecture is deliberately excluded because it would affect every persistence service and needs a separate design and stress-test cycle.

## Error handling and recovery

- `LastUsed` persistence failure is non-fatal and must not hide or close the active profile.
- Profile-load cancellation continues to be controlled by the existing profile generation token.
- Repair cancellation/failure leaves the stored version unchanged.
- Import interruption recovery and staging cleanup are unchanged.
- Schema fixup remains safe to run repeatedly on legacy and current databases.

## Tests

Tests will be written before production changes and observed failing for the intended reason.

1. Profile selection publishes the selection event without waiting for a blocked `UpdateLastUsedAsync` operation.
2. A failed background `LastUsed` persistence does not prevent selection.
3. A mobile source regression test verifies that profile selection uses the supplied profile and does not create a second database context before showing the loading host.
4. A playlist with the current repair version skips repair and aggregation.
5. A legacy playlist runs repair once and persists the current version.
6. Cancellation/failure does not persist the repair version.
7. Schema fixup does not alter already-clean Series rows and remains idempotent.
8. Existing cancellation, import recovery and playlist integration tests remain green.

## Device verification

After automated tests pass, build and install the APK without clearing application data and repeat on the Huawei DBY-W09 with the same real M3U profile:

- Cold launch to profile list.
- Profile tap to first Home frame.
- First Live/Movies/Series page.
- Category sheet and All selection.
- Series detail and episode load.
- Rapid navigation and long scroll.
- DB/WAL/SHM size, PSS/RSS and crash/ANR/OOM inspection.

Acceptance targets for this patch:

- Profile selection produces visible feedback immediately.
- A cached, idle M3U profile reaches its shell in about one second.
- The same playlist does not execute legacy channel-type repair again after a successful repair-version commit.
- No regression in Xtream, Stalker or M3U list loading and cancellation tests.
