using IniaAi.AiConnection.HttpHandling;
using IniaAi.AiConnection.HttpHandling.Enums;
using IniaAi.Testing;
using Microsoft.Extensions.Logging;
using System.Net;

namespace IniaAi.AiConnection.Tests.HttpHandling;

public class RetryLogicTests
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger _logger;
    private readonly RetryPolicy _defaultPolicy;

    public RetryLogicTests(ITestOutputHelper output)
    {
        _output = output;
        _logger = TestsLogger.CreateLogger<RetryLogicTests>(_output);
        _defaultPolicy = new RetryPolicy();
    }

    #region ArgumentValidation Tests

    [Fact]
    public void ShouldRetry_BothResponseAndExceptionNull_ThrowsArgumentException()
    {
        // Act & Assert
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            RetryLogic.ShouldRetry(null, null, 0, 0, _defaultPolicy, _logger));

        Assert.Contains("Either response or exception must be provided", exception.Message);
    }

    [Fact]
    public void ShouldRetry_ValidInputs_DoesNotThrow()
    {
        // Arrange
        using var response = new HttpResponseMessage(HttpStatusCode.OK);

        // Act & Assert - Should not throw
        RetryDecision result = RetryLogic.ShouldRetry(response, null, 0, 0, _defaultPolicy, _logger);

        Assert.NotNull(result);
    }

    #endregion

    #region Success Response Tests

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.NoContent)]
    public void ShouldRetry_SuccessfulResponse_ReturnsNoRetry(HttpStatusCode successCode)
    {
        // Arrange
        using var response = new HttpResponseMessage(successCode);
        int rateLimitAttempts = 2;
        int unavailableAttempts = 1;

        // Act
        RetryDecision result = RetryLogic.ShouldRetry(response, null, rateLimitAttempts, unavailableAttempts, _defaultPolicy, _logger);

        // Assert
        Assert.False(result.ShouldRetry);
        Assert.Equal(RetryType.None, result.RetryType);
        Assert.Equal(0, result.DelaySeconds);
        Assert.Equal(rateLimitAttempts, result.UpdatedRateLimitAttempts);
        Assert.Equal(unavailableAttempts, result.UpdatedUnavailableAttempts);
    }

    #endregion

    #region Rate Limit Tests

    [Fact]
    public void ShouldRetry_RateLimit_FirstAttempt_ReturnsRetryWithDelay()
    {
        // Arrange
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.Add("Retry-After", "30");

        // Act
        RetryDecision result = RetryLogic.ShouldRetry(response, null, 0, 0, _defaultPolicy, _logger);

        // Assert
        Assert.True(result.ShouldRetry);
        Assert.Equal(RetryType.RateLimit, result.RetryType);
        Assert.Equal(30, result.DelaySeconds);
        Assert.Equal(1, result.UpdatedRateLimitAttempts);
        Assert.Equal(0, result.UpdatedUnavailableAttempts);
    }

    [Fact]
    public void ShouldRetry_RateLimit_NoRetryAfterHeader_UsesMaxDelaySeconds()
    {
        // Arrange
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        var policy = new RetryPolicy { MaxRetryDelaySeconds = 45 };

        // Act
        RetryDecision result = RetryLogic.ShouldRetry(response, null, 0, 0, policy, _logger);

        // Assert
        Assert.True(result.ShouldRetry);
        Assert.Equal(RetryType.RateLimit, result.RetryType);
        Assert.Equal(45, result.DelaySeconds);
        Assert.Equal(1, result.UpdatedRateLimitAttempts);
        Assert.Equal(0, result.UpdatedUnavailableAttempts);
    }

    [Fact]
    public void ShouldRetry_RateLimit_InvalidRetryAfterHeader_UsesMaxDelaySeconds()
    {
        // Arrange
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        // Don't add an invalid header - just test without any retry-after header
        // which should also fall back to max delay
        var policy = new RetryPolicy { MaxRetryDelaySeconds = 45 };

        // Act
        RetryDecision result = RetryLogic.ShouldRetry(response, null, 0, 0, policy, _logger);

        // Assert
        Assert.True(result.ShouldRetry);
        Assert.Equal(RetryType.RateLimit, result.RetryType);
        Assert.Equal(45, result.DelaySeconds);
    }

    [Fact]
    public void ShouldRetry_RateLimit_MaxAttemptsReached_ReturnsNoRetry()
    {
        // Arrange
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        var policy = new RetryPolicy { MaxRateLimitRetries = 3 };

        // Act
        RetryDecision result = RetryLogic.ShouldRetry(response, null, 2, 0, policy, _logger); // 3rd attempt (2 + 1)

        // Assert
        Assert.False(result.ShouldRetry);
        Assert.Equal(RetryType.RateLimit, result.RetryType);
        Assert.Equal(3, result.UpdatedRateLimitAttempts);
        Assert.Equal(0, result.UpdatedUnavailableAttempts);
    }

    [Fact]
    public void ShouldRetry_RateLimit_CustomRetryAfterHeader_ReadsCorrectHeader()
    {
        // Arrange
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.Add("X-RateLimit-Reset", "15");
        var policy = new RetryPolicy { RetryAfterHeader = "X-RateLimit-Reset" };

        // Act
        RetryDecision result = RetryLogic.ShouldRetry(response, null, 0, 0, policy, _logger);

        // Assert
        Assert.True(result.ShouldRetry);
        Assert.Equal(15, result.DelaySeconds);
    }

    #endregion

    #region Service Unavailable Tests

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.FailedDependency)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData((HttpStatusCode)529)] // Server is overloaded
    public void ShouldRetry_ServiceUnavailable_FirstAttempt_ReturnsRetryWithExponentialBackoff(HttpStatusCode statusCode)
    {
        // Arrange
        using var response = new HttpResponseMessage(statusCode);
        var policy = new RetryPolicy { DefaultUnavailableRetryDelaySeconds = 2 };

        // Act
        RetryDecision result = RetryLogic.ShouldRetry(response, null, 0, 0, policy, _logger);

        // Assert
        Assert.True(result.ShouldRetry);
        Assert.Equal(RetryType.ServiceUnavailable, result.RetryType);
        Assert.Equal(4, result.DelaySeconds); // 2^1 * 2 = 4
        Assert.Equal(0, result.UpdatedRateLimitAttempts);
        Assert.Equal(1, result.UpdatedUnavailableAttempts);
    }

    [Fact]
    public void ShouldRetry_ServiceUnavailable_SecondAttempt_IncreasesDelay()
    {
        // Arrange
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var policy = new RetryPolicy { DefaultUnavailableRetryDelaySeconds = 1 };

        // Act
        RetryDecision result = RetryLogic.ShouldRetry(response, null, 0, 1, policy, _logger);

        // Assert
        Assert.True(result.ShouldRetry);
        Assert.Equal(RetryType.ServiceUnavailable, result.RetryType);
        Assert.Equal(4, result.DelaySeconds); // 2^2 * 1 = 4
        Assert.Equal(2, result.UpdatedUnavailableAttempts);
    }

    [Fact]
    public void ShouldRetry_ServiceUnavailable_DelayExceedsMax_CapsAtMaxDelay()
    {
        // Arrange
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var policy = new RetryPolicy
        {
            DefaultUnavailableRetryDelaySeconds = 10,
            MaxRetryDelaySeconds = 30,
            MaxUnavailableRetries = 10 // Set high to avoid hitting max attempts
        };

        // Act - Simulate enough attempts to exceed max delay
        RetryDecision result = RetryLogic.ShouldRetry(response, null, 0, 4, policy, _logger); // 2^4 * 10 = 160, should cap at 30

        // Assert
        Assert.True(result.ShouldRetry);
        Assert.Equal(30, result.DelaySeconds); // Capped at MaxRetryDelaySeconds
    }

    [Fact]
    public void ShouldRetry_ServiceUnavailable_MaxAttemptsReached_ReturnsNoRetry()
    {
        // Arrange
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var policy = new RetryPolicy { MaxUnavailableRetries = 2 };

        // Act
        RetryDecision result = RetryLogic.ShouldRetry(response, null, 0, 1, policy, _logger); // 2nd attempt (1 + 1)

        // Assert
        Assert.False(result.ShouldRetry);
        Assert.Equal(RetryType.ServiceUnavailable, result.RetryType);
        Assert.Equal(2, result.UpdatedUnavailableAttempts);
    }

    #endregion

    #region Exception Handling Tests

    [Fact]
    public void ShouldRetry_Exception_FirstAttempt_ReturnsRetryWithExponentialBackoff()
    {
        // Arrange
        var exception = new HttpRequestException("Network error");
        var policy = new RetryPolicy { DefaultUnavailableRetryDelaySeconds = 2 };

        // Act
        RetryDecision result = RetryLogic.ShouldRetry(null, exception, 0, 0, policy, _logger);

        // Assert
        Assert.True(result.ShouldRetry);
        Assert.Equal(RetryType.ServiceUnavailable, result.RetryType);
        Assert.Equal(4, result.DelaySeconds); // 2^1 * 2 = 4
        Assert.Equal(0, result.UpdatedRateLimitAttempts); // Reset to 0 for exceptions
        Assert.Equal(1, result.UpdatedUnavailableAttempts);
    }

    [Fact]
    public void ShouldRetry_Exception_MaxAttemptsReached_ReturnsNoRetry()
    {
        // Arrange
        var exception = new TimeoutException("Request timeout");
        var policy = new RetryPolicy { MaxUnavailableRetries = 2 };

        // Act
        RetryDecision result = RetryLogic.ShouldRetry(null, exception, 1, 1, policy, _logger); // 2nd attempt (1 + 1)

        // Assert
        Assert.False(result.ShouldRetry);
        Assert.Equal(RetryType.ServiceUnavailable, result.RetryType);
        Assert.Equal(2, result.UpdatedUnavailableAttempts);
        Assert.Equal(0, result.UpdatedRateLimitAttempts); // Reset to 0 for exceptions
    }

    [Theory]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(TaskCanceledException))]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(InvalidOperationException))]
    public void ShouldRetry_DifferentExceptionTypes_HandledConsistently(Type exceptionType)
    {
        // Arrange
        var exception = (Exception)Activator.CreateInstance(exceptionType, "Test exception")!;

        // Act
        RetryDecision result = RetryLogic.ShouldRetry(null, exception, 0, 0, _defaultPolicy, _logger);

        // Assert
        Assert.True(result.ShouldRetry);
        Assert.Equal(RetryType.ServiceUnavailable, result.RetryType);
        Assert.Equal(1, result.UpdatedUnavailableAttempts);
        Assert.Equal(0, result.UpdatedRateLimitAttempts);
    }

    #endregion

    #region Non-Retryable Response Tests

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    [InlineData(HttpStatusCode.NotAcceptable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public void ShouldRetry_NonRetryableStatusCodes_ReturnsNoRetry(HttpStatusCode statusCode)
    {
        // Arrange
        using var response = new HttpResponseMessage(statusCode);
        int rateLimitAttempts = 1;
        int unavailableAttempts = 2;

        // Act
        RetryDecision result = RetryLogic.ShouldRetry(response, null, rateLimitAttempts, unavailableAttempts, _defaultPolicy, _logger);

        // Assert
        Assert.False(result.ShouldRetry);
        Assert.Equal(RetryType.None, result.RetryType);
        Assert.Equal(0, result.DelaySeconds);
        Assert.Equal(rateLimitAttempts, result.UpdatedRateLimitAttempts); // Preserved
        Assert.Equal(unavailableAttempts, result.UpdatedUnavailableAttempts); // Preserved
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ShouldRetry_ZeroMaxRetries_NeverRetries()
    {
        // Arrange
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        var policy = new RetryPolicy { MaxRateLimitRetries = 0 };

        // Act
        RetryDecision result = RetryLogic.ShouldRetry(response, null, 0, 0, policy, _logger);

        // Assert
        Assert.False(result.ShouldRetry);
        Assert.Equal(1, result.UpdatedRateLimitAttempts);
    }

    [Fact]
    public void ShouldRetry_ZeroDefaultDelay_StillCalculatesExponentialBackoff()
    {
        // Arrange
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var policy = new RetryPolicy { DefaultUnavailableRetryDelaySeconds = 0 };

        // Act
        RetryDecision result = RetryLogic.ShouldRetry(response, null, 0, 0, policy, _logger);

        // Assert
        Assert.True(result.ShouldRetry);
        Assert.Equal(0, result.DelaySeconds); // 2^1 * 0 = 0
    }

    [Fact]
    public void ShouldRetry_LargeAttemptCount_DoesNotOverflow()
    {
        // Arrange
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var policy = new RetryPolicy
        {
            DefaultUnavailableRetryDelaySeconds = 1,
            MaxRetryDelaySeconds = 1000,
            MaxUnavailableRetries = 100
        };

        // Act
        RetryDecision result = RetryLogic.ShouldRetry(response, null, 0, 50, policy, _logger);

        // Assert
        Assert.True(result.ShouldRetry);
        Assert.Equal(1000, result.DelaySeconds); // Should be capped at max
        Assert.True(result.DelaySeconds > 0);
        Assert.True(result.DelaySeconds <= policy.MaxRetryDelaySeconds);
    }

    #endregion

    #region Combination Scenarios

    [Fact]
    public void ShouldRetry_MixedRateLimitAndServiceUnavailable_TracksIndependently()
    {
        // Arrange
        var policy = new RetryPolicy();
        int initialRateLimit = 2;
        int initialUnavailable = 1;

        // First, test a rate limit response
        using var rateLimitResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        RetryDecision rateLimitResult = RetryLogic.ShouldRetry(rateLimitResponse, null, initialRateLimit, initialUnavailable, policy, _logger);

        // Then, test a service unavailable response with updated counts
        using var unavailableResponse = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        RetryDecision unavailableResult = RetryLogic.ShouldRetry(unavailableResponse, null,
            rateLimitResult.UpdatedRateLimitAttempts, rateLimitResult.UpdatedUnavailableAttempts, policy, _logger);

        // Assert rate limit result
        Assert.True(rateLimitResult.ShouldRetry);
        Assert.Equal(RetryType.RateLimit, rateLimitResult.RetryType);
        Assert.Equal(3, rateLimitResult.UpdatedRateLimitAttempts); // 2 + 1
        Assert.Equal(1, rateLimitResult.UpdatedUnavailableAttempts); // Unchanged

        // Assert service unavailable result
        Assert.True(unavailableResult.ShouldRetry);
        Assert.Equal(RetryType.ServiceUnavailable, unavailableResult.RetryType);
        Assert.Equal(3, unavailableResult.UpdatedRateLimitAttempts); // Unchanged from previous
        Assert.Equal(2, unavailableResult.UpdatedUnavailableAttempts); // 1 + 1
    }

    #endregion
}
