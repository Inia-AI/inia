using Microsoft.Extensions.Logging;

namespace IniaAi.Common.Http;

/// <summary>
/// Configuration options for HTTP logging behavior.
/// </summary>
public class HttpLoggingOptions
{
    /// <summary>
    /// Service name to include in log messages (e.g., "Exa", "Anthropic").
    /// Used as a structured log property for filtering in Application Insights.
    /// </summary>
    public string ServiceName { get; set; } = "HTTP";

    /// <summary>
    /// Whether to log HTTP requests.
    /// </summary>
    public bool LogRequests { get; set; } = true;

    /// <summary>
    /// Whether to log HTTP responses.
    /// </summary>
    public bool LogResponses { get; set; } = true;

    /// <summary>
    /// Whether to log request/response headers.
    /// </summary>
    public bool LogHeaders { get; set; } = true;

    /// <summary>
    /// Log level for successful requests.
    /// Default: Debug (only visible when Debug logging is enabled).
    /// </summary>
    public LogLevel RequestLogLevel { get; set; } = LogLevel.Debug;

    /// <summary>
    /// Log level for successful responses.
    /// Default: Debug (only visible when Debug logging is enabled).
    /// </summary>
    public LogLevel ResponseLogLevel { get; set; } = LogLevel.Debug;

    /// <summary>
    /// Log level for error responses (4xx, 5xx status codes).
    /// Default: Warning (always visible).
    /// </summary>
    public LogLevel ErrorResponseLogLevel { get; set; } = LogLevel.Warning;

    /// <summary>
    /// Additional sensitive header names to redact (beyond the default list).
    /// Default sensitive headers: x-api-key, ocp-apim-subscription-key, authorization, etc.
    /// </summary>
    public List<string> SensitiveHeaders { get; set; } = [];
}
