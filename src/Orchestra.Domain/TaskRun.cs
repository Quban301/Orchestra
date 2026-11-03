namespace Orchestra.Domain;

/// <summary>
/// One task inside a workflow run. All state transitions live here so they can be unit tested
/// without a database. Time is always passed in explicitly.
/// </summary>
public class TaskRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowRunId { get; set; }
    public string Name { get; set; } = "";
    public string Handler { get; set; } = "";
    public List<string> DependsOn { get; set; } = new();
    public TaskRunStatus Status { get; set; } = TaskRunStatus.Pending;

    /// <summary>Number of times this task has been claimed by a worker.</summary>
    public int Attempt { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 300;

    public string? Payload { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }

    /// <summary>Earliest time a Queued task may be claimed (used for retry backoff).</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public bool IsTerminal => Status is TaskRunStatus.Succeeded or TaskRunStatus.Failed
        or TaskRunStatus.Skipped or TaskRunStatus.Cancelled;

    public bool MarkQueued(DateTimeOffset now)
    {
        if (Status != TaskRunStatus.Pending) return false;
        Status = TaskRunStatus.Queued;
        NextAttemptAt = now;
        return true;
    }

    public bool TryClaim(string owner, DateTimeOffset now, TimeSpan lease)
    {
        if (Status != TaskRunStatus.Queued) return false;
        if (NextAttemptAt is { } due && due > now) return false;

        Status = TaskRunStatus.Running;
        Attempt++;
        LeaseOwner = owner;
        LeaseExpiresAt = now + lease;
        StartedAt ??= now;
        return true;
    }

    public bool Heartbeat(string owner, DateTimeOffset now, TimeSpan lease)
    {
        if (!HoldsLease(owner, now)) return false;
        LeaseExpiresAt = now + lease;
        return true;
    }

    public bool Complete(string owner, string? output, DateTimeOffset now)
    {
        if (Status != TaskRunStatus.Running || LeaseOwner != owner) return false;
        Status = TaskRunStatus.Succeeded;
        Output = output;
        Error = null;
        CompletedAt = now;
        ClearLease();
        return true;
    }

    /// <summary>Reports a failure from the worker holding the lease. Re-queues with backoff or fails permanently.</summary>
    public bool Fail(string owner, string error, DateTimeOffset now, double jitter = 0)
    {
        if (Status != TaskRunStatus.Running || LeaseOwner != owner) return false;
        ApplyFailure(error, now, jitter);
        return true;
    }

    /// <summary>Called by the scheduler: if a worker stopped heartbeating, treat it as a failed attempt.</summary>
    public bool ExpireLease(DateTimeOffset now, double jitter = 0)
    {
        if (Status != TaskRunStatus.Running || LeaseExpiresAt is null || LeaseExpiresAt > now) return false;
        ApplyFailure("Lease expired (worker stopped responding).", now, jitter);
        return true;
    }

    public bool Skip(DateTimeOffset now)
    {
        if (IsTerminal) return false;
        Status = TaskRunStatus.Skipped;
        CompletedAt = now;
        ClearLease();
        return true;
    }

    public bool Cancel(DateTimeOffset now)
    {
        if (IsTerminal) return false;
        Status = TaskRunStatus.Cancelled;
        CompletedAt = now;
        ClearLease();
        return true;
    }

    private bool HoldsLease(string owner, DateTimeOffset now) =>
        Status == TaskRunStatus.Running && LeaseOwner == owner && LeaseExpiresAt > now;

    private void ApplyFailure(string error, DateTimeOffset now, double jitter)
    {
        Error = error;
        ClearLease();
        if (Attempt < MaxAttempts)
        {
            Status = TaskRunStatus.Queued;
            NextAttemptAt = now + RetryPolicy.Delay(Attempt, jitter);
        }
        else
        {
            Status = TaskRunStatus.Failed;
            CompletedAt = now;
        }
    }

    private void ClearLease()
    {
        LeaseOwner = null;
        LeaseExpiresAt = null;
    }
}
