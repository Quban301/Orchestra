namespace Orchestra.Domain;

public class WorkflowRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowDefinitionId { get; set; }
    public string WorkflowName { get; set; } = "";
    public RunStatus Status { get; set; } = RunStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public List<TaskRun> Tasks { get; set; } = new();

    public bool IsTerminal => Status is RunStatus.Succeeded or RunStatus.Failed or RunStatus.Cancelled;

    public static WorkflowRun Start(WorkflowDefinition definition, DateTimeOffset now)
    {
        var run = new WorkflowRun
        {
            WorkflowDefinitionId = definition.Id,
            WorkflowName = definition.Name,
            CreatedAt = now
        };

        run.Tasks = definition.Tasks.Select(t => new TaskRun
        {
            WorkflowRunId = run.Id,
            Name = t.Name,
            Handler = t.Handler,
            DependsOn = t.DependsOn.Distinct().ToList(),
            MaxAttempts = t.MaxAttempts,
            TimeoutSeconds = t.TimeoutSeconds,
            Payload = t.Payload
        }).ToList();

        return run;
    }

    public void Cancel(DateTimeOffset now)
    {
        if (IsTerminal) return;
        foreach (var t in Tasks) t.Cancel(now);
        Status = RunStatus.Cancelled;
        CompletedAt = now;
    }
}
