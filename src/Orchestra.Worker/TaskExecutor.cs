using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestra.Infrastructure.Tasks;

namespace Orchestra.Worker;

/// <summary>Claims one task, runs its handler while heartbeating the lease, and reports the outcome.</summary>
public sealed class TaskExecutor(
    ITaskStore store,
    HandlerRegistry handlers,
    IOptions<WorkerOptions> options,
    ILogger<TaskExecutor> logger)
{
    private string WorkerId => options.Value.WorkerId;

    /// <param name="stopping">No new tasks are claimed once this fires.</param>
    /// <param name="abort">Cancels a running handler (forced shutdown after the grace period).</param>
    /// <returns>True if a task was claimed and processed.</returns>
    public async Task<bool> RunOneAsync(CancellationToken stopping, CancellationToken abort)
    {
        if (stopping.IsCancellationRequested) return false;

        var lease = TimeSpan.FromSeconds(options.Value.LeaseSeconds);
        var task = await store.ClaimNextAsync(WorkerId, lease, abort);
        if (task is null) return false;

        using var _ = logger.BeginScope(new Dictionary<string, object>
        {
            ["TaskRunId"] = task.Id, ["WorkflowRunId"] = task.WorkflowRunId, ["Task"] = task.Name
        });

        await ExecuteAsync(task, lease, abort);
        return true;
    }

    private async Task ExecuteAsync(ClaimedTask task, TimeSpan lease, CancellationToken abort)
    {
        var handler = handlers.Find(task.Handler);
        if (handler is null)
        {
            logger.LogError("No handler registered for '{Handler}'.", task.Handler);
            await store.FailAsync(task.Id, WorkerId, $"No handler registered for '{task.Handler}'.", CancellationToken.None);
            return;
        }

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(task.TimeoutSeconds));
        using var leaseLostCts = new CancellationTokenSource();
        using var heartbeatCts = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(abort, timeoutCts.Token, leaseLostCts.Token);

        var heartbeat = HeartbeatLoopAsync(task, lease, leaseLostCts, heartbeatCts.Token);
        var context = new TaskContext(task.Id, task.WorkflowRunId, task.Name, task.Attempt, task.Payload, task.Inputs);

        try
        {
            logger.LogInformation("Running attempt {Attempt} with handler {Handler}.", task.Attempt, handler.Name);
            var result = await handler.ExecuteAsync(context, linked.Token);

            if (leaseLostCts.IsCancellationRequested)
            {
                logger.LogWarning("Lease was lost while the handler ran; discarding its result.");
                return;
            }

            if (!await store.CompleteAsync(task.Id, WorkerId, result.Output, CancellationToken.None))
                logger.LogWarning("Task finished but the lease was no longer held; result discarded.");
            else
                logger.LogInformation("Succeeded.");
        }
        catch (Exception ex)
        {
            if (leaseLostCts.IsCancellationRequested)
            {
                // Someone else owns (or cancelled) this task now. Reporting would be wrong.
                logger.LogWarning("Lease lost; abandoning task.");
                return;
            }

            var error = timeoutCts.IsCancellationRequested
                ? $"Timed out after {task.TimeoutSeconds}s."
                : abort.IsCancellationRequested
                    ? "Worker shut down before the task finished."
                    : $"{ex.GetType().Name}: {ex.Message}";

            logger.LogWarning(ex, "Attempt {Attempt} failed: {Error}", task.Attempt, error);
            await store.FailAsync(task.Id, WorkerId, error, CancellationToken.None);
        }
        finally
        {
            await heartbeatCts.CancelAsync();
            await heartbeat;
        }
    }

    private async Task HeartbeatLoopAsync(ClaimedTask task, TimeSpan lease, CancellationTokenSource leaseLost, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(lease / 3);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                bool held;
                try
                {
                    held = await store.HeartbeatAsync(task.Id, WorkerId, lease, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Transient DB error: keep trying; the lease has two more intervals of slack.
                    logger.LogWarning(ex, "Heartbeat failed; will retry.");
                    continue;
                }

                if (!held)
                {
                    await leaseLost.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException) { /* handler finished */ }
    }
}
