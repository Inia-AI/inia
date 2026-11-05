using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using System.Net;
using RetryPolicy = IniaAi.AiConnection.HttpHandling.RetryPolicy;

namespace IniaAi.AiConnection.AwsBedrock;

/// <summary>
/// Factory for creating Polly resilience pipelines for AWS Bedrock API calls.
/// </summary>
/// <remarks>
/// <para>
/// This factory creates resilience pipelines that handle transient failures when calling
/// AWS Bedrock APIs. The retry logic operates at the application level (above the AWS SDK
/// signing layer), ensuring that each retry attempt gets a fresh SigV4 signature.
/// </para>
/// <para>
/// AWS SigV4 signatures expire after 5 minutes. By retrying at the application level
/// rather than the HTTP level, we avoid signature expiration errors during extended
/// retry sequences.
/// </para>
/// </remarks>
internal static class BedrockResiliencePipelineFactory
{
    /// <summary>
    /// HTTP status codes that indicate transient failures and should be retried.
    /// </summary>
    private static readonly HashSet<HttpStatusCode> TransientStatusCodes =
    [
        HttpStatusCode.TooManyRequests,      // 429 - Rate limited
        HttpStatusCode.InternalServerError,  // 500 - Server error
        HttpStatusCode.BadGateway,           // 502 - Bad gateway
        HttpStatusCode.ServiceUnavailable,   // 503 - Service unavailable
        HttpStatusCode.GatewayTimeout,       // 504 - Gateway timeout
        (HttpStatusCode)529                  // 529 - Server overloaded (AWS-specific)
    ];

