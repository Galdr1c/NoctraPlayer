# P1-18 Database Work Scheduler Implementation Plan

1. Add RED tests for read/write concurrency, priority fairness, pending/active cancellation,
   exception propagation and Schedule/Dispose races.
2. Add `IDatabaseWorkScheduler`, lane/priority contracts and validated scheduler options.
3. Implement bounded priority queues with dedicated read/write workers and exact-once
   terminal/slot ownership.
4. Register the scheduler as a production singleton.
5. Inject it into `ContentQueryService`, pass scheduler cancellation through every query and
   remove direct per-query `Task.Run`.
6. Extend DI and ContentQuery scheduling contracts.
7. Run focused tests repeatedly, independent production review, full regression and Android
   live acceptance; record evidence in the main report.

