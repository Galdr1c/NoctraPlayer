# Mobile Image Decode Profiles Design

Date: 2026-08-13  
Scope: P1-08 and P1-09

## Problem

`Noctra.Mobile.Controls.RemoteImage` uses a single 384 pixel decode width unless a caller
provides an override. This is wasteful for 36 DIP Live logos and other small images. A fixed
smaller poster value is not safe either: the DBY_W09 test device renders a 180 DIP poster at
roughly 450 physical pixels because its render scale is 2.5.

The root problem is therefore not simply “384 is always too large.” It is that image role,
logical display width, and render scale are absent from decode selection.

## Decision

Add a pure, testable `MobileImageDecodePolicy` and an `ImageDecodeProfile` property on
`RemoteImage`.

Profiles used in this phase:

- `Default`: preserve the explicit/current `DecodePixelWidth` behavior.
- `LiveLogo`: 64–96 physical pixels.
- `PosterSmall`: 160–384 physical pixels.
- `Backdrop`: 256–768 physical pixels.

Profile widths are calculated from `logical width × TopLevel.RenderScaling`, rounded upward
to a small fixed set of buckets, then clamped to the profile limits. Bucketed widths preserve
cache sharing and prevent minor layout changes from producing new cache keys or reload churn.

## Control lifecycle

For profiled images, `RemoteImage` resolves logical width from an explicit `Width` first and
from arranged `Bounds.Width` otherwise. When neither is usable, it defers loading until layout
provides a stable width. `SizeChanged` only restarts a request when the resolved decode bucket
actually changes.

URL, surface activity, foreground state, visibility, cancellation, shared-load coordination,
and stale-commit rules remain unchanged. Existing explicit `DecodePixelWidth="960"` detail
usage remains on the `Default` profile and therefore keeps its current behavior.

## Card mapping

- `MobileLiveTvCard` → `LiveLogo`
- `MobileVodCard` → `PosterSmall`
- `MobileSeriesCard` → `PosterSmall`
- `MobileContinueWatchingCard` → `Backdrop`

Search, Favorites, My List, History, Live, Movies, and Series inherit these mappings because
they all construct the same recycled card controls through `MobileCardRowPresenter`.

## Expected effect

On DBY_W09 (2.5 render scale), a 36 DIP logo resolves to 96 px instead of 384 px, reducing
decoded pixel area from roughly 147,456 to 9,216 for a square source (16× reduction). A
180 DIP poster resolves to the 384 px cap rather than being reduced blindly and blurred. On
lower-density devices, the same poster can resolve to 192, 256, or 320 px, improving effective
cache capacity without sacrificing its physical display target.

## Verification

1. Pure policy tests cover profile bounds, render scaling, bucketing, invalid dimensions, and
   explicit/default compatibility.
2. Contract tests require every primary mobile card to declare the intended profile.
3. Existing image cancellation/cache/lifecycle tests must stay green.
4. Android acceptance must verify real catalog images remain visually correct, the process is
   stable during scroll/navigation/background-resume, and no ANR/crash/OOM is logged.

## Non-goals

- Streaming image bytes instead of buffering (P1-10).
- Cache ownership/commit policy changes (P1-07/P1-12/P1-13/P1-14).
- Desktop `RemoteImage` behavior.
- Detail/backdrop source URL selection or TMDB enrichment behavior.

