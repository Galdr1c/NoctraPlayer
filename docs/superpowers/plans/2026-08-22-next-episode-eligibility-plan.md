# Next Episode Eligibility Implementation Plan

1. Add failing PlayerEpisodeNavigator tests for short duration, boundary duration, long-duration tail-cap compatibility, invalid early credits metadata, and unknown duration.
2. Run the focused tests and confirm the failures reproduce the zero-threshold behavior.
3. Update TryGetCreditsTriggerThreshold with finite-duration validation, one continuous 25–180 second tail window, and safe metadata clamping.
4. Re-run focused player tests, then the full Noctra.Tests suite.
5. Build desktop test target and Android Debug target; inspect the final diff for unrelated changes.
