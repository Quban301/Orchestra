namespace Orchestra.Domain;

/// <summary>
/// Pure function-style scheduler step: promotes tasks whose dependencies are satisfied,
/// skips tasks whose upstream failed, and rolls the run status up from its tasks.
/// </summary>
public static class RunPlanner
{
    public static void Advance(WorkflowRun run, DateTimeOffset now)
    {
        if (run.IsTerminal) return;

        var byName = run.Tasks.ToDictionary(t => t.Name, StringComparer.Ordinal);
        bool changed;
        do
        {
            changed = false;
            foreach (var task in run.Tasks.Where(t => t.Status == TaskRunStatus.Pending))
            {
                var deps = task.DependsOn.Select(d => byName[d]).ToList();

                if (deps.Any(d => d.Status is TaskRunStatus.Failed or TaskRunStatus.Skipped
                                              or TaskRunStatus.Cancelled))
                    changed |= task.Skip(now);
                else if (deps.All(d => d.Status == TaskRunStatus.Succeeded))
                    changed |= task.MarkQueued(now);
            }
        } while (changed); // Skips can cascade through several levels.

        if (run.Status == RunStatus.Pending)
        {
            run.Status = RunStatus.Running;
            run.StartedAt = now;
        }

        if (run.Tasks.All(t => t.IsTerminal))
        {
            run.Status = run.Tasks.Any(t => t.Status == TaskRunStatus.Failed) ? RunStatus.Failed
                : run.Tasks.Any(t => t.Status == TaskRunStatus.Cancelled) ? RunStatus.Cancelled
                : RunStatus.Succeeded;
            run.CompletedAt = now;
        }
    }
}