    /// <summary>
    /// Creates a resilience pipeline for AWS Bedrock InvokeModel API calls using the specified retry policy.
    /// </summary>
    /// <param name="logger">Logger for recording retry events.</param>
    /// <param name="retryPolicy">
    /// The retry policy containing configuration for retry attempts and delays.
    /// Uses <see cref="RetryPolicy.MaxUnavailableRetries"/> for service unavailability retries,
    /// <see cref="RetryPolicy.MaxRateLimitRetries"/> for rate limit retries,
    /// <see cref="RetryPolicy.DefaultUnavailableRetryDelaySeconds"/> for base delay,
    /// and <see cref="RetryPolicy.MaxRetryDelaySeconds"/> for maximum delay cap.
    /// </param>
    /// <returns>A configured <see cref="ResiliencePipeline{T}"/> for InvokeModelResponse.</returns>
    /// <remarks>
    /// <para>
    /// The pipeline uses exponential backoff with jitter to prevent thundering herd problems.
    /// The delay formula is: min(2^attempt * baseDelay + random(0-1), maxDelaySeconds)
    /// </para>
    /// <para>
    /// Retryable exceptions include:
    /// <list type="bullet">
    ///   <item><description><see cref="ThrottlingException"/> - Rate limit exceeded (429)</description></item>
    ///   <item><description><see cref="ServiceUnavailableException"/> - Service temporarily unavailable (503)</description></item>
    ///   <item><description><see cref="InternalServerException"/> - Internal server error (500)</description></item>
    ///   <item><description><see cref="ModelTimeoutException"/> - Model inference timeout</description></item>
    ///   <item><description><see cref="ModelStreamErrorException"/> - Streaming error</description></item>
    ///   <item><description><see cref="HttpRequestException"/> - Network-level failures</description></item>
    ///   <item><description>Timeout exceptions</description></item>
    ///   <item><description><see cref="AmazonBedrockRuntimeException"/> with transient status codes</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Non-retryable exceptions (will fail immediately):
    /// <list type="bullet">
    ///   <item><description><see cref="AccessDeniedException"/> - Permission denied (403)</description></item>
    ///   <item><description><see cref="ResourceNotFoundException"/> - Model not found (404)</description></item>
    ///   <item><description><see cref="ValidationException"/> - Invalid request (400)</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public static ResiliencePipeline<InvokeModelResponse> Create(
        ILogger logger,
        RetryPolicy retryPolicy)
    {
        // Use the higher of the two retry counts to handle both rate limits and service unavailability
        int maxRetryAttempts = Math.Max(retryPolicy.MaxRateLimitRetries, retryPolicy.MaxUnavailableRetries);
        int baseDelaySeconds = retryPolicy.DefaultUnavailableRetryDelaySeconds;
        int maxDelaySeconds = retryPolicy.MaxRetryDelaySeconds;

        logger.LogInformation(
            "Creating Bedrock resilience pipeline: MaxRetryAttempts={MaxRetryAttempts}, " +
            "BaseDelaySeconds={BaseDelaySeconds}, MaxDelaySeconds={MaxDelaySeconds}",
            maxRetryAttempts,
            baseDelaySeconds,
            maxDelaySeconds);

        return new ResiliencePipelineBuilder<InvokeModelResponse>()
            .AddRetry(new RetryStrategyOptions<InvokeModelResponse>
            {
                ShouldHandle = new PredicateBuilder<InvokeModelResponse>()
                    .Handle<ThrottlingException>()
                    .Handle<ServiceUnavailableException>()
                    .Handle<InternalServerException>()
                    .Handle<ModelTimeoutException>()
                    .Handle<ModelStreamErrorException>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>(ex => ex.InnerException is TimeoutException)
                    .Handle<AmazonBedrockRuntimeException>(IsTransientException),

                MaxRetryAttempts = maxRetryAttempts,

                DelayGenerator = args => GenerateDelay(args.AttemptNumber, baseDelaySeconds, maxDelaySeconds),

                OnRetry = args =>
                {
                    LogRetryAttempt(logger, args, maxRetryAttempts);
                    return default;
                }
            })
            .Build();
    }

    /// <summary>
    /// Determines if an <see cref="AmazonBedrockRuntimeException"/> represents a transient failure
    /// that should be retried.
    /// </summary>
    /// <param name="exception">The exception to evaluate.</param>
    /// <returns>
    /// <c>true</c> if the exception represents a transient failure; otherwise, <c>false</c>.
    /// </returns>
    private static bool IsTransientException(AmazonBedrockRuntimeException exception)
    {
        // Check if the status code indicates a transient error
        if (exception.StatusCode != default && TransientStatusCodes.Contains(exception.StatusCode))
        {
            return true;
        }

        // Check for signature expiration (clock skew or long-running operations)
        // This shouldn't happen with application-level retry, but handle it defensively
        return exception.Message.Contains("Signature expired", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Generates the delay for a retry attempt using exponential backoff with jitter.
    /// </summary>
    /// <param name="attemptNumber">The current retry attempt number (0-based).</param>
    /// <param name="baseDelaySeconds">Base delay in seconds for exponential calculation.</param>
    /// <param name="maxDelaySeconds">Maximum delay in seconds.</param>
    /// <returns>The delay to wait before the next retry attempt.</returns>
    /// <remarks>
    /// The delay formula is: min(2^(attempt+1) * baseDelay + random(0-1), maxDelaySeconds)
    /// With baseDelay=1, this provides delays of approximately 2s, 4s, 8s, 16s, 32s for attempts 0-4.
    /// Jitter is added to prevent synchronized retries from multiple clients.
    /// </remarks>
    private static ValueTask<TimeSpan?> GenerateDelay(int attemptNumber, int baseDelaySeconds, int maxDelaySeconds)
    {
        // Exponential backoff: 2^(attempt+1) * baseDelay gives us 2*base, 4*base, 8*base, 16*base, 32*base...
        double exponentialDelay = Math.Pow(2, attemptNumber + 1) * baseDelaySeconds;

        // Add jitter (0-1 second) to prevent thundering herd
        double jitter = Random.Shared.NextDouble();

        // Cap at max delay
        double totalDelaySeconds = Math.Min(exponentialDelay + jitter, maxDelaySeconds);

        return new ValueTask<TimeSpan?>(TimeSpan.FromSeconds(totalDelaySeconds));
    }

    /// <summary>
    /// Logs information about a retry attempt.
    /// </summary>
    /// <param name="logger">The logger to use.</param>
    /// <param name="args">The retry event arguments.</param>
    /// <param name="maxRetryAttempts">The maximum number of retry attempts configured.</param>
    private static void LogRetryAttempt(
        ILogger logger,
        OnRetryArguments<InvokeModelResponse> args,
        int maxRetryAttempts)
    {
        Exception? exception = args.Outcome.Exception;
        string exceptionType = exception?.GetType().Name ?? "Unknown";
        string exceptionMessage = exception?.Message ?? "No exception message";

        // Extract status code if available
        string statusCode = exception switch
        {
            AmazonBedrockRuntimeException bedrockEx => bedrockEx.StatusCode.ToString(),
            _ => "N/A"
        };

        logger.LogWarning(
            "AWS Bedrock retry attempt {AttemptNumber} of {MaxAttempts}. " +
            "Waiting {DelaySeconds:F1}s before retry. " +
            "Exception: {ExceptionType}, StatusCode: {StatusCode}, Message: {Message}",
            args.AttemptNumber + 1,
            maxRetryAttempts,
            args.RetryDelay.TotalSeconds,
            exceptionType,
            statusCode,
            exceptionMessage);
    }
}
