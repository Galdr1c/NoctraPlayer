# P1-19 SQLite connection PRAGMA implementation plan

1. Add failing integration tests for desktop/mobile factory connections, reopen behavior, and active-connection telemetry.
2. Add immutable tuning options and a sync/async EF Core connection interceptor.
3. Register the interceptor centrally in the context factory and select the mobile profile in Android DI.
4. Run focused tests repeatedly, then the related regression suite and platform builds.
5. Review the production diff, update the main report, build/install the APK without clearing app data, and run live navigation/background acceptance.

