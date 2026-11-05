using IniaAi.AiConnection.HttpHandling;
using IniaAi.AiConnection.HttpHandling.Enums;

namespace IniaAi.AiConnection.Tests.HttpHandling;

public class RetryDecisionTests
{
    [Fact]
    public void Constructor_CreatesInstanceWithDefaultValues()
    {
        // Act
        var decision = new RetryDecision();

        // Assert
        Assert.False(decision.ShouldRetry);
        Assert.Equal(RetryType.None, decision.RetryType);
        Assert.Equal(0, decision.DelaySeconds);
        Assert.Equal(0, decision.UpdatedRateLimitAttempts);
        Assert.Equal(0, decision.UpdatedUnavailableAttempts);
    }

    [Fact]
    public void Properties_CanBeSetAndRetrieved()
    {
        // Arrange
        var decision = new RetryDecision
        {
            // Act
            ShouldRetry = true,
            RetryType = RetryType.RateLimit,
            DelaySeconds = 30.5,
            UpdatedRateLimitAttempts = 2,
            UpdatedUnavailableAttempts = 1
        };

        // Assert
        Assert.True(decision.ShouldRetry);
        Assert.Equal(RetryType.RateLimit, decision.RetryType);
        Assert.Equal(30.5, decision.DelaySeconds);
        Assert.Equal(2, decision.UpdatedRateLimitAttempts);
        Assert.Equal(1, decision.UpdatedUnavailableAttempts);
    }

    [Theory]
    // passing ints because enum is internal and cannot be used as parameter type
    // for public test methods
    [InlineData(0)] // RetryType.None
    [InlineData(1)] // RetryType.RateLimit
    [InlineData(2)] // RetryType.ServiceUnavailable
    public void RetryType_SupportsAllEnumValues(int retryTypeInt)
    {
        // Arrange
        var retryType = (RetryType)retryTypeInt;
        var decision = new RetryDecision
        {
            // Act
            RetryType = retryType
        };

        // Assert
        Assert.Equal(retryType, decision.RetryType);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1.5)]
    [InlineData(60)]
    [InlineData(120.75)]
    public void DelaySeconds_AcceptsVariousDoubleValues(double delaySeconds)
    {
        // Arrange
        var decision = new RetryDecision
        {
            // Act
            DelaySeconds = delaySeconds
        };

        // Assert
        Assert.Equal(delaySeconds, decision.DelaySeconds);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(5, 3)]
    [InlineData(10, 0)]
    public void AttemptCounts_AcceptVariousIntegerValues(int rateLimitAttempts, int unavailableAttempts)
    {
        // Arrange
        var decision = new RetryDecision
        {
            // Act
            UpdatedRateLimitAttempts = rateLimitAttempts,
            UpdatedUnavailableAttempts = unavailableAttempts
        };

        // Assert
        Assert.Equal(rateLimitAttempts, decision.UpdatedRateLimitAttempts);
        Assert.Equal(unavailableAttempts, decision.UpdatedUnavailableAttempts);
    }
}
