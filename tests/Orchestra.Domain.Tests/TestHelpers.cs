using Orchestra.Domain;

namespace Orchestra.Domain.Tests;

internal static class T
{
    public static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public static TaskDefinition Task(string name, params string[] deps) =>
        new(name, "noop", deps);

    public static WorkflowRun Run(params TaskDefinition[] tasks)
    {
        var def = WorkflowDefinition.Create("wf", tasks, Now);
        return WorkflowRun.Start(def, Now);
    }

    public static TaskRun Get(this WorkflowRun run, string name) => run.Tasks.Single(t => t.Name == name);
}
