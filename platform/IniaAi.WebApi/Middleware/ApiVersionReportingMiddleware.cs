using Asp.Versioning;

namespace IniaAi.WebApi.Middleware;

/// <summary>
/// Reports the supported API versions on every response of a versioned endpoint, including the
/// refusals that never reach the action: the tenant rejection, the 401 and 403 of authentication
/// and authorization, and the model-state 400. The versioning library's own reporting runs as an
/// action filter and misses all of them.
/// </summary>
public class ApiVersionReportingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IReportApiVersions _reporter;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiVersionReportingMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware delegate.</param>
    /// <param name="reporter">The versioning library's reporter, which honours its ReportApiVersions option.</param>
    public ApiVersionReportingMiddleware(RequestDelegate next, IReportApiVersions reporter)
    {
        _next = next;
        _reporter = reporter;
    }

    /// <summary>
    /// Registers the report on the response of a versioned endpoint, then continues the pipeline.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task that completes when the rest of the pipeline has run.</returns>
    public Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<ApiVersionMetadata>() is { } metadata)
        {
            context.Response.OnStarting(() =>
            {
                _reporter.Report(context.Response, metadata.Map(_reporter.Mapping));
                return Task.CompletedTask;
            });
        }

        return _next(context);
    }
}
