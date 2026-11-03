Orchestra

A distributed workflow and job orchestration platform, in the spirit of a small-scale Temporal or Airflow. Workflows are defined as directed acyclic graphs of tasks. A scheduler resolves dependencies and a cluster of workers executes them reliably.

Highlights
DAG validation: detects duplicate tasks, unknown dependencies and cycles before a workflow is accepted
Reliable execution: lease-based task claiming (FOR UPDATE SKIP LOCKED), heartbeats, and automatic recovery when a worker dies
Retries: exponential backoff with jitter, per-task attempt limits and timeouts
Failure handling: a permanent failure skips downstream tasks and fails the run
Graceful shutdown: in-flight tasks get a grace period to finish before being cancelled
Pluggable handlers: add tasks by implementing ITaskHandler
Tested domain core: the state machine and planner are pure logic with unit tests

Stack: C# / .NET 10, ASP.NET Core, EF Core, PostgreSQL, xUnit. Planned: REST API with auth, React + TypeScript dashboard, OpenTelemetry.