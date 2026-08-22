# Playback Exit Ad Policy and Resume Recovery Plan

1. Add failing policy/coordinator tests for downloaded playback and persistent
   rolling impression history.
2. Add failing provider/mobile contract tests for shared policy enforcement,
   dismissal-based task completion, and deterministic grid recovery.
3. Extend the ad context and implement the shared Core coordinator plus Android
   SharedPreferences history store.
4. Inject the coordinator into both Android providers, evaluate before show,
   record on open, and complete on dismiss/failure.
5. Track downloaded playback in `MainView`, suppress ineligible preloads, and
   recover active virtualized grids after an interstitial dismissal.
6. Run focused tests, full tests, Android Debug build, install without clearing
   app data, and repeat the Huawei playback-exit smoke test.
