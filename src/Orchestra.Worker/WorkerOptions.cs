namespace Orchestra.Worker;

public sealed class WorkerOptions
{
    public string WorkerId { get; set; } =
        $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid().ToString("N")[..6]}";

    /// <summary>How many tasks this process runs in parallel.</summary>
    public int Concurrency { get; set; } = 4;

    /// <summary>A claimed task is reclaimed if no heartbeat arrives within this window. Heartbeats go out at a third of it.</summary>
    public int LeaseSeconds { get; set; } = 30;

    /// <summary>How long an idle slot waits before polling again.</summary>
    public int PollIntervalMs { get; set; } = 500;

    /// <summary>On shutdown, in-flight tasks get this long to finish before being cancelled.</summary>
    public int ShutdownGraceSeconds { get; set; } = 20;
}
