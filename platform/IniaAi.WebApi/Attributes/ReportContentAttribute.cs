using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Reports;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace IniaAi.WebApi.Attributes;

/// <summary>
/// The endpoint reads a report's content, so it answers 403 when <see cref="IReportContentRule"/>
/// hides the content of the report its route names. A route without the named value fails closed,
/// so a wrong name never leaves the content open.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ReportContentAttribute(string routeValue = "id") : Attribute, IFilterFactory
{
    /// <summary>The title of the refusal.</summary>
    public const string TITLE = "Report not available";

    /// <summary>The route value that names the report's run.</summary>
    public string RouteValue { get; } = routeValue;

    /// <inheritdoc />
    public bool IsReusable => false;

    /// <inheritdoc />
    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
    {
        return new Gate(
            RouteValue,
            serviceProvider.GetRequiredService<IReportContentRule>(),
            serviceProvider.GetRequiredService<IRequestContext>(),
            serviceProvider.GetRequiredService<ProblemDetailsFactory>());
    }

    private sealed class Gate(string routeValue, IReportContentRule rule, IRequestContext requestContext, ProblemDetailsFactory problems) : IAsyncAuthorizationFilter
    {
        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            if (context.RouteData.Values[routeValue] is not string executionId)
            {
                throw new InvalidOperationException($"The route has no '{routeValue}' value, so the report content gate cannot tell which report it guards.");
            }

            if (requestContext.OrganizationId is not { } organizationId)
            {
                return;
            }

            string? refusal = await rule.RefusalAsync(organizationId, executionId, context.HttpContext.RequestAborted);
            if (refusal is null)
            {
                return;
            }

            ProblemDetails problem = problems.CreateProblemDetails(context.HttpContext, StatusCodes.Status403Forbidden, TITLE, detail: refusal, instance: context.HttpContext.Request.Path);
            context.Result = new ObjectResult(problem) { StatusCode = StatusCodes.Status403Forbidden };
        }
    }
}
