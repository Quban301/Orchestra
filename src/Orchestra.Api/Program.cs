using Microsoft.EntityFrameworkCore;
using Orchestra.Domain;
using Orchestra.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOrchestraInfrastructure(builder.Configuration);
builder.Services.AddOrchestraScheduler();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Dev convenience: apply any migrations that exist. Generate the first one with:
    //   dotnet ef migrations add Initial -p src/Orchestra.Infrastructure -s src/Orchestra.Api
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
    await db.Database.MigrateAsync();

    // TEMPORARY dev-only endpoints so Phase 3 can be exercised end to end.
    // Phase 4 replaces these with the real workflow/run API.
    app.MapPost("/dev/demo-run", async (OrchestraDbContext db, TimeProvider clock) =>
    {
        var now = clock.GetUtcNow();
        var def = await db.WorkflowDefinitions.FirstOrDefaultAsync(d => d.Name == "demo");
        if (def is null)
        {
            def = WorkflowDefinition.Create("demo", new[]
            {
                new TaskDefinition("extract", "echo", [], Payload: "{\"rows\":42}"),
                new TaskDefinition("transform", "flaky", ["extract"], MaxAttempts: 4, Payload: "3"),
                new TaskDefinition("load", "sleep", ["transform"], Payload: "1500"),
                new TaskDefinition("notify", "echo", ["load"], Payload: "done")
            }, now);
            db.WorkflowDefinitions.Add(def);
        }

        var run = WorkflowRun.Start(def, now);
        db.WorkflowRuns.Add(run);
        await db.SaveChangesAsync();
        return Results.Created($"/dev/runs/{run.Id}", new { run.Id });
    });

    app.MapGet("/dev/runs/{id:guid}", async (Guid id, OrchestraDbContext db) =>
    {
        var run = await db.WorkflowRuns.AsNoTracking().Include(r => r.Tasks).FirstOrDefaultAsync(r => r.Id == id);
        return run is null
            ? Results.NotFound()
            : Results.Ok(new
            {
                run.Id,
                Status = run.Status.ToString(),
                Tasks = run.Tasks.Select(t => new
                {
                    t.Name, Status = t.Status.ToString(), t.Attempt, t.Error, t.Output, t.LeaseOwner
                })
            });
    });
}

app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Ok(new { name = "Orchestra", status = "ok" }));

app.Run();
