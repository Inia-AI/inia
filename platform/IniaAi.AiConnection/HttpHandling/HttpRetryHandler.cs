using Microsoft.Extensions.Logging;

namespace IniaAi.AiConnection.HttpHandling;

internal static partial class HttpRetryHandler
{
    public static async Task<HttpResponseMessage> SendRequestWithRetryAsync(
        HttpClient httpClient,
        string url,
        HttpContent content,
        ILogger logger,
        RetryPolicy? policy = null,
        Dictionary<string, string>? additionalHeaders = null,
        CancellationToken cancellationToken = default)
    {
        policy ??= new RetryPolicy();
        int rateLimitAttempts = 0;
        int unavailableAttempts = 0;

        while (true)
        {
            HttpResponseMessage? response = null;
            Exception? exception = null;

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = content
                };

                if (additionalHeaders is not null)
                {
                    foreach (KeyValuePair<string, string> header in additionalHeaders)
                    {
                        request.Headers.Add(header.Key, header.Value);
                    }
                }

                response = await httpClient.SendAsync(request, cancellationToken);
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            RetryDecision retryDecision = RetryLogic.ShouldRetry(
                response,
                exception,
                rateLimitAttempts,
                unavailableAttempts,
                policy,
                logger);

            rateLimitAttempts = retryDecision.UpdatedRateLimitAttempts;
            unavailableAttempts = retryDecision.UpdatedUnavailableAttempts;

            if (!retryDecision.ShouldRetry)
            {
                return exception is not null ? throw exception : response!;
            }

            await Task.Delay(TimeSpan.FromSeconds(retryDecision.DelaySeconds), cancellationToken);
        }
    }
}
