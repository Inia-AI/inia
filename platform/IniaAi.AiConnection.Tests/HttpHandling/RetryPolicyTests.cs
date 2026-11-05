using IniaAi.AiConnection.HttpHandling;
using IniaAi.Configuration.LLMProviders;

namespace IniaAi.AiConnection.Tests.HttpHandling;

public class RetryPolicyTests
{
    [Fact]
    public void DefaultConstructor_SetsDefaultValues()
    {
        // Act
        var policy = new RetryPolicy();

        // Assert
        Assert.Equal(5, policy.MaxRateLimitRetries);
        Assert.Equal(3, policy.MaxUnavailableRetries);
        Assert.Equal(1, policy.DefaultUnavailableRetryDelaySeconds);
        Assert.Equal(60, policy.MaxRetryDelaySeconds);
        Assert.Equal("Retry-After", policy.RetryAfterHeader);
        Assert.False(policy.EnableTcpKeepAlive);
        Assert.Equal(30000, policy.TcpKeepAliveTime);
        Assert.Equal(1000, policy.TcpKeepAliveInterval);
    }

    [Fact]
    public void AnthropicSettingsConstructor_MapsSettingsCorrectly()
    {
        // Arrange
        var anthropicSettings = new AnthropicSettings
        {
            MaxRateLimitRetryAttempts = 7,
            MaxServiceUnavailableRetryAttempts = 4
        };

        // Act
        var policy = new RetryPolicy(anthropicSettings);

        // Assert
        Assert.Equal(7, policy.MaxRateLimitRetries);
        Assert.Equal(4, policy.MaxUnavailableRetries);
        // Other properties should remain at default values
        Assert.Equal(1, policy.DefaultUnavailableRetryDelaySeconds);
        Assert.Equal(60, policy.MaxRetryDelaySeconds);
        Assert.Equal("Retry-After", policy.RetryAfterHeader);
        Assert.False(policy.EnableTcpKeepAlive);
        Assert.Equal(30000, policy.TcpKeepAliveTime);
        Assert.Equal(1000, policy.TcpKeepAliveInterval);
    }

    [Fact]
    public void AnthropicSettingsConstructor_WithZeroValues_MapsCorrectly()
    {
        // Arrange
        var anthropicSettings = new AnthropicSettings
        {
            MaxRateLimitRetryAttempts = 0,
            MaxServiceUnavailableRetryAttempts = 0
        };

        // Act
        var policy = new RetryPolicy(anthropicSettings);

        // Assert
        Assert.Equal(0, policy.MaxRateLimitRetries);
        Assert.Equal(0, policy.MaxUnavailableRetries);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(100)]
    public void MaxRateLimitRetries_CanBeSetToVariousValues(int maxRetries)
    {
        // Arrange
        var policy = new RetryPolicy
        {
            // Act
            MaxRateLimitRetries = maxRetries
        };

        // Assert
        Assert.Equal(maxRetries, policy.MaxRateLimitRetries);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(50)]
    public void MaxUnavailableRetries_CanBeSetToVariousValues(int maxRetries)
    {
        // Arrange
        var policy = new RetryPolicy
        {
            // Act
            MaxUnavailableRetries = maxRetries
        };

        // Assert
        Assert.Equal(maxRetries, policy.MaxUnavailableRetries);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    public void DefaultUnavailableRetryDelaySeconds_CanBeSetToVariousValues(int delaySeconds)
    {
        // Arrange
        var policy = new RetryPolicy
        {
            // Act
            DefaultUnavailableRetryDelaySeconds = delaySeconds
        };

        // Assert
        Assert.Equal(delaySeconds, policy.DefaultUnavailableRetryDelaySeconds);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(300)]
    public void MaxRetryDelaySeconds_CanBeSetToVariousValues(int maxDelaySeconds)
    {
        // Arrange
        var policy = new RetryPolicy
        {
            // Act
            MaxRetryDelaySeconds = maxDelaySeconds
        };

        // Assert
        Assert.Equal(maxDelaySeconds, policy.MaxRetryDelaySeconds);
    }

    [Theory]
    [InlineData("Retry-After")]
    [InlineData("retry-after")]
    [InlineData("X-RateLimit-Reset")]
    [InlineData("Custom-Header")]
    public void RetryAfterHeader_CanBeSetToVariousValues(string headerName)
    {
        // Arrange
        var policy = new RetryPolicy
        {
            // Act
            RetryAfterHeader = headerName
        };

        // Assert
        Assert.Equal(headerName, policy.RetryAfterHeader);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EnableTcpKeepAlive_CanBeSetToBooleanValues(bool enableKeepAlive)
    {
        // Arrange
        var policy = new RetryPolicy
        {
            // Act
            EnableTcpKeepAlive = enableKeepAlive
        };

        // Assert
        Assert.Equal(enableKeepAlive, policy.EnableTcpKeepAlive);
    }

    [Theory]
    [InlineData(10000)]
    [InlineData(30000)]
    [InlineData(60000)]
    public void TcpKeepAliveTime_CanBeSetToVariousValues(int keepAliveTime)
    {
        // Arrange
        var policy = new RetryPolicy
        {
            // Act
            TcpKeepAliveTime = keepAliveTime
        };

        // Assert
        Assert.Equal(keepAliveTime, policy.TcpKeepAliveTime);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(1000)]
    [InlineData(2000)]
    public void TcpKeepAliveInterval_CanBeSetToVariousValues(int keepAliveInterval)
    {
        // Arrange
        var policy = new RetryPolicy
        {
            // Act
            TcpKeepAliveInterval = keepAliveInterval
        };

        // Assert
        Assert.Equal(keepAliveInterval, policy.TcpKeepAliveInterval);
    }

    [Fact]
    public void AnthropicSettingsConstructor_PreservesOtherPropertiesAtDefaults()
    {
        // Arrange
        var anthropicSettings = new AnthropicSettings
        {
            MaxRateLimitRetryAttempts = 10,
            MaxServiceUnavailableRetryAttempts = 8
        };

        // Act
        var policy = new RetryPolicy(anthropicSettings)
        {
            // Modify some properties after construction
            MaxRetryDelaySeconds = 120,
            EnableTcpKeepAlive = true
        };

        // Assert - Anthropic settings are applied
        Assert.Equal(10, policy.MaxRateLimitRetries);
        Assert.Equal(8, policy.MaxUnavailableRetries);

        // Assert - Modified properties are updated
        Assert.Equal(120, policy.MaxRetryDelaySeconds);
        Assert.True(policy.EnableTcpKeepAlive);

        // Assert - Other properties remain at defaults
        Assert.Equal(1, policy.DefaultUnavailableRetryDelaySeconds);
        Assert.Equal("Retry-After", policy.RetryAfterHeader);
        Assert.Equal(30000, policy.TcpKeepAliveTime);
        Assert.Equal(1000, policy.TcpKeepAliveInterval);
    }
}
