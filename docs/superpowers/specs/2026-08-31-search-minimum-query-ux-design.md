# Search minimum-query UX design

## Problem

Search intentionally requires at least two normalized characters, but the UI
still allows a one-character query to be committed. `OnSearchTextChanged`
correctly avoids starting the expensive search and clears the result buckets,
while `ShowSearchIdleState` remains false because the committed `SearchText` is
not empty. Neither the idle nor the empty state is visible, leaving a blank
Search page.

The same state can be reached from the mobile Search button and from Enter in
the desktop header search box.

## Design

- Keep `MinSearchQueryLength = 2` and evaluate the normalized input through
  `SearchDocumentCache.Normalize`; whitespace and punctuation do not count as
  searchable characters.
- Add `CanCommitSearch`, derived from `SearchQuery`, and use it as the generated
  `CommitSearchCommand` can-execute condition. The mobile Search button disables
  automatically through command binding, and desktop/mobile Enter cannot commit
  an invalid query.
- Add `ShowSearchMinimumLengthHint`, true only when the input contains something
  other than whitespace but normalizes to fewer than two characters.
- Notify `CanCommitSearch`, `ShowSearchMinimumLengthHint`,
  `ShowSearchIdleState`, and the command whenever `SearchQuery` changes.
- Keep a defensive length guard inside the commit path. A direct or future
  programmatic call with an invalid value must not change `SearchText`, navigate
  to Search, clear existing results, or start DB/ranking work.
- Treat an uncommitted short query as a validation state, not as an empty-result
  search. Existing committed results remain intact until a valid query is
  submitted.
- Show a compact localized message directly beneath the search controls on
  mobile and in the desktop Search view: “Enter at least 2 characters to
  search.” The hint replaces the mobile idle state while active and disappears
  immediately once the normalized length reaches two or the field is cleared.
- Add the new localization key to all five supported languages.

## Performance safeguards

- No DB query, ranking session, cancellation source, result synchronization, or
  collection reset is created for a short query.
- Existing bounded search candidate limits, incremental ranking,
  `SearchDocumentCache`, collection diffing, generation checks, cancellation,
  and UI dispatch behavior remain unchanged.
- The hint is a derived boolean and does not introduce timers, background work,
  or per-keystroke collection mutations.

## Deferred candidate-index finding

The DB candidate funnel still uses literal `Name` / `GroupTitle` filtering, so
some typo, `TvgName`, and Turkish-normalized matches can be excluded before
fuzzy ranking. The gap is real, but it is deliberately not changed in this
task:

- current exact/substring search is working well for the user;
- the performance report keeps P1-27 open pending real-data measurement;
- a correct FTS implementation adds schema, rebuild, consistency, disk, and
  failure-mode complexity;
- the bundled SQLite supports FTS5/trigram, and a 50,000-row synthetic audit
  showed the approach is feasible, so it remains a measured follow-up if real
  missed-query reports appear.

No FTS table, schema migration, channel import hook, candidate limit, or ranking
behavior is changed now.

## Verification

- Add ViewModel tests for empty, whitespace, punctuation-only, one-character,
  Turkish one-character, two-character, and normalized two-character input.
- Prove an invalid command cannot change a previous committed query, navigate,
  clear result collections, or call `IContentQueryService`.
- Add source/XAML contracts for the localized hint on mobile and desktop and for
  the mobile Search button remaining command-driven.
- Run the focused Search tests, the complete `Noctra.Tests` suite, mobile and
  desktop builds, and an Android phone smoke test.
- Confirm no commit, push, AAB publication, or Play Console update is performed
  as part of this task.
