using System.Text;

namespace IniaAi.WebApi.HealthChecks;

/// <summary>
/// Delegating handler that logs HTTP request/response details for health check endpoints.
/// </summary>
public class LoggingDelegatingHandler : DelegatingHandler
{
    private readonly ILogger<LoggingDelegatingHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoggingDelegatingHandler"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public LoggingDelegatingHandler(ILogger<LoggingDelegatingHandler> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string requestId = Guid.NewGuid().ToString("N")[..8];

        // Log detailed request information
        var requestLog = new StringBuilder();
        _ = requestLog.AppendLine($"[{requestId}] MinIO Health Check HTTP Request:");
        _ = requestLog.AppendLine($"  Method: {request.Method}");
        _ = requestLog.AppendLine($"  URL: {request.RequestUri}");
        _ = requestLog.AppendLine($"  Headers:");

        foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
        {
            _ = requestLog.AppendLine($"    {header.Key}: {string.Join(", ", header.Value)}");
        }

        if (request.Content is not null)
        {
            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Content.Headers)
            {
                _ = requestLog.AppendLine($"    {header.Key}: {string.Join(", ", header.Value)}");
            }
        }

        _logger.LogInformation(requestLog.ToString());

        DateTime startTime = DateTime.UtcNow;

        try
        {
            HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
            TimeSpan elapsed = DateTime.UtcNow - startTime;

            // Log detailed response information
            var responseLog = new StringBuilder();
            _ = responseLog.AppendLine($"[{requestId}] MinIO Health Check HTTP Response (took {elapsed.TotalMilliseconds:F2}ms):");
            _ = responseLog.AppendLine($"  Status: {(int)response.StatusCode} {response.ReasonPhrase}");
            _ = responseLog.AppendLine($"  Success: {response.IsSuccessStatusCode}");
            _ = responseLog.AppendLine($"  Headers:");

            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers)
            {
                _ = responseLog.AppendLine($"    {header.Key}: {string.Join(", ", header.Value)}");
            }

            if (response.Content is not null)
            {
                foreach (KeyValuePair<string, IEnumerable<string>> header in response.Content.Headers)
                {
                    _ = responseLog.AppendLine($"    {header.Key}: {string.Join(", ", header.Value)}");
                }

                // Always log response body for MinIO health checks (it's usually small)
                try
                {
                    string body = await response.Content.ReadAsStringAsync(cancellationToken);
                    _ = !string.IsNullOrWhiteSpace(body) ? responseLog.AppendLine($"  Body: {body}") : responseLog.AppendLine($"  Body: [Empty]");
                }
                catch (Exception ex)
                {
                    _ = responseLog.AppendLine($"  Body: [Error reading response body: {ex.Message}]");
                }
            }

            _logger.LogInformation(responseLog.ToString());

            return response;
        }
        catch (HttpRequestException httpEx)
        {
            TimeSpan duration = DateTime.UtcNow - startTime;
            _logger.LogError(httpEx,
                "[{RequestId}] MinIO Health Check HTTP Request Exception - URL: {Url}, Duration: {Duration}ms, Message: {Message}",
                requestId, request.RequestUri, duration.TotalMilliseconds, httpEx.Message);
            throw;
        }
        catch (TaskCanceledException tcEx)
        {
            TimeSpan duration = DateTime.UtcNow - startTime;
            _logger.LogError(tcEx,
                "[{RequestId}] MinIO Health Check Timeout - URL: {Url}, Duration: {Duration}ms, Timeout: {IsTimeout}",
                requestId, request.RequestUri, duration.TotalMilliseconds, tcEx.CancellationToken.IsCancellationRequested);
            throw;
        }
        catch (Exception ex)
        {
            TimeSpan duration = DateTime.UtcNow - startTime;
            _logger.LogError(ex,
                "[{RequestId}] MinIO Health Check Unexpected Error - URL: {Url}, Duration: {Duration}ms, Type: {ExceptionType}",
                requestId, request.RequestUri, duration.TotalMilliseconds, ex.GetType().Name);
            throw;
        }
    }
}
