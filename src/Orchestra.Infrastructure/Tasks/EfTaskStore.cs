using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orchestra.Domain;

namespace Orchestra.Infrastructure.Tasks;

public sealed class EfTaskStore(IServiceScopeFactory scopes, TimeProvider clock) : ITaskStore
{
    private const int MaxConcurrencyRetries = 5;

    public async Task<ClaimedTask?> ClaimNextAsync(string workerId, TimeSpan lease, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
        var now = clock.GetUtcNow();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // FOR UPDATE SKIP LOCKED lets many workers poll the same table without blocking each other:
        // rows another worker is mid-claim on are simply skipped. xmin is selected explicitly because
        // "t.*" does not include system columns, and EF maps it as the concurrency token.
        var candidate = (await db.TaskRuns.FromSql($"""
            SELECT t.*, t.xmin
            FROM task_runs t
            WHERE t."Status" = 'Queued'
              AND (t."NextAttemptAt" IS NULL OR t."NextAttemptAt" <= {now})
            ORDER BY t."NextAttemptAt"
            LIMIT 1
            FOR UPDATE SKIP LOCKED
            """).AsTracking().ToListAsync(ct)).FirstOrDefault();

        if (candidate is null) return null;
        if (!candidate.TryClaim(workerId, now, lease)) return null;

        await db.SaveChangesAsync(ct);

        // Hand the task the outputs of the tasks it depends on.
        var inputs = candidate.DependsOn.Count == 0
            ? new Dictionary<string, string?>()
            : await db.TaskRuns.AsNoTracking()
                .Where(t => t.WorkflowRunId == candidate.WorkflowRunId && candidate.DependsOn.Contains(t.Name))
                .ToDictionaryAsync(t => t.Name, t => t.Output, ct);

        await tx.CommitAsync(ct);

        return new ClaimedTask(candidate.Id, candidate.WorkflowRunId, candidate.Name, candidate.Handler,
            candidate.Attempt, candidate.TimeoutSeconds, candidate.Payload, inputs);
    }

    public Task<bool> HeartbeatAsync(Guid taskId, string workerId, TimeSpan lease, CancellationToken ct)
        => MutateAsync(taskId, (t, now) => t.Heartbeat(workerId, now, lease), ct);

    public Task<bool> CompleteAsync(Guid taskId, string workerId, string? output, CancellationToken ct)
        => MutateAsync(taskId, (t, now) => t.Complete(workerId, output, now), ct);

    public Task<bool> FailAsync(Guid taskId, string workerId, string error, CancellationToken ct)
        => MutateAsync(taskId, (t, now) => t.Fail(workerId, Truncate(error), now, Random.Shared.NextDouble()), ct);

    /// <summary>Load, apply a domain transition, save; retry if the scheduler touched the row in between.</summary>
    private async Task<bool> MutateAsync(Guid id, Func<TaskRun, DateTimeOffset, bool> apply, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxConcurrencyRetries; attempt++)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();

            var task = await db.TaskRuns.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (task is null || !apply(task, clock.GetUtcNow())) return false;

            try
            {
                await db.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), ct);
            }
        }

        throw new InvalidOperationException($"Could not update task {id} after {MaxConcurrencyRetries} conflicting attempts.");
    }

    private static string Truncate(string s) => s.Length <= 2000 ? s : s[..2000];
}
