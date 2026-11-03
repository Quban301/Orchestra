using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Orchestra.Worker;

public sealed class WorkerService(
    TaskExecutor executor,
    IOptions<WorkerOptions> options,
    ILogger<WorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        var opt = options.Value;
        logger.LogInformation("Worker {WorkerId} starting with {Concurrency} slots.", opt.WorkerId, opt.Concurrency);

        // Graceful shutdown: once stopping fires we stop claiming, then give in-flight work the grace
        // period before forcibly cancelling handlers.
        using var abortCts = new CancellationTokenSource();
        await using var reg = stopping.Register(() => abortCts.CancelAfter(TimeSpan.FromSeconds(opt.ShutdownGraceSeconds)));

        var slots = Enumerable.Range(0, Math.Max(1, opt.Concurrency))
            .Select(_ => Task.Run(() => SlotLoopAsync(stopping, abortCts.Token)));
        await Task.WhenAll(slots);

        logger.LogInformation("Worker {WorkerId} stopped.", opt.WorkerId);
    }

    private async Task SlotLoopAsync(CancellationToken stopping, CancellationToken abort)
    {
        var poll = TimeSpan.FromMilliseconds(options.Value.PollIntervalMs);
        while (!stopping.IsCancellationRequested)
        {
            var worked = false;
            try
            {
                worked = await executor.RunOneAsync(stopping, abort);
            }
            catch (Exception ex)
            {
                if (!abort.IsCancellationRequested) logger.LogError(ex, "Worker slot error.");
            }

            if (worked) continue;

            try { await Task.Delay(poll + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 100)), stopping); }
            catch (OperationCanceledException) { }
        }
    }
}
