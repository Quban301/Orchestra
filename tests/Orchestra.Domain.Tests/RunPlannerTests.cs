using Orchestra.Domain;

namespace Orchestra.Domain.Tests;

public class RunPlannerTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    private static void Succeed(TaskRun t)
    {
        t.TryClaim("w", T.Now, Lease);
        t.Complete("w", null, T.Now);
    }

    private static void FailPermanently(TaskRun t)
    {
        t.MaxAttempts = 1;
        t.TryClaim("w", T.Now, Lease);
        t.Fail("w", "boom", T.Now);
    }

    [Fact]
    public void First_advance_queues_only_roots_and_starts_run()
    {
        var run = T.Run(T.Task("a"), T.Task("b", "a"), T.Task("c"));
        RunPlanner.Advance(run, T.Now);

        Assert.Equal(RunStatus.Running, run.Status);
        Assert.Equal(T.Now, run.StartedAt);
        Assert.Equal(TaskRunStatus.Queued, run.Get("a").Status);
        Assert.Equal(TaskRunStatus.Pending, run.Get("b").Status);
        Assert.Equal(TaskRunStatus.Queued, run.Get("c").Status);
    }

    [Fact]
    public void Dependents_wait_for_all_dependencies()
    {
        var run = T.Run(T.Task("a"), T.Task("b"), T.Task("c", "a", "b"));
        RunPlanner.Advance(run, T.Now);

        Succeed(run.Get("a"));
        RunPlanner.Advance(run, T.Now);
        Assert.Equal(TaskRunStatus.Pending, run.Get("c").Status);

        Succeed(run.Get("b"));
        RunPlanner.Advance(run, T.Now);
        Assert.Equal(TaskRunStatus.Queued, run.Get("c").Status);
    }

    [Fact]
    public void Run_succeeds_when_all_tasks_succeed()
    {
        var run = T.Run(T.Task("a"), T.Task("b", "a"));
        RunPlanner.Advance(run, T.Now);
        Succeed(run.Get("a"));
        RunPlanner.Advance(run, T.Now);
        Succeed(run.Get("b"));
        RunPlanner.Advance(run, T.Now);

        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.NotNull(run.CompletedAt);
    }

    [Fact]
    public void Failure_skips_downstream_transitively_and_fails_run()
    {
        var run = T.Run(T.Task("a"), T.Task("b", "a"), T.Task("c", "b"), T.Task("independent"));
        RunPlanner.Advance(run, T.Now);

        FailPermanently(run.Get("a"));
        Succeed(run.Get("independent"));
        RunPlanner.Advance(run, T.Now);

        Assert.Equal(TaskRunStatus.Skipped, run.Get("b").Status);
        Assert.Equal(TaskRunStatus.Skipped, run.Get("c").Status);
        Assert.Equal(RunStatus.Failed, run.Status);
    }

    [Fact]
    public void Run_stays_running_while_a_task_is_retrying()
    {
        var run = T.Run(T.Task("a"));
        RunPlanner.Advance(run, T.Now);
        run.Get("a").TryClaim("w", T.Now, Lease);
        run.Get("a").Fail("w", "transient", T.Now);
        RunPlanner.Advance(run, T.Now);

        Assert.Equal(RunStatus.Running, run.Status);
        Assert.Equal(TaskRunStatus.Queued, run.Get("a").Status);
    }

    [Fact]
    public void Cancel_stops_everything_and_planner_is_then_a_no_op()
    {
        var run = T.Run(T.Task("a"), T.Task("b", "a"));
        RunPlanner.Advance(run, T.Now);
        run.Cancel(T.Now);
        RunPlanner.Advance(run, T.Now.AddMinutes(1));

        Assert.Equal(RunStatus.Cancelled, run.Status);
        Assert.All(run.Tasks, t => Assert.Equal(TaskRunStatus.Cancelled, t.Status));
    }

    [Fact]
    public void WorkflowDefinition_Create_rejects_invalid_dag()
        => Assert.Throws<InvalidOperationException>(() =>
            WorkflowDefinition.Create("bad", new[] { T.Task("a", "b"), T.Task("b", "a") }, T.Now));
}
