namespace Orchestra.Domain;

public static class DagValidator
{
    /// <summary>Returns a list of human-readable problems; empty means the DAG is valid.</summary>
    public static IReadOnlyList<string> Validate(IReadOnlyList<TaskDefinition> tasks)
    {
        var errors = new List<string>();

        if (tasks.Count == 0)
        {
            errors.Add("A workflow must contain at least one task.");
            return errors;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in tasks)
        {
            if (string.IsNullOrWhiteSpace(t.Name))
                errors.Add("Task names must not be blank.");
            else if (!names.Add(t.Name))
                errors.Add($"Duplicate task name '{t.Name}'.");
        }

        foreach (var t in tasks)
        {
            if (string.IsNullOrWhiteSpace(t.Handler))
                errors.Add($"Task '{t.Name}' has no handler.");
            if (t.MaxAttempts < 1)
                errors.Add($"Task '{t.Name}' must allow at least 1 attempt.");
            if (t.TimeoutSeconds < 1)
                errors.Add($"Task '{t.Name}' must have a positive timeout.");

            foreach (var dep in t.DependsOn)
            {
                if (dep == t.Name)
                    errors.Add($"Task '{t.Name}' cannot depend on itself.");
                else if (!names.Contains(dep))
                    errors.Add($"Task '{t.Name}' depends on unknown task '{dep}'.");
            }
        }

        if (errors.Count > 0) return errors;

        var order = Sort(tasks, out var remaining);
        if (order.Count != tasks.Count)
            errors.Add($"Dependency cycle detected involving: {string.Join(", ", remaining)}.");

        return errors;
    }

    /// <summary>Kahn's algorithm. Throws if the graph is invalid or cyclic.</summary>
    public static IReadOnlyList<string> TopologicalOrder(IReadOnlyList<TaskDefinition> tasks)
    {
        var errors = Validate(tasks);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));
        return Sort(tasks, out _);
    }

    private static List<string> Sort(IReadOnlyList<TaskDefinition> tasks, out List<string> remaining)
    {
        var indegree = new Dictionary<string, int>(StringComparer.Ordinal);
        var dependents = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var t in tasks)
        {
            var distinct = t.DependsOn.Distinct(StringComparer.Ordinal).ToList();
            indegree[t.Name] = distinct.Count;
            foreach (var dep in distinct)
            {
                if (!dependents.TryGetValue(dep, out var list))
                    dependents[dep] = list = new List<string>();
                list.Add(t.Name);
            }
        }

        var queue = new Queue<string>(tasks.Where(t => indegree[t.Name] == 0).Select(t => t.Name));
        var order = new List<string>(tasks.Count);

        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            order.Add(n);
            if (!dependents.TryGetValue(n, out var next)) continue;
            foreach (var d in next)
                if (--indegree[d] == 0) queue.Enqueue(d);
        }

        remaining = tasks.Select(t => t.Name).Where(n => indegree[n] > 0).ToList();
        return order;
    }
}
