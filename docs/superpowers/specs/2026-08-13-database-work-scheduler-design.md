# P1-18 Database Work Scheduler Design

## Problem

`ContentQueryService` currently creates one `Task.Run` per query. This keeps synchronous
SQLite work off the UI thread, but paging, Search and History can create an unbounded
ThreadPool/SQLite backlog. Other database producers will need the same process-wide
coordination in later report items.

## Design

- Register one `IDatabaseWorkScheduler` singleton for the application process.
- Keep dedicated bounded lanes: three read workers and one write worker.
- Use a shared pending-capacity gate so admission never blocks the caller thread and the
  backlog cannot grow without a limit.
- Maintain interactive/background queues per lane. Interactive work runs first, but a
  bounded burst forces an eligible background item so background persistence cannot starve.
- Caller cancellation removes pending work before it starts. Active work receives a token
  linked to application shutdown; its returned task represents the complete worker lifetime.
- Dispose atomically stops admission, terminalizes pending work and cancels active workers.
  Semaphore release and cancellation-registration cleanup happen outside unsafe lock cycles.
- Route every `ContentQueryService` operation through the interactive read lane and remove
  its per-call `Task.Run`. The service passes the scheduler token end-to-end to EF/provider
  calls.

## Acceptance

- Read concurrency never exceeds 3; write concurrency never exceeds 1.
- Interactive work overtakes queued background work; background work still runs within the
  configured interactive burst limit.
- Pending cancellation never invokes the delegate; active cancellation propagates.
- Exceptions propagate to the scheduled task, and Schedule/Dispose races always terminate.
- `ContentQueryService` still executes SQLite work away from the caller/UI thread.
- Production DI resolves one shared scheduler instance and `ContentQueryService` uses it.

