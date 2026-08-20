# P1-20 EPG visible refresh implementation plan

1. Add failing ViewModel tests for bounded snapshot lookup, empty snapshot no-op, and snapshot clearing.
2. Add realized-row source item accessors to mobile and desktop virtualized grids.
3. Wire Live views to publish snapshots and clear them on detach/navigation.
4. Replace the all-loaded timer call with the bounded snapshot and single-flight guard.
5. Run focused and EPG regression tests, then build/review the D:\IPTVPlayer tree and update the report.
