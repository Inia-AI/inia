using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace IniaAi.Common.Http;

/// <summary>
/// Generic HTTP message handler that logs requests and responses with structured logging.
/// Automatically redacts sensitive headers (API keys, tokens, etc.) from logs.
/// Optimized for Application Insights queries with individual named properties.
/// A body is logged by its size only: it can carry a customer's documents or what a model wrote about them.
/// </summary>
public class HttpLoggingHandler : DelegatingHandler
{
    private readonly ILogger _logger;
    private readonly HttpLoggingOptions _options;

    private static readonly string[] DefaultSensitiveHeaders =
    [
        "x-api-key",
        "ocp-apim-subscription-key",
        "authorization",
        "api-key",
        "apikey",
        "x-auth-token",
        "cookie",
        "set-cookie"
    ];

    public HttpLoggingHandler(ILogger logger, HttpLoggingOptions? options = null)
    {
        _logger = logger;
        _options = options ?? new HttpLoggingOptions();
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        string requestId = Guid.NewGuid().ToString("N")[..8]; // Short ID for correlation

        if (_options.LogRequests)
        {
            Dictionary<string, string>? requestHeaders = null;

            if (_options.LogHeaders)
            {
                requestHeaders = GetRedactedHeaders(request.Headers);

                // Include content headers if present
                if (request.Content is not null)
                {
                    foreach (KeyValuePair<string, IEnumerable<string>> header in request.Content.Headers)
                    {
                        requestHeaders[header.Key] = string.Join(", ", header.Value);
                    }
                }
            }

            LogRequest(requestId, request, requestHeaders);
        }

        // Execute the request
        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "[{ServiceName}-{RequestId}] HTTP request failed | Method: {RequestMethod} | Uri: {RequestUri} | Duration: {DurationMs}ms",
                _options.ServiceName,
                requestId,
                request.Method.ToString(),
                request.RequestUri?.ToString(),
                stopwatch.ElapsedMilliseconds);
            throw;
        }

        stopwatch.Stop();

        // Capture and log response
        if (_options.LogResponses)
        {
            Dictionary<string, string>? responseHeaders = null;

            if (_options.LogHeaders)
            {
                responseHeaders = GetResponseHeaders(response);
            }

            LogResponse(requestId, request, response, responseHeaders, stopwatch.ElapsedMilliseconds);
        }

        return response;
    }

    private void LogRequest(
        string requestId,
        HttpRequestMessage request,
        Dictionary<string, string>? headers)
    {
        _logger.Log(
            _options.RequestLogLevel,
            "[{ServiceName}-{RequestId}] HTTP Request | Method: {RequestMethod} | Uri: {RequestUri} | Headers: {@RequestHeaders} | Body: {RequestBytes} bytes",
            _options.ServiceName,
            requestId,
            request.Method.ToString(),
            request.RequestUri?.ToString(),
            headers,
            request.Content?.Headers.ContentLength);
    }

    private void LogResponse(
        string requestId,
        HttpRequestMessage request,
        HttpResponseMessage response,
        Dictionary<string, string>? headers,
        long durationMs)
    {
        LogLevel logLevel = response.IsSuccessStatusCode
            ? _options.ResponseLogLevel
            : _options.ErrorResponseLogLevel;

        _logger.Log(
            logLevel,
            "[{ServiceName}-{RequestId}] HTTP Response | Method: {RequestMethod} | Uri: {RequestUri} | Status: {StatusCode} | Duration: {DurationMs}ms | Headers: {@ResponseHeaders} | Body: {ResponseBytes} bytes",
            _options.ServiceName,
            requestId,
            request.Method.ToString(),
            request.RequestUri?.ToString(),
            (int)response.StatusCode,
            durationMs,
            headers,
            response.Content.Headers.ContentLength);
    }

    private Dictionary<string, string> GetRedactedHeaders(System.Net.Http.Headers.HttpHeaders headers)
    {
        var result = new Dictionary<string, string>();

        foreach (KeyValuePair<string, IEnumerable<string>> header in headers)
        {
            string value = IsSensitiveHeader(header.Key)
                ? "[REDACTED]"
                : string.Join(", ", header.Value);

            result[header.Key] = value;
        }

        return result;
    }

    private static Dictionary<string, string> GetResponseHeaders(HttpResponseMessage response)
    {
        var result = new Dictionary<string, string>();

        // Response headers
        foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers)
        {
            result[header.Key] = string.Join(", ", header.Value);
        }

        // Content headers
        if (response.Content is not null)
        {
            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Content.Headers)
            {
                result[header.Key] = string.Join(", ", header.Value);
            }
        }

        return result;
    }

    private bool IsSensitiveHeader(string headerName)
    {
        // Check custom sensitive headers first
        if (_options.SensitiveHeaders.Any(sh =>
            sh.Equals(headerName, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Check default sensitive headers
        return DefaultSensitiveHeaders.Any(sh =>
            sh.Equals(headerName, StringComparison.OrdinalIgnoreCase));
    }
}
