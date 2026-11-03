namespace Orchestra.Domain;

/// <summary>A single node in a workflow DAG.</summary>
public sealed record TaskDefinition(
    string Name,
    string Handler,
    IReadOnlyList<string> DependsOn,
    int MaxAttempts = 3,
    int TimeoutSeconds = 300,
    string? Payload = null);
