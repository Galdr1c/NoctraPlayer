# Stalker First-Series Availability

## Problem

Stalker category discovery returns Live, VOD, and Series metadata together, but the import queue currently processes all Live categories, then all VOD categories, then all Series categories. Series rows are also aggregated only after the entire provider import completes. On a large portal this leaves the Series screen in its loading state for several minutes even though the profile shell, Live, and Movies are usable.

## Chosen design

1. Build the initial Stalker work queue by interleaving categories from `itv`, `vod`, and `series`, preserving the server's order inside each type. This lets the existing five workers start useful content for all three navigation destinations early.
2. After the first completed non-empty Series category is persisted, run exactly one early aggregation for that playlist. Do not aggregate after every category.
3. Raise the normal aggregation-completed notification after the early pass and allow the view model to refresh its lightweight Series cache while provider import is still active.
4. Keep the existing final aggregation after the full import. It remains authoritative and incorporates all later Series categories.

## Constraints

- No schema migration or new persistent identity column.
- No repeated full aggregation per batch or per category.
- Preserve cancellation and profile-scope checks.
- Preserve category priority behavior and order within each content type.
- A failed early aggregation is non-fatal; import continues and the final aggregation remains the recovery path.

## Test strategy

- A queue-order test proves the first scheduling window contains Live, VOD, and Series while maintaining relative order within each type.
- An early-aggregation gate test proves only the first completed non-empty Series category claims the early pass; Live, VOD, empty, and subsequent Series batches do not.
- Existing Stalker import and view-model tests must remain green.
- Device verification measures profile shell, first Live/Movies cards, first Series cards, navigation responsiveness, scroll behavior, and crash/ANR/memory signals during a real import.

## Alternatives rejected

- Reordering only: Series channels arrive earlier but remain invisible until final aggregation.
- Direct Series upsert with a new normalized unique key: potentially optimal, but expands schema and recovery scope beyond the observed bug.
