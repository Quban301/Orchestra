namespace Orchestra.Infrastructure.Tasks;

/// <summary>A task a worker has successfully leased, detached from EF so it can cross async scopes.</summary>
public sealed record ClaimedTask(
    Guid Id,
    Guid WorkflowRunId,
    string Name,
    string Handler,
    int Attempt,
    int TimeoutSeconds,
    string? Payload,
    IReadOnlyDictionary<string, string?> Inputs);

/// <summary>Everything a worker needs from persistence. Every mutating call is fenced by the worker id (lease owner).</summary>
public interface ITaskStore
{
    /// <summary>Atomically leases the next due task, or returns null if none is available.</summary>
    Task<ClaimedTask?> ClaimNextAsync(string workerId, TimeSpan lease, CancellationToken ct);

    /// <summary>Extends the lease. False means the lease is lost (expired, reassigned or run cancelled).</summary>
    Task<bool> HeartbeatAsync(Guid taskId, string workerId, TimeSpan lease, CancellationToken ct);

    Task<bool> CompleteAsync(Guid taskId, string workerId, string? output, CancellationToken ct);

    Task<bool> FailAsync(Guid taskId, string workerId, string error, CancellationToken ct);
}
