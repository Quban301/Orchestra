using Microsoft.EntityFrameworkCore;
using Orchestra.Api.Auth;
using Orchestra.Domain;
using Orchestra.Infrastructure;

namespace Orchestra.Api.Endpoints;

public static class WorkflowEndpoints
{
    public static void MapWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/workflows").WithTags("Workflows");

        g.MapPost("/", Create).RequireAuthorization(Policies.Admin);
        g.MapGet("/", List);
        g.MapGet("/{id:guid}", Get);
        g.MapPost("/{id:guid}/runs", StartRun).RequireAuthorization(Policies.Admin);
    }

    private static async Task<IResult> Create(
        CreateWorkflowRequest request, OrchestraDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length is 0 or > 200)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = ["Name is required and must be at most 200 characters."]
            });

        var tasks = (request.Tasks ?? []).Select(t => new TaskDefinition(
            t.Name?.Trim() ?? "",
            t.Handler?.Trim() ?? "",
            (t.DependsOn ?? []).Select(d => d.Trim()).ToList(),
            t.MaxAttempts ?? 3,
            t.TimeoutSeconds ?? 300,
            t.Payload)).ToList();

        var errors = DagValidator.Validate(tasks);
        if (errors.Count > 0)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["tasks"] = errors.ToArray() });

        var latest = await db.WorkflowDefinitions
            .Where(d => d.Name == name)
            .MaxAsync(d => (int?)d.Version, ct) ?? 0;

        var definition = WorkflowDefinition.Create(name, tasks, clock.GetUtcNow(), version: latest + 1);
        db.WorkflowDefinitions.Add(definition);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two clients registered the same name at once and both computed the same next version.
            return Results.Problem(statusCode: 409, title: "Conflict",
                detail: "Another version of this workflow was created at the same time. Please retry.");
        }

        return Results.Created($"/api/v1/workflows/{definition.Id}", definition.ToDto());
    }

    private static async Task<IResult> List(
        OrchestraDbContext db, CancellationToken ct, int page = 1, int pageSize = 20, string? name = null)
    {
        (page, pageSize) = Mapping.NormalizePaging(page, pageSize);

        var query = db.WorkflowDefinitions.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(name))
            query = query.Where(d => d.Name == name);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(d => d.Name).ThenByDescending(d => d.Version)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<WorkflowDto>(items.Select(i => i.ToDto()).ToList(), page, pageSize, total));
    }

    private static async Task<IResult> Get(Guid id, OrchestraDbContext db, CancellationToken ct)
    {
        var definition = await db.WorkflowDefinitions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        return definition is null ? Results.NotFound() : Results.Ok(definition.ToDto());
    }

    /// <summary>Starts a run. Send an <c>Idempotency-Key</c> header to make retries of this call safe.</summary>
    private static async Task<IResult> StartRun(
        Guid id, HttpRequest http, OrchestraDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var definition = await db.WorkflowDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (definition is null) return Results.NotFound();
        if (!definition.IsActive)
            return Results.Problem(statusCode: 409, title: "Workflow is inactive");

        var key = http.Headers["Idempotency-Key"].FirstOrDefault()?.Trim();
        if (key is { Length: > 200 })
            return Results.Problem(statusCode: 400, title: "Idempotency-Key must be at most 200 characters.");
        if (string.IsNullOrEmpty(key)) key = null;

        if (key is not null)
        {
            var existing = await FindByKeyAsync(db, id, key, ct);
            if (existing is not null) return Results.Ok(existing.ToSummary());
        }

        var run = WorkflowRun.Start(definition, clock.GetUtcNow());
        run.IdempotencyKey = key;
        db.WorkflowRuns.Add(run);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (key is not null)
        {
            // Lost a race with a concurrent request using the same key: return the winner.
            db.ChangeTracker.Clear();
            var winner = await FindByKeyAsync(db, id, key, ct);
            if (winner is not null) return Results.Ok(winner.ToSummary());
            throw;
        }

        return Results.Created($"/api/v1/runs/{run.Id}", run.ToSummary());
    }

    private static Task<WorkflowRun?> FindByKeyAsync(OrchestraDbContext db, Guid definitionId, string key, CancellationToken ct)
        => db.WorkflowRuns.AsNoTracking().Include(r => r.Tasks)
            .FirstOrDefaultAsync(r => r.WorkflowDefinitionId == definitionId && r.IdempotencyKey == key, ct);
}
