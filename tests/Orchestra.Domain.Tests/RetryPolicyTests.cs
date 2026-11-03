using Orchestra.Domain;

namespace Orchestra.Domain.Tests;

public class RetryPolicyTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 16)]
    public void Delay_doubles_each_attempt(int attempt, int expectedSeconds)
        => Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), RetryPolicy.Delay(attempt));

    [Fact]
    public void Delay_is_capped()
        => Assert.Equal(RetryPolicy.MaxDelay, RetryPolicy.Delay(100));

    [Fact]
    public void Jitter_adds_at_most_twenty_percent()
        => Assert.Equal(TimeSpan.FromSeconds(2.4), RetryPolicy.Delay(1, jitter: 1));

    [Fact]
    public void Attempt_zero_is_invalid()
        => Assert.Throws<ArgumentOutOfRangeException>(() => RetryPolicy.Delay(0));
}
