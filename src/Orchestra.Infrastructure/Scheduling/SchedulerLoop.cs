using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orchestra.Domain;

namespace Orchestra.Infrastructure.Scheduling;

/// <summary>
/// Background coordinator. Every tick it reaps expired task leases, promotes ready tasks and rolls up
/// run status. Safe to run on several nodes: updates are guarded by optimistic concurrency, and a
/// conflicting run is simply retried on the next tick.
/// </summary>
public sealed class SchedulerLoop(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<SchedulerLoop> logger) : BackgroundService
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TickInterval);
        do
        {
            try { await TickAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "Scheduler tick failed."); }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }

    public async Task<int> TickAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
        var now = clock.GetUtcNow();
        var changedRuns = 0;

        var runs = await db.WorkflowRuns
            .Include(r => r.Tasks)
            .Where(r => r.Status == RunStatus.Pending || r.Status == RunStatus.Running)
            .OrderBy(r => r.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var run in runs)
        {
            foreach (var task in run.Tasks)
                if (task.ExpireLease(now, Random.Shared.NextDouble()))
                    logger.LogWarning("Lease expired for task {Task} in run {Run}.", task.Name, run.Id);

            RunPlanner.Advance(run, now);

            try
            {
                if (await db.SaveChangesAsync(ct) > 0) changedRuns++;
            }
            catch (DbUpdateConcurrencyException)
            {
                // A worker updated a task concurrently; drop our stale view and retry next tick.
                logger.LogDebug("Concurrency conflict on run {Run}; will retry.", run.Id);
                db.ChangeTracker.Clear();
            }
        }

        return changedRuns;
    }
}
