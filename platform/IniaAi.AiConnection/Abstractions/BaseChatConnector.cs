using IniaAi.AiConnection.Common;
using IniaAi.AiConnection.Common.ChatConnector;
using Microsoft.Extensions.Logging;

namespace IniaAi.AiConnection.Abstractions;

/// <summary>
/// Provides a reusable base implementation for LLM connectors that need
/// token-based rate limiting and optional concurrency control.
/// </summary>
public abstract class BaseChatConnector
{
    /// <summary>
    /// Controls the maximum allowed tokens in a rolling time window.
    /// </summary>
    protected readonly TokenSlidingWindowRateLimiter TokenRateLimiter;

    /// <summary>
    /// Controls the maximum number of concurrent LLM requests.
    /// </summary>
    protected readonly SemaphoreSlim ConcurrencySemaphore;

    ///// <summary>
    ///// Shared HttpClient instance for LLM API calls.
    ///// </summary>
    //protected readonly HttpClient HttpClient;

    /// <summary>
    /// Initializes the <see cref="BaseChatConnector"/> with a token rate limit and concurrency limit.
    /// </summary>
    /// <param name="maxTokensPerWindow">Maximum allowed input tokens in the window.</param>
    /// <param name="window">Time window for token budget (e.g., 1 minute).</param>
    /// <param name="maxConcurrency">Maximum allowed parallel API calls.</param>
    /// <param name="httpClient">HttpClient to use for HTTP calls.</param>
    protected BaseChatConnector(
        ILogger logger,
        int maxConcurrency,
        TokenSlidingWindowRateLimiter limiter)
    {
        TokenRateLimiter = limiter ?? throw new ArgumentNullException(nameof(limiter));
        ConcurrencySemaphore = new SemaphoreSlim(maxConcurrency);
    }

    /// <summary>
    /// Sends a request to the LLM provider after enforcing token rate limit and concurrency.
    /// </summary>
    /// <param name="requestBody">The request payload to send.</param>
    /// <param name="estimatedTokens">Estimated input tokens for this request.</param>
    /// <param name="sendAsync">A delegate that sends the actual HTTP request.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>Returns the raw response string from the LLM provider.</returns>
    /// <remarks>
    /// Child classes should call this helper to enforce global limits.
    /// </remarks>
    protected async Task<ResponseContentSequence> SendRequestWithGuardsAsync(
        int estimatedTokens,
        Func<Task<ResponseContentSequence>> sendAsync,
        CancellationToken cancellationToken = default)
    {
        // Wait for concurrency slot
        await ConcurrencySemaphore.WaitAsync(cancellationToken);

        try
        {
            // Wait for enough available tokens in the rolling window
            await TokenRateLimiter.WaitForPermissionAsync(estimatedTokens, cancellationToken);

            // Call the actual HTTP request delegate
            return await sendAsync();
        }
        finally
        {
            _ = ConcurrencySemaphore.Release();
        }
    }
}
