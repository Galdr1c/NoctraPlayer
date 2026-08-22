# Next Episode Eligibility Design

## Problem

The shared PlayerEpisodeNavigator calculates a three-minute fallback as max(0, duration - 180). For episodes at or below three minutes this becomes zero, so the first position callback satisfies position >= threshold and opens the next-episode prompt immediately.

## Goal

Show the prompt only in a defensible end-of-episode window, preserve the long-content 180-second tail cap and existing end-of-stream behavior, and prevent missing or invalid duration/metadata from triggering a countdown at playback start. Desktop and Android must use the same Core calculation.

## Selected Algorithm

1. The eligibility calculation requires a finite positive player duration. Metadata alone is not sufficient because it cannot be validated against the actual media length.
2. Calculate a dynamic tail window: 6 percent of duration, clamped between 25 and 180 seconds.
3. Calculate the tail trigger as duration minus the tail window.
4. Use the tail trigger as the sole duration-derived trigger. The 180-second maximum tail bound already gives long content the same three-minute upper limit without a discontinuous duration-minus-three-minutes rule.
5. If CreditsStartSec is finite, positive, and within the media duration, use it only when it is later than the safe tail trigger. A bad early metadata value cannot move the prompt into the opening portion of the episode.
6. If the resulting trigger is not strictly positive, report that automatic position-based eligibility is unavailable. The explicit playback-end handler remains responsible for end-of-stream behavior.

## Behavior Examples

- 120 seconds: tail 25 seconds, trigger 95 seconds.
- 180 seconds: tail 25 seconds, trigger 155 seconds.
- 600 seconds: tail 36 seconds, trigger 564 seconds.
- 2700 seconds: tail 162 seconds, trigger 2538 seconds.
- 3000 seconds and longer: the tail clamp reaches its 180-second maximum.
- Missing/zero duration: no position-based prompt, even if CreditsStartSec exists.

## State and UI

No UI changes are needed. CheckIntroCreditsPosition remains the single position callback, and the existing cancellation, rewind, generation, and transition guards remain unchanged. The same Core method feeds Avalonia and Mobile bindings.

## Testing

Add regression tests for 120/180-second episodes, long-episode compatibility, invalid early CreditsStartSec, and missing duration. Verify that position zero does not show the prompt and that the calculated safe trigger does.
