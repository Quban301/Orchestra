using Orchestra.Domain;

namespace Orchestra.Domain.Tests;

public class RetryRunTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    private static WorkflowRun FailedRun()
    {
        // a -> b -> c, plus independent d. 'b' fails permanently, so 'c' is skipped.
        var run = T.Run(T.Task("a"), T.Task("b", "a"), T.Task("c", "b"), T.Task("d"));
        RunPlanner.Advance(run, T.Now);

        foreach (var name in new[] { "a", "d" })
        {
            run.Get(name).TryClaim("w", T.Now, Lease);
            run.Get(name).Complete("w", $"out-{name}", T.Now);
        }
        RunPlanner.Advance(run, T.Now);

        var b = run.Get("b");
        b.MaxAttempts = 1;
        b.TryClaim("w", T.Now, Lease);
        b.Fail("w", "boom", T.Now);
        RunPlanner.Advance(run, T.Now);
        Assert.Equal(RunStatus.Failed, run.Status);
        return run;
    }

    [Fact]
    public void RetryFailed_resets_failed_and_skipped_but_keeps_succeeded()
    {
        var run = FailedRun();

        Assert.True(run.RetryFailed());

        Assert.Equal(RunStatus.Running, run.Status);
        Assert.Null(run.CompletedAt);
        Assert.Equal(TaskRunStatus.Succeeded, run.Get("a").Status);
        Assert.Equal("out-a", run.Get("a").Output);
        Assert.Equal(TaskRunStatus.Succeeded, run.Get("d").Status);
        Assert.Equal(TaskRunStatus.Pending, run.Get("b").Status);
        Assert.Equal(0, run.Get("b").Attempt);
        Assert.Null(run.Get("b").Error);
        Assert.Equal(TaskRunStatus.Pending, run.Get("c").Status);
    }

    [Fact]
    public void After_retry_the_planner_requeues_only_what_is_ready()
    {
        var run = FailedRun();
        run.RetryFailed();

        RunPlanner.Advance(run, T.Now.AddMinutes(1));

        Assert.Equal(TaskRunStatus.Queued, run.Get("b").Status);
        Assert.Equal(TaskRunStatus.Pending, run.Get("c").Status);
        Assert.Equal(RunStatus.Running, run.Status);
    }

    [Fact]
    public void Retried_run_can_complete_successfully()
    {
        var run = FailedRun();
        run.RetryFailed();

        foreach (var name in new[] { "b", "c" })
        {
            RunPlanner.Advance(run, T.Now);
            run.Get(name).TryClaim("w", T.Now, Lease);
            run.Get(name).Complete("w", null, T.Now);
        }
        RunPlanner.Advance(run, T.Now);

        Assert.Equal(RunStatus.Succeeded, run.Status);
    }

    [Theory]
    [InlineData(RunStatus.Running)]
    [InlineData(RunStatus.Succeeded)]
    [InlineData(RunStatus.Cancelled)]
    public void RetryFailed_is_rejected_unless_run_failed(RunStatus status)
    {
        var run = T.Run(T.Task("a"));
        run.Status = status;
        Assert.False(run.RetryFailed());
        Assert.Equal(status, run.Status);
    }

    [Fact]
    public void Reset_only_applies_to_failed_or_skipped_tasks()
    {
        var queued = new TaskRun();
        queued.MarkQueued(T.Now);
        Assert.False(queued.Reset());

        var skipped = new TaskRun();
        skipped.Skip(T.Now);
        Assert.True(skipped.Reset());
        Assert.Equal(TaskRunStatus.Pending, skipped.Status);
    }
}
