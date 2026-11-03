namespace Orchestra.Worker;

public sealed record TaskContext(
    Guid TaskRunId,
    Guid WorkflowRunId,
    string TaskName,
    int Attempt,
    string? Payload,
    IReadOnlyDictionary<string, string?> Inputs);

public sealed record TaskResult(string? Output = null);

/// <summary>
/// A unit of work, looked up by <see cref="Name"/> from a task definition's Handler field.
/// Handlers may run more than once for the same task (retries, lease loss), so make them idempotent.
/// The cancellation token fires on timeout, lost lease, or forced shutdown.
/// </summary>
public interface ITaskHandler
{
    string Name { get; }
    Task<TaskResult> ExecuteAsync(TaskContext context, CancellationToken ct);
}

public sealed class HandlerRegistry
{
    private readonly Dictionary<string, ITaskHandler> _handlers;

    public HandlerRegistry(IEnumerable<ITaskHandler> handlers)
        => _handlers = handlers.ToDictionary(h => h.Name, StringComparer.OrdinalIgnoreCase);

    public ITaskHandler? Find(string name) => _handlers.GetValueOrDefault(name);
}
