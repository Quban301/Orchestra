namespace Orchestra.Worker.Handlers;

/// <summary>Returns its payload. Useful for wiring tests.</summary>
public sealed class EchoHandler : ITaskHandler
{
    public string Name => "echo";
    public Task<TaskResult> ExecuteAsync(TaskContext context, CancellationToken ct)
        => Task.FromResult(new TaskResult(context.Payload));
}

/// <summary>Sleeps for Payload milliseconds. Useful for exercising heartbeats, timeouts and shutdown.</summary>
public sealed class SleepHandler : ITaskHandler
{
    public string Name => "sleep";
    public async Task<TaskResult> ExecuteAsync(TaskContext context, CancellationToken ct)
    {
        var ms = int.TryParse(context.Payload, out var parsed) ? parsed : 1000;
        await Task.Delay(ms, ct);
        return new TaskResult($"slept {ms}ms");
    }
}

/// <summary>Fails until attempt number Payload, then succeeds. Demonstrates retries with backoff.</summary>
public sealed class FlakyHandler : ITaskHandler
{
    public string Name => "flaky";
    public Task<TaskResult> ExecuteAsync(TaskContext context, CancellationToken ct)
    {
        var succeedOn = int.TryParse(context.Payload, out var n) ? n : 2;
        if (context.Attempt < succeedOn)
            throw new InvalidOperationException($"Simulated failure on attempt {context.Attempt}.");
        return Task.FromResult(new TaskResult($"recovered on attempt {context.Attempt}"));
    }
}
