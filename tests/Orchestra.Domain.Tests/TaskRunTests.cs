using Orchestra.Domain;

namespace Orchestra.Domain.Tests;

public class TaskRunTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    private static TaskRun Queued(int maxAttempts = 3)
    {
        var t = new TaskRun { Name = "t", MaxAttempts = maxAttempts };
        t.MarkQueued(T.Now);
        return t;
    }

    [Fact]
    public void Claim_moves_to_running_and_sets_lease()
    {
        var t = Queued();
        Assert.True(t.TryClaim("w1", T.Now, Lease));
        Assert.Equal(TaskRunStatus.Running, t.Status);
        Assert.Equal(1, t.Attempt);
        Assert.Equal(T.Now + Lease, t.LeaseExpiresAt);
    }

    [Fact]
    public void Running_task_cannot_be_claimed_twice()
    {
        var t = Queued();
        t.TryClaim("w1", T.Now, Lease);
        Assert.False(t.TryClaim("w2", T.Now, Lease));
    }

    [Fact]
    public void Failure_requeues_with_backoff_then_blocks_early_claims()
    {
        var t = Queued();
        t.TryClaim("w1", T.Now, Lease);
        Assert.True(t.Fail("w1", "boom", T.Now));

        Assert.Equal(TaskRunStatus.Queued, t.Status);
        Assert.Equal(T.Now + TimeSpan.FromSeconds(2), t.NextAttemptAt);
        Assert.False(t.TryClaim("w2", T.Now.AddSeconds(1), Lease));
        Assert.True(t.TryClaim("w2", T.Now.AddSeconds(2), Lease));
        Assert.Equal(2, t.Attempt);
    }

    [Fact]
    public void Failure_on_last_attempt_is_permanent()
    {
        var t = Queued(maxAttempts: 1);
        t.TryClaim("w1", T.Now, Lease);
        t.Fail("w1", "boom", T.Now);
        Assert.Equal(TaskRunStatus.Failed, t.Status);
        Assert.Equal("boom", t.Error);
        Assert.NotNull(t.CompletedAt);
    }

    [Fact]
    public void Stale_worker_cannot_complete_or_fail()
    {
        var t = Queued();
        t.TryClaim("w1", T.Now, Lease);
        Assert.False(t.Complete("someone-else", "x", T.Now));
        Assert.False(t.Fail("someone-else", "x", T.Now));
        Assert.Equal(TaskRunStatus.Running, t.Status);
    }

    [Fact]
    public void Heartbeat_extends_lease_only_for_owner_before_expiry()
    {
        var t = Queued();
        t.TryClaim("w1", T.Now, Lease);

        Assert.True(t.Heartbeat("w1", T.Now.AddSeconds(10), Lease));
        Assert.Equal(T.Now.AddSeconds(40), t.LeaseExpiresAt);
        Assert.False(t.Heartbeat("w2", T.Now.AddSeconds(11), Lease));
        Assert.False(t.Heartbeat("w1", T.Now.AddSeconds(100), Lease));
    }

    [Fact]
    public void Expired_lease_counts_as_failed_attempt_and_requeues()
    {
        var t = Queued();
        t.TryClaim("w1", T.Now, Lease);

        Assert.False(t.ExpireLease(T.Now.AddSeconds(29)));
        Assert.True(t.ExpireLease(T.Now.AddSeconds(30)));
        Assert.Equal(TaskRunStatus.Queued, t.Status);
        Assert.Null(t.LeaseOwner);
        Assert.Contains("Lease expired", t.Error);
    }

    [Fact]
    public void Complete_succeeds_and_clears_lease()
    {
        var t = Queued();
        t.TryClaim("w1", T.Now, Lease);
        Assert.True(t.Complete("w1", "{\"ok\":true}", T.Now));
        Assert.Equal(TaskRunStatus.Succeeded, t.Status);
        Assert.Null(t.LeaseOwner);
    }
}
