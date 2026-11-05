using IniaAi.AiConnection.HttpHandling.Enums;
using Microsoft.Extensions.Logging;
using System.Net;

namespace IniaAi.AiConnection.HttpHandling;

/// <summary>
/// Shared retry logic for both HTTP and AWS connectors
/// </summary>
internal static class RetryLogic
{
    /// <summary>
    /// Determines if a response should be retried and calculates the delay
    /// </summary>
    /// <param name="response">The HTTP response (null for exceptions)</param>
    /// <param name="exception">The exception that occurred (null for HTTP responses)</param>
    /// <param name="rateLimitAttempts">Current rate limit retry attempts</param>
    /// <param name="unavailableAttempts">Current unavailable retry attempts</param>
    /// <param name="policy">The retry policy</param>
    /// <param name="logger">Logger for retry events</param>
    /// <returns>Retry decision and delay information</returns>
    public static RetryDecision ShouldRetry(
        HttpResponseMessage? response,
        Exception? exception,
        int rateLimitAttempts,
        int unavailableAttempts,
        RetryPolicy policy,
        ILogger logger)
    {
        // Handle exceptions
        if (exception is not null)
        {
            return HandleException(exception, unavailableAttempts, policy, logger);
        }

        // Handle HTTP responses
        if (response is not null)
        {
            return HandleHttpResponse(response, rateLimitAttempts, unavailableAttempts, policy, logger);
        }

        // Should not happen
        throw new ArgumentException("Either response or exception must be provided");
    }

    private static RetryDecision HandleException(
        Exception exception,
        int unavailableAttempts,
        RetryPolicy policy,
        ILogger logger)
    {
        unavailableAttempts++;

        if (unavailableAttempts >= policy.MaxUnavailableRetries)
        {
            logger.LogError(exception,
                "Maximum service unavailable retry attempts ({MaxAttempts}) reached after exception",
                policy.MaxUnavailableRetries);

            return new RetryDecision
            {
                ShouldRetry = false,
                RetryType = RetryType.ServiceUnavailable,
                UpdatedRateLimitAttempts = 0,
                UpdatedUnavailableAttempts = unavailableAttempts
            };
        }

        double delaySeconds = CalculateExponentialBackoffDelay(unavailableAttempts, policy);

        logger.LogError(exception,
            "Error occurred while sending request. Attempt {AttemptCount} of {MaxAttempts}",
            unavailableAttempts,
            policy.MaxUnavailableRetries);

        return new RetryDecision
        {
            ShouldRetry = true,
            RetryType = RetryType.ServiceUnavailable,
            DelaySeconds = delaySeconds,
            UpdatedRateLimitAttempts = 0,
            UpdatedUnavailableAttempts = unavailableAttempts
        };
    }

    private static RetryDecision HandleHttpResponse(
        HttpResponseMessage response,
        int rateLimitAttempts,
        int unavailableAttempts,
        RetryPolicy policy,
        ILogger logger)
    {
        return response.IsSuccessStatusCode
            ? new RetryDecision
            {
                ShouldRetry = false,
                RetryType = RetryType.None,
                UpdatedRateLimitAttempts = rateLimitAttempts,
                UpdatedUnavailableAttempts = unavailableAttempts
            }
            : response.StatusCode switch
            {
                // 429
                HttpStatusCode.TooManyRequests => HandleRateLimitResponse(response, rateLimitAttempts, unavailableAttempts, policy, logger),
                // 503
                HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway or HttpStatusCode.FailedDependency or HttpStatusCode.InternalServerError or (HttpStatusCode)529 => HandleServiceUnavailableResponse(response, rateLimitAttempts, unavailableAttempts, policy, logger),
                _ => new RetryDecision
                {
                    ShouldRetry = false,
                    RetryType = RetryType.None,
                    UpdatedRateLimitAttempts = rateLimitAttempts,
                    UpdatedUnavailableAttempts = unavailableAttempts
                },
            };
    }

    private static RetryDecision HandleRateLimitResponse(
        HttpResponseMessage response,
        int rateLimitAttempts,
        int unavailableAttempts,
        RetryPolicy policy,
        ILogger logger)
    {
        rateLimitAttempts++;

        if (rateLimitAttempts >= policy.MaxRateLimitRetries)
        {
            logger.LogError(
                "Maximum rate limit retry attempts ({MaxAttempts}) reached",
                policy.MaxRateLimitRetries);

            return new RetryDecision
            {
                ShouldRetry = false,
                RetryType = RetryType.RateLimit,
                UpdatedRateLimitAttempts = rateLimitAttempts,
                UpdatedUnavailableAttempts = unavailableAttempts
            };
        }

        int retryAfterSeconds = GetRetryAfterSeconds(response, policy);

        logger.LogWarning(
            "Rate limit hit. Attempt {AttemptCount} of {MaxAttempts}. Waiting {RetryAfter} seconds before retry.",
            rateLimitAttempts,
            policy.MaxRateLimitRetries,
            retryAfterSeconds);

        return new RetryDecision
        {
            ShouldRetry = true,
            RetryType = RetryType.RateLimit,
            DelaySeconds = retryAfterSeconds,
            UpdatedRateLimitAttempts = rateLimitAttempts,
            UpdatedUnavailableAttempts = unavailableAttempts
        };
    }

    private static RetryDecision HandleServiceUnavailableResponse(
        HttpResponseMessage response,
        int rateLimitAttempts,
        int unavailableAttempts,
        RetryPolicy policy,
        ILogger logger)
    {
        unavailableAttempts++;

        if (unavailableAttempts >= policy.MaxUnavailableRetries)
        {
            logger.LogError(
                "Maximum service unavailable retry attempts ({MaxAttempts}) reached",
                policy.MaxUnavailableRetries);

            return new RetryDecision
            {
                ShouldRetry = false,
                RetryType = RetryType.ServiceUnavailable,
                UpdatedRateLimitAttempts = rateLimitAttempts,
                UpdatedUnavailableAttempts = unavailableAttempts
            };
        }

        double delaySeconds = CalculateExponentialBackoffDelay(unavailableAttempts, policy);

        logger.LogWarning(
            "Service unavailable. Attempt {AttemptCount} of {MaxAttempts}. Waiting {RetryAfter} seconds before retry.",
            unavailableAttempts,
            policy.MaxUnavailableRetries,
            delaySeconds);

        return new RetryDecision
        {
            ShouldRetry = true,
            RetryType = RetryType.ServiceUnavailable,
            DelaySeconds = delaySeconds,
            UpdatedRateLimitAttempts = rateLimitAttempts,
            UpdatedUnavailableAttempts = unavailableAttempts
        };
    }

    private static double CalculateExponentialBackoffDelay(int attempts, RetryPolicy policy)
    {
        return Math.Min(
            Math.Pow(2, attempts) * policy.DefaultUnavailableRetryDelaySeconds,
            policy.MaxRetryDelaySeconds);
    }

    private static int GetRetryAfterSeconds(HttpResponseMessage response, RetryPolicy policy)
    {
        return response.Headers.TryGetValues(policy.RetryAfterHeader, out IEnumerable<string>? values) &&
            int.TryParse(values.FirstOrDefault(), out int retryAfter)
            ? retryAfter
            : policy.MaxRetryDelaySeconds;
    }
}
