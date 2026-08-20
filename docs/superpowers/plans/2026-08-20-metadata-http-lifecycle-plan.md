# Metadata HTTP lifecycle plan

1. Add red disposal and per-attempt-timeout tests.
2. Add the 20-second linked request timeout in `MetadataService.SendGetAsync`.
3. Dispose responses at all JSON-consumption call sites and failed fallback ownership paths.
4. Run focused metadata/cancellation tests, the full suite, Android build and data-preserving smoke.
5. Update the main performance report with evidence and the next open item.
