using IniaAi.Application.Common.Capabilities;
using IniaAi.Common.Text;
using IniaAi.Application.Common.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace IniaAi.WebApi.Filters;

/// <summary>
/// Refuses a request to a gated endpoint when the request's organization lacks the capability, before
/// its body is read. A request with no organization has no capabilities.
/// </summary>
public sealed class CapabilityGateFilter : IAsyncAuthorizationFilter
{
    /// <summary>The title of the refusal.</summary>
    public const string TITLE = "Not included";

    /// <summary>The text of the refusal, which the apps show as it is, unless the capabilities give a reason of their own.</summary>
    public const string DETAIL = "Your account does not include this feature. Contact support if you need it.";

    /// <summary>The refusal's field that names the missing capability.</summary>
    public const string CAPABILITY_EXTENSION = "capability";

    private readonly string _capability;
    private readonly IOrganizationCapabilities _capabilities;
    private readonly IRequestContext _requestContext;
    private readonly ProblemDetailsFactory _problems;
    private readonly ILogger<CapabilityGateFilter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CapabilityGateFilter"/> class.
    /// </summary>
    public CapabilityGateFilter(
        string capability,
        IOrganizationCapabilities capabilities,
        IRequestContext requestContext,
        ProblemDetailsFactory problems,
        ILogger<CapabilityGateFilter> logger)
    {
        _capability = capability;
        _capabilities = capabilities;
        _requestContext = requestContext;
        _problems = problems;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        CancellationToken cancellationToken = context.HttpContext.RequestAborted;
        string? organizationId = _requestContext.OrganizationId;
        if (organizationId is not null && (await _capabilities.GetAsync(organizationId, cancellationToken)).Contains(_capability))
        {
            return;
        }

        _logger.LogWarning(
            "Refused by a capability: user {UserId} of organization {OrganizationId} asked for {Action}, which needs {Capability}",
            LogSanitizer.ForLog(_requestContext.UserId),
            LogSanitizer.ForLog(organizationId),
            ActionNames.Of(context.ActionDescriptor),
            _capability);

        string? reason = organizationId is null ? null : await _capabilities.RefusalAsync(organizationId, _capability, cancellationToken);
        ProblemDetails problem = _problems.CreateProblemDetails(
            context.HttpContext,
            StatusCodes.Status403Forbidden,
            TITLE,
            detail: reason ?? DETAIL,
            instance: context.HttpContext.Request.Path);
        problem.Extensions[CAPABILITY_EXTENSION] = _capability;

        context.Result = new ObjectResult(problem) { StatusCode = StatusCodes.Status403Forbidden };
    }
}
