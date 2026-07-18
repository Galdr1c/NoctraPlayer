# Progressive import group metadata refresh

## Problem

On a brand-new Stalker profile the content shell can query group metadata before
the provider has persisted discovered categories. The empty result is cached by
playlist id. Later progressive writes populate SQLite and cards reload, but the
metadata query short-circuits against the same playlist id. Category selection
therefore stays hidden until the profile is reopened and its cache is cleared.

## Design

Add one UI-thread helper in `MainViewModel` for provider persistence boundaries.
It will act only when the persisted playlist is still selected, invalidate the
cached group metadata for that playlist, and reuse the existing cancellable,
paged `LoadChannelsAsync` path. Provider callbacks call it after discovered
category markers have committed. Refresh-staging commits call it after the
staging playlist replaces the active playlist.

The helper is intentionally not called for every content batch. Category names
are complete at discovery time, so one authoritative SQLite metadata refresh is
enough and avoids repeated full group queries during large imports. The same
boundary is used for Stalker and Xtream because both progressive startup paths
can cache an early empty result.

## Safety and verification

- Ignore callbacks for a playlist that is no longer selected.
- Keep existing generation-based cancellation and paging behavior.
- Add a regression test that first caches empty metadata, simulates provider
  category persistence, then verifies a second metadata query makes the category
  filter visible without reopening the profile.
- Run focused tests, the full suite, Android build, then reinstall without
  clearing application data and validate the first-entry flow on device.
