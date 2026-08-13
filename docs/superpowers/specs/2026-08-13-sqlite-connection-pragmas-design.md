# P1-19 SQLite connection PRAGMA design

## Goal

Make every EF Core factory connection use the intended SQLite connection-scoped tuning instead of relying on the one connection used by schema fix-up.

## Design

- Register one singleton EF Core `DbConnectionInterceptor` with the `AddDbContextFactory<AppDbContext>` options.
- Apply `foreign_keys`, `synchronous`, `cache_size`, `temp_store`, and `mmap_size` immediately after every synchronous or asynchronous connection open.
- Keep `journal_mode=WAL` in schema initialization. WAL is database/file scoped and repeatedly negotiating it on every open can add locking work.
- Select desktop or mobile cache/mmap limits through an immutable DI option. Core defaults to desktop; Android registers the mobile profile before core services.
- Treat configuration failure as a connection-open failure. A connection must not silently enter the pool with inconsistent settings.
- When performance tracing is enabled, read the values back from that same active connection and emit applied/verified telemetry.

## Acceptance

- Two independently created factory contexts both expose the expected PRAGMA values.
- A deliberately changed pooled connection is restored on its next open.
- Android resolves the mobile profile; desktop resolves the desktop profile.
- Telemetry contains values read from an active factory connection.
- Existing schema fix-up remains idempotent and WAL remains enabled there.

