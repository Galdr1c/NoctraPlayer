# NoctraPlayer Incremental Search Ranking Design

**Scope:** P0-09 and P0-10  
**Target:** Android-first; shared Core behavior on desktop  
**Compatibility:** Preserve current fuzzy thresholds, result limits, ordering, suggestion rules,
and observable collection identity.

## Problem

`MainViewModel.UpdateSearchBuckets()` rebuilds every search bucket from accumulated source
collections. Channels are scored once for primary results and again for similar results and
suggestions. The complete Series cache is rescored on every channel page. Because
`LoadMoreChannelsAsync()` calls the method after every 30-item page, work grows with all pages
already loaded instead of only with the new page.

## Chosen architecture

Introduce an internal, query-scoped `IncrementalSearchRankingSession` in Core. One session owns:

- normalized query parts;
- the playlist/query generation identity;
- processed Channel and Series identity sets;
- bounded primary and similar Top-K buckets;
- the best suggestion candidate;
- cumulative evaluation counters used by telemetry and tests.

The session accepts only newly arrived Channel/Series items. An item identity is evaluated at
most once in a session. A normalized-document cache stores normalized searchable fields by
playlist/item identity plus a source fingerprint, so later queries reuse normalization while
changed source fields rebuild the document.

`MainViewModel` creates a fresh session when query, playlist, profile, or Search ownership
changes. Each page is ranked on a background worker with the request cancellation token. UI
collections are updated only if the same session/generation/query/view/playlist is still active.

## Ranking data flow

```text
new DB page + current Series snapshot
        |
        v
document cache (normalize once per unchanged item)
        |
        v
query session identity check (skip items already scored)
        |
        v
single score per item
        |
        +--> bounded primary Top-K
        +--> bounded similar Top-K
        +--> best suggestion candidate
        |
        v
immutable snapshot
        |
        v
generation/view/playlist/query guard
        |
        v
in-place observable collection update
```

Channel Live/VOD classification occurs after its single score. Series primary scoring retains
episode-name matching; similar scoring continues to exclude episode-only similarity. Ranking
ties remain score descending, image availability descending, then title ascending.

## Incremental and bounded behavior

- Page 1 evaluates only page 1 Channels plus Series not already seen by the session.
- Page N evaluates only page N Channels; the unchanged Series snapshot is skipped by identity.
- Primary buckets retain at most 96 entries per type.
- Similar buckets retain at most 18 entries per type.
- The implementation does not sort the complete candidate collection after every page.
- An empty terminal page commits no new ranking work.

The content database query remains unchanged in this patch. This keeps result compatibility and
limits scope to eliminating repeated CPU work rather than replacing persistence search with FTS.

## Cancellation and stale commit rules

Ranking checks cancellation throughout Channel, Series, episode, and bounded-merge loops.
Cancellation or replacement may leave only the discarded old session partially populated; it
must never mutate the new session or UI. A result commit requires all of:

- Search is the active view;
- playlist id matches;
- normalized query matches;
- content generation matches;
- the captured session is still the active session;
- cancellation has not been requested.

Short queries invalidate the session and clear all buckets synchronously.

## Telemetry

Add monotonic performance marks for:

- `search.rank.items.evaluated.count`;
- `search.rank.items.reused.count`;
- `search.rank.sessions.started.count`;
- `search.rank.sessions.cancelled.count`;
- `search.rank.stale_commit_rejected.count`;
- `search.rank.commit.count`.

Counters must not contain query text or content titles.

## Test strategy

1. Pure ranking tests prove one item is scored once across primary/similar/suggestion paths.
2. Appending a second page evaluates only new identities and merges Top-K correctly.
3. Re-supplying Series on later Channel pages performs zero additional Series evaluations.
4. Normalized documents are reused across query sessions and invalidated when fields change.
5. Cancellation during a large batch terminates cooperatively.
6. MainViewModel tests prove an old query/navigation result cannot commit and a second page does
   not reset collection identity.
7. Existing fuzzy behavior tests and Search/mobile navigation regressions remain green.

## Acceptance

- Focused ranking and MainViewModel tests pass repeatedly.
- Existing fuzzy result behavior remains unchanged for exact, prefix, contains, Turkish
  normalization, typo, episode, suggestion, and similar-result cases.
- Telemetry proves page 2 evaluates only page 2 Channel identities.
- Android Search stress keeps the same PID with no ANR/crash/OOM and no stale commit.
