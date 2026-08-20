# P1-20 EPG visible refresh design

## Problem

The five-minute UI EPG refresh currently passes the entire loaded `Channels` collection to the bulk lookup. Paging therefore increases the refresh workload even though only a small viewport is rendered.

## Design

- `MobileVirtualizingCardGrid` and `DesktopVirtualizingCardGrid` expose the source items held by their realized row controls. The virtualization cache is intentionally included as a small overscan window.
- The Live view sends that realized snapshot to `MainViewModel` on attach and scroll. The ViewModel deduplicates live channels by ID and stores a bounded immutable snapshot.
- The Live view republishes after active-view changes and collection rebuilds so a retained desktop view cannot leave the snapshot empty after navigation.
- The timer refresh reads only that snapshot. If the active view is not Live, or the snapshot is empty, it exits without a database query.
- View, playlist, profile, filter, and sort changes clear the snapshot. A version guard rejects a lookup result if the snapshot changed while the database query was in flight. A single atomic running flag prevents overlapping timer refreshes.
- The existing page-load enrichment remains unchanged; it already receives only the newly loaded page.

## Rejected alternatives

- Passing `FilteredChannels` would still scan every loaded row and is not a viewport guarantee.
- Reimplementing row geometry in the ViewModel would duplicate virtualization calculations and drift when card metrics change.

## Acceptance

- A 200-item loaded collection with a 12-item realized snapshot issues a 12-item EPG lookup.
- An empty snapshot issues no lookup.
- Realized mobile and desktop rows expose only their bounded virtualization window.
- The timer cannot overlap itself and does not retain the snapshot after view/playlist/profile changes.
