namespace Orchestra.Domain;

public class WorkflowDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public int Version { get; set; } = 1;
    public List<TaskDefinition> Tasks { get; set; } = new();
    public string? CronExpression { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    public static WorkflowDefinition Create(string name, IEnumerable<TaskDefinition> tasks,
        DateTimeOffset now, string? cron = null, int version = 1)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Workflow name is required.", nameof(name));

        var list = tasks.ToList();
        var errors = DagValidator.Validate(list);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));

        return new WorkflowDefinition
        {
            Name = name, Tasks = list, CronExpression = cron, Version = version, CreatedAt = now
        };
    }
}
