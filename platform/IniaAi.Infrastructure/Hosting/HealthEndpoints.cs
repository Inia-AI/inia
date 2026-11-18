using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace IniaAi.Infrastructure.Hosting;

/// <summary>
/// Maps a health check path for the methods its callers use, GET and HEAD. A plain <c>MapHealthChecks</c> answers
/// every method, TRACE included.
/// </summary>
public static class HealthEndpoints
{
    private static readonly HttpMethodMetadata _readMethods = new([HttpMethods.Get, HttpMethods.Head]);

    /// <summary>
    /// Maps the health checks at <paramref name="path"/> for GET and HEAD; any other method answers 405.
    /// </summary>
    /// <param name="endpoints">The route builder to map on.</param>
    /// <param name="path">The path of the health endpoint.</param>
    /// <param name="options">How the report is filtered and written; the framework's default when null.</param>
    /// <returns>The endpoint's convention builder.</returns>
    public static IEndpointConventionBuilder MapReadOnlyHealthChecks(this IEndpointRouteBuilder endpoints, string path, HealthCheckOptions? options = null)
    {
        IEndpointConventionBuilder endpoint = options is null ? endpoints.MapHealthChecks(path) : endpoints.MapHealthChecks(path, options);
        return endpoint.WithMetadata(_readMethods);
    }

    /// <summary>
    /// Maps the health checks at <paramref name="path"/> for GET and HEAD, writing the full report for a caller on
    /// this host only. Any other caller, such as a load balancer or a Kubernetes probe from the node, gets the same
    /// status code with the overall status alone. For a report that names internal checks and errors.
    /// </summary>
    /// <param name="endpoints">The route builder to map on.</param>
    /// <param name="path">The path of the health endpoint.</param>
    /// <param name="options">How the report is filtered and written for a caller on this host.</param>
    /// <returns>The endpoint's convention builder.</returns>
    public static IEndpointConventionBuilder MapHealthChecksWithHostOnlyReport(this IEndpointRouteBuilder endpoints, string path, HealthCheckOptions options)
    {
        RequestDelegate report = HealthChecks(endpoints, options);
        RequestDelegate status = HealthChecks(endpoints, new HealthCheckOptions
        {
            Predicate = options.Predicate,
            ResultStatusCodes = options.ResultStatusCodes,
            AllowCachingResponses = options.AllowCachingResponses,
        });
        return endpoints.Map(path, context => IsOnThisHost(context) ? report(context) : status(context)).WithMetadata(_readMethods);
    }

    private static RequestDelegate HealthChecks(IEndpointRouteBuilder endpoints, HealthCheckOptions options)
    {
        return endpoints.CreateApplicationBuilder().UseMiddleware<HealthCheckMiddleware>(Options.Create(options)).Build();
    }

    private static bool IsOnThisHost(HttpContext context)
    {
        return context.Connection.RemoteIpAddress is { } caller && IPAddress.IsLoopback(caller);
    }
}
