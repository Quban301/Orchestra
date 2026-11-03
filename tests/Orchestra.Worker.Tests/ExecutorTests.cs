using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orchestra.Infrastructure.Tasks;
using Orchestra.Worker;
using Orchestra.Worker.Handlers;

namespace Orchestra.Worker.Tests;

internal sealed class FakeStore : ITaskStore
{
    private readonly Queue<ClaimedTask> _queue = new();
    public bool HeartbeatResult { get; set; } = true;
    public int Heartbeats;
    public List<(Guid Id, string? Output)> Completed { get; } = new();
    public List<(Guid Id, string Error)> Failed { get; } = new();

    public void Enqueue(ClaimedTask t) => _queue.Enqueue(t);

    public Task<ClaimedTask?> ClaimNextAsync(string workerId, TimeSpan lease, CancellationToken ct)
        => Task.FromResult(_queue.Count > 0 ? _queue.Dequeue() : null);

    public Task<bool> HeartbeatAsync(Guid taskId, string workerId, TimeSpan lease, CancellationToken ct)
    {
        Interlocked.Increment(ref Heartbeats);
        return Task.FromResult(HeartbeatResult);
    }

    public Task<bool> CompleteAsync(Guid taskId, string workerId, string? output, CancellationToken ct)
    {
        Completed.Add((taskId, output));
        return Task.FromResult(true);
    }

    public Task<bool> FailAsync(Guid taskId, string workerId, string error, CancellationToken ct)
    {
        Failed.Add((taskId, error));
        return Task.FromResult(true);
    }
}

internal sealed class LambdaHandler(string name, Func<TaskContext, CancellationToken, Task<TaskResult>> run) : ITaskHandler
{
    public string Name => name;
    public Task<TaskResult> ExecuteAsync(TaskContext context, CancellationToken ct) => run(context, ct);
}

public class ExecutorTests
{
    private static ClaimedTask Claimed(string handler = "h", int timeout = 30, int attempt = 1, string? payload = null)
        => new(Guid.NewGuid(), Guid.NewGuid(), "task", handler, attempt, timeout, payload,
            new Dictionary<string, string?>());

    private static TaskExecutor Executor(FakeStore store, params ITaskHandler[] handlers)
        => new(store, new HandlerRegistry(handlers),
            Options.Create(new WorkerOptions { WorkerId = "w1", LeaseSeconds = 1 }),
            NullLogger<TaskExecutor>.Instance);

    [Fact]
    public async Task Returns_false_when_nothing_to_claim()
    {
        var exec = Executor(new FakeStore());
        Assert.False(await exec.RunOneAsync(CancellationToken.None, CancellationToken.None));
    }

    [Fact]
    public async Task Does_not_claim_once_stopping()
    {
        var store = new FakeStore();
        store.Enqueue(Claimed());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.False(await Executor(store).RunOneAsync(cts.Token, CancellationToken.None));
    }

    [Fact]
    public async Task Successful_handler_completes_task_with_output()
    {
        var store = new FakeStore();
        var task = Claimed(payload: "hello");
        store.Enqueue(task);
        var exec = Executor(store, new EchoHandler().As("h"));

        Assert.True(await exec.RunOneAsync(CancellationToken.None, CancellationToken.None));

        var done = Assert.Single(store.Completed);
        Assert.Equal(task.Id, done.Id);
        Assert.Equal("hello", done.Output);
        Assert.Empty(store.Failed);
    }

    [Fact]
    public async Task Throwing_handler_reports_failure()
    {
        var store = new FakeStore();
        store.Enqueue(Claimed());
        var exec = Executor(store, new LambdaHandler("h", (_, _) => throw new InvalidOperationException("kaboom")));

        await exec.RunOneAsync(CancellationToken.None, CancellationToken.None);

        var failure = Assert.Single(store.Failed);
        Assert.Contains("kaboom", failure.Error);
        Assert.Empty(store.Completed);
    }

    [Fact]
    public async Task Unknown_handler_reports_failure()
    {
        var store = new FakeStore();
        store.Enqueue(Claimed(handler: "missing"));

        await Executor(store).RunOneAsync(CancellationToken.None, CancellationToken.None);

        Assert.Contains("No handler", Assert.Single(store.Failed).Error);
    }

    [Fact]
    public async Task Handler_exceeding_timeout_is_cancelled_and_failed()
    {
        var store = new FakeStore();
        store.Enqueue(Claimed(timeout: 1));
        var exec = Executor(store, new LambdaHandler("h", async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new TaskResult();
        }));

        await exec.RunOneAsync(CancellationToken.None, CancellationToken.None);

        Assert.Contains("Timed out", Assert.Single(store.Failed).Error);
    }

    [Fact]
    public async Task Lost_lease_cancels_handler_and_reports_nothing()
    {
        var store = new FakeStore { HeartbeatResult = false };
        store.Enqueue(Claimed());
        var sawCancellation = false;
        var exec = Executor(store, new LambdaHandler("h", async (_, ct) =>
        {
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { sawCancellation = true; throw; }
            return new TaskResult();
        }));

        await exec.RunOneAsync(CancellationToken.None, CancellationToken.None);

        Assert.True(sawCancellation);
        Assert.Empty(store.Completed);
        Assert.Empty(store.Failed);
    }

    [Fact]
    public async Task Long_running_handler_keeps_heartbeating()
    {
        var store = new FakeStore();
        store.Enqueue(Claimed());
        var exec = Executor(store, new LambdaHandler("h", async (_, ct) =>
        {
            await Task.Delay(1200, ct);   // lease is 1s, so heartbeats go out every ~333ms
            return new TaskResult("ok");
        }));

        await exec.RunOneAsync(CancellationToken.None, CancellationToken.None);

        Assert.True(store.Heartbeats >= 2, $"expected at least 2 heartbeats, saw {store.Heartbeats}");
        Assert.Single(store.Completed);
    }

    [Fact]
    public async Task Forced_shutdown_fails_the_task_so_it_can_be_retried()
    {
        var store = new FakeStore();
        store.Enqueue(Claimed());
        using var abort = new CancellationTokenSource();
        var exec = Executor(store, new LambdaHandler("h", async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new TaskResult();
        }));

        var running = exec.RunOneAsync(CancellationToken.None, abort.Token);
        await Task.Delay(100);
        await abort.CancelAsync();
        await running;

        Assert.Contains("shut down", Assert.Single(store.Failed).Error);
    }

    [Fact]
    public async Task Flaky_handler_fails_until_target_attempt()
    {
        var flaky = new FlakyHandler();
        var ctx = (int attempt) => new TaskContext(Guid.NewGuid(), Guid.NewGuid(), "t", attempt, "3",
            new Dictionary<string, string?>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => flaky.ExecuteAsync(ctx(2), default));
        var ok = await flaky.ExecuteAsync(ctx(3), default);
        Assert.Contains("recovered", ok.Output);
    }
}

internal static class HandlerExtensions
{
    /// <summary>Re-registers a built-in handler under a different name for tests.</summary>
    public static ITaskHandler As(this ITaskHandler inner, string name)
        => new LambdaHandler(name, inner.ExecuteAsync);
}
