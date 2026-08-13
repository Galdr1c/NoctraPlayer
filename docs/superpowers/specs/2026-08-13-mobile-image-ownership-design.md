# Mobile Image Ownership and Cache Commit Design

Date: 2026-08-13
Scope: P1-07, P1-12, P1-13, P1-14

## Context

The mobile image pipeline already bounds distinct work and decoded image size. Its remaining
memory risk is ownership: the LRU stores raw `Bitmap` instances, eviction only drops the cache
reference, and each `Image.Source` keeps the same bitmap alive without an explicit consumer
lifetime. A completed load can also publish to cache immediately before its last consumer is
cancelled.

## Goals

- Never dispose a bitmap while a visible control still owns it.
- Dispose a bitmap deterministically after both cache ownership and all consumer ownership end.
- Do not publish a completed load when no consumer successfully claims the result.
- Clear a recycled control's stale source as soon as its effective URL/request identity changes.
- Release the visible lease on detach, inactive surface, empty URL, and replacement.
- Preserve a source only when the normalized URL and decode bucket are exactly the same.
- Emit owned-native-byte, peak-byte, and live-consumer telemetry.

## Non-goals

- Desktop `RemoteImage` changes.
- HTTP response-size and streaming-buffer changes (P1-10/P1-11).
- Android `OnTrimMemory` policy (P1-17).
- Changes to image fade/dispatcher priority (P1-15/P1-16).

## Considered approaches

### 1. Dispose raw bitmaps during LRU eviction

Rejected. A raw bitmap may still be referenced by one or more `Image.Source` properties. Eviction
could invalidate an image that is currently rendered.

### 2. Increase the cache budget or clear the cache more often

Rejected. This only moves the failure point and cannot distinguish useful visible images from
abandoned work.

### 3. Reference-counted resource with explicit cache and consumer leases

Selected. The decoded resource starts with one owner. Successful cache publication transfers that
owner to the cache. Each visible control obtains a consumer lease. Eviction releases only the cache
owner; the bitmap is disposed when the final consumer lease also ends.

## Architecture

### Shared image resource

`SharedImageResource<T>` owns one decoded value and its estimated native size. It provides
idempotent consumer leases and disposes the value exactly once when its reference count reaches
zero. Static counters for the bitmap resource type report current/peak owned bytes, resource count,
and active consumer leases.

### LRU release contract

`ByteBudgetLruCache` gains an optional release callback. Evicted entries are removed under the cache
gate, but their callbacks run after the gate is released. A projection/read operation executes under
the cache gate so a consumer can atomically acquire a lease before eviction can release the cache
owner.

### Shared-load publication contract

The load coordinator keeps the producer owner until a non-cancelled consumer successfully projects
the completed value into a lease. The first successful claim may publish the producer owner to the
cache. If every consumer cancels, or publication is rejected, the coordinator releases the producer
owner after the final consumer exits. Publication and abandonment are exactly-once operations.

### RemoteImage source contract

`RemoteImage` stores the current consumer lease and effective cache key. A URL or bucket change
clears `Source` and releases the old lease before starting the replacement load. The same effective
key may retain its source to avoid flicker. Stale UI callbacks dispose the lease they failed to apply.
Detach, inactive surface, empty URL, and explicit replacement always clear and release ownership.

## Concurrency invariants

1. A resource owner is transferred to cache at most once.
2. An uncommitted resource owner is released exactly once.
3. A consumer lease is acquired before the coordinator decrements that consumer.
4. Cache eviction cannot race between cache lookup and consumer lease acquisition.
5. A stale or cancelled dispatcher callback cannot leak its lease.
6. An old completion cannot remove or publish a newer same-key entry.
7. User callbacks and value disposal do not execute while the coordinator/cache gate is held unless
   the callback is the bounded, non-reentrant lease increment operation.

## Acceptance

- Focused tests prove abandoned loads never publish and release exactly once.
- LRU tests prove eviction releases cache ownership and an acquired consumer survives eviction.
- Source-policy tests prove URL change, detach, and inactive surface clear ownership; same identity
  preservation remains explicit.
- Existing image scheduling/decode tests remain green, including repeated stress runs.
- Android APK installs with `-r -d` without clearing user data.
- Movies, Series, Live, and Search survive repeated scroll/navigation/background-resume cycles with
  no ANR/crash and without monotonic native/PSS growth.

