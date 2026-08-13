# Mobile Image Decode Profiles Implementation Plan

Date: 2026-08-13  
Design: `docs/superpowers/specs/2026-08-13-mobile-image-decode-profiles-design.md`

1. Add failing pure tests for `MobileImageDecodePolicy` and link the policy source into
   `Noctra.Tests`.
2. Add failing XAML contract tests for Live, VOD, Series, and Continue Watching profiles.
3. Implement the minimal profile enum and bounded/quantized resolver.
4. Integrate profile resolution into `MobileRemoteImage` without changing existing explicit
   decode behavior, cache keys, cancellation, or stale commit guards.
5. Assign the four profiles in card XAML.
6. Run focused image/profile tests, existing Android performance contracts, repeated lifecycle
   regressions, build verification, and the full test suite.
7. Review the diff for Critical/Important regressions.
8. Build/install an arm64 Android APK without deleting app data and run visual, scroll,
   navigation, lifecycle, memory, and crash/ANR acceptance.
9. Record exact evidence in the main performance report.

