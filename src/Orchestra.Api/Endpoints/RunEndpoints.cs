using Microsoft.EntityFrameworkCore;
using Orchestra.Api.Auth;
using Orchestra.Domain;
using Orchestra.Infrastructure;

namespace Orchestra.Api.Endpoints;

public static class RunEndpoints
{
    public static void MapRunEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/runs").WithTags("Runs");

        g.MapGet("/", List);
        g.MapGet("/{id:guid}", Get);
        g.MapPost("/{id:guid}/cancel", (Guid id, OrchestraDbContext db, TimeProvider clock, CancellationToken ct) =>
                MutateAsync(id, db, clock, (run, now) =>
                {
                    if (run.IsTerminal) return $"Run is already {run.Status}.";
                    run.Cancel(now);
                    return null;
                }, ct))
            .RequireAuthorization(Policies.Admin);

        g.MapPost("/{id:guid}/retry", (Guid id, OrchestraDbContext db, TimeProvider clock, CancellationToken ct) =>
                MutateAsync(id, db, clock, (run, _) =>
                    run.RetryFailed() ? null : $"Only failed runs can be retried; this run is {run.Status}.", ct))
            .RequireAuthorization(Policies.Admin);

        app.MapGet("/api/v1/stats", Stats).WithTags("Stats");
    }

    private static async Task<IResult> List(
        OrchestraDbContext db, CancellationToken ct,
        int page = 1, int pageSize = 20, string? status = null, Guid? workflowId = null)
    {
        (page, pageSize) = Mapping.NormalizePaging(page, pageSize);

        var query = db.WorkflowRuns.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<RunStatus>(status, ignoreCase: true, out var parsed))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = [$"Unknown status '{status}'. Use one of: {string.Join(", ", Enum.GetNames<RunStatus>())}."]
                });
            query = query.Where(r => r.Status == parsed);
        }

        if (workflowId is not null)
            query = query.Where(r => r.WorkflowDefinitionId == workflowId);

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new RunSummaryDto(
                r.Id, r.WorkflowDefinitionId, r.WorkflowName, r.Status.ToString(), r.Tasks.Count,
                r.CreatedAt, r.StartedAt, r.CompletedAt))
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<RunSummaryDto>(rows, page, pageSize, total));
    }

    private static async Task<IResult> Get(Guid id, OrchestraDbContext db, CancellationToken ct)
    {
        var run = await db.WorkflowRuns.AsNoTracking().Include(r => r.Tasks).FirstOrDefaultAsync(r => r.Id == id, ct);
        return run is null ? Results.NotFound() : Results.Ok(run.ToDetail());
    }

    /// <summary>
    /// Loads the run, applies a domain operation and saves. Workers and the scheduler update the same rows,
    /// so a concurrency conflict is retried against fresh data. <paramref name="apply"/> returns an error
    /// message to refuse the operation (409), or null on success.
    /// </summary>
    private static async Task<IResult> MutateAsync(
        Guid id, OrchestraDbContext db, TimeProvider clock,
        Func<WorkflowRun, DateTimeOffset, string?> apply, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var run = await db.WorkflowRuns.Include(r => r.Tasks).FirstOrDefaultAsync(r => r.Id == id, ct);
            if (run is null) return Results.NotFound();

            var refusal = apply(run, clock.GetUtcNow());
            if (refusal is not null)
                return Results.Problem(statusCode: 409, title: "Conflict", detail: refusal);

            try
            {
                await db.SaveChangesAsync(ct);
                return Results.Ok(run.ToDetail());
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
            }
        }

        return Results.Problem(statusCode: 409, title: "Conflict",
            detail: "The run was being updated by the engine. Please try again.");
    }

    private static async Task<IResult> Stats(OrchestraDbContext db, CancellationToken ct)
    {
        var workflows = await db.WorkflowDefinitions.CountAsync(ct);

        var runCounts = (await db.WorkflowRuns.GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Status, x => x.Count);

        var taskCounts = (await db.TaskRuns.GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Status, x => x.Count);

        return Results.Ok(new StatsDto(
            workflows,
            Enum.GetValues<RunStatus>().ToDictionary(s => s.ToString(), s => runCounts.GetValueOrDefault(s)),
            Enum.GetValues<TaskRunStatus>().ToDictionary(s => s.ToString(), s => taskCounts.GetValueOrDefault(s))));
    }
}
