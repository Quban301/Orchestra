using Orchestra.Domain;

namespace Orchestra.Api;

public sealed record TaskDefinitionDto(
    string Name,
    string Handler,
    List<string>? DependsOn,
    int? MaxAttempts,
    int? TimeoutSeconds,
    string? Payload);

public sealed record CreateWorkflowRequest(string Name, List<TaskDefinitionDto> Tasks);

public sealed record WorkflowDto(
    Guid Id,
    string Name,
    int Version,
    bool IsActive,
    DateTimeOffset CreatedAt,
    IReadOnlyList<TaskDefinitionDto> Tasks);

public sealed record TaskRunDto(
    Guid Id,
    string Name,
    string Handler,
    string Status,
    IReadOnlyList<string> DependsOn,
    int Attempt,
    int MaxAttempts,
    string? Error,
    string? Output,
    string? LeaseOwner,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record RunSummaryDto(
    Guid Id,
    Guid WorkflowDefinitionId,
    string WorkflowName,
    string Status,
    int TaskCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record RunDetailDto(
    Guid Id,
    Guid WorkflowDefinitionId,
    string WorkflowName,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<TaskRunDto> Tasks);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public sealed record StatsDto(
    int Workflows,
    IReadOnlyDictionary<string, int> Runs,
    IReadOnlyDictionary<string, int> Tasks);

public static class Mapping
{
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) NormalizePaging(int page, int pageSize)
        => (Math.Max(page, 1), Math.Clamp(pageSize, 1, MaxPageSize));

    public static WorkflowDto ToDto(this WorkflowDefinition d) => new(
        d.Id, d.Name, d.Version, d.IsActive, d.CreatedAt,
        d.Tasks.Select(t => new TaskDefinitionDto(
            t.Name, t.Handler, t.DependsOn.ToList(), t.MaxAttempts, t.TimeoutSeconds, t.Payload)).ToList());

    public static TaskRunDto ToDto(this TaskRun t) => new(
        t.Id, t.Name, t.Handler, t.Status.ToString(), t.DependsOn, t.Attempt, t.MaxAttempts,
        t.Error, t.Output, t.LeaseOwner, t.NextAttemptAt, t.StartedAt, t.CompletedAt);

    public static RunSummaryDto ToSummary(this WorkflowRun r) => new(
        r.Id, r.WorkflowDefinitionId, r.WorkflowName, r.Status.ToString(), r.Tasks.Count,
        r.CreatedAt, r.StartedAt, r.CompletedAt);

    public static RunDetailDto ToDetail(this WorkflowRun r) => new(
        r.Id, r.WorkflowDefinitionId, r.WorkflowName, r.Status.ToString(),
        r.CreatedAt, r.StartedAt, r.CompletedAt,
        r.Tasks.OrderBy(t => t.Name).Select(t => t.ToDto()).ToList());
}
