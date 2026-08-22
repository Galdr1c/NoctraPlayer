# Search Live-First Design

## Problem

Search channels are loaded incrementally. The first database page mixes Live and VOD channels using the normal page sort, so a matching VOD item can occupy the first page while matching Live channels arrive only after scrolling to a later page.

## Selected Behavior

When SearchText is non-empty and no explicit ChannelType filter is supplied, channel pages are ordered by Live type first, then by the user's existing sort order and stable channel ID tie-breaker. This ensures the first search page can populate SearchLiveChannels without changing normal Live, Movies, or filtered group page behavior.

Series remain in their existing separate search bucket. UI section order remains Live TV, Series, Movies, then Similar Results.

## Safety

The priority is applied only to search requests with Type == null. Keyset cursor and Skip/Take pagination operate on the same deterministic ordering, so later pages do not duplicate or reorder already-loaded results.

## Verification

Integration tests will prove a mixed Live/VOD search page returns Live first while non-search sorting remains unchanged. The full test suite and both desktop/Android targets will be built.
