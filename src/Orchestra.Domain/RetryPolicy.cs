namespace Orchestra.Domain;

public static class RetryPolicy
{
    public static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Exponential backoff after the given (1-based) failed attempt: 2s, 4s, 8s ... capped at 5 minutes.
    /// <paramref name="jitter"/> in [0,1) adds up to +20% so retries from many tasks don't synchronise.
    /// </summary>
    public static TimeSpan Delay(int failedAttempt, double jitter = 0)
    {
        if (failedAttempt < 1) throw new ArgumentOutOfRangeException(nameof(failedAttempt));
        var exponent = Math.Min(failedAttempt - 1, 30);
        var seconds = Math.Min(BaseDelay.TotalSeconds * Math.Pow(2, exponent), MaxDelay.TotalSeconds);
        seconds *= 1 + 0.2 * Math.Clamp(jitter, 0, 1);
        return TimeSpan.FromSeconds(Math.Min(seconds, MaxDelay.TotalSeconds));
    }
}
