using Asp.Versioning;
using ErrorOr;
using IniaAi.Application.Errors;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.RateLimits;
using IniaAi.Configuration;
using IniaAi.Domain.Common;
using IniaAi.WebApi.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;

namespace IniaAi.WebApi.Controllers.v1.Base;

/// <summary>
/// Base API controller that provides shared functionality such as error handling, versioning,
/// and convenient access to tenant (organization) and user context.
/// All v1 controllers inherit from this base.
/// </summary>
[Authorize]
[Produces("application/json")]
[Consumes("application/json")]
[ApiController]
[ApiVersion("1.0")]
public class ApiController : ControllerBase
{
    /// <summary>
    /// The request body limit for an endpoint that receives files or a bundle. Kestrel's default
    /// (30 MB) is too small for a document batch or a large export.
    /// </summary>
    public const int LARGE_PAYLOAD_LIMIT_BYTES = 200_000_000;

    /// <summary>
    /// The client's address, as the forwarded-headers settings resolve it behind a proxy.
    /// </summary>
    protected string? ClientAddress => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>
    /// How this instance names itself in an exported bundle: the configured instance name, else
    /// the request host.
    /// </summary>
    protected string InstanceName => InstanceIdentity.Resolve(
        HttpContext.RequestServices.GetRequiredService<IOptions<PlatformSettings>>().Value.InstanceName,
        Request.Host.Value);

    /// <summary>
    /// Gets the organization ID (tenant ID) for the current request.
    /// This is populated by <see cref="Middleware.TenantUserContextMiddleware"/> and validated for authenticated endpoints.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if organization context is not available (should not happen for [Authorize] endpoints).</exception>
    protected string OrganizationId => RequestContext.OrganizationId
        ?? throw new InvalidOperationException("Organization context not available");

    /// <summary>
    /// Parses raw JSON into a detached <see cref="System.Text.Json.JsonElement"/> so a response can
    /// carry it verbatim: serializing a domain entity through the API's camelCase policy would
    /// mangle the PascalCase domain JSON.
    /// </summary>
    protected static System.Text.Json.JsonElement ParseVerbatimJson(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }

    /// <summary>
    /// Gets the user ID for the current request.
    /// This is populated by <see cref="Middleware.TenantUserContextMiddleware"/> and validated for authenticated endpoints.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if user context is not available (should not happen for [Authorize] endpoints).</exception>
    protected string UserId => RequestContext.UserId
        ?? throw new InvalidOperationException("User context not available");

    /// <summary>
    /// Gets the request context containing organization and user information.
    /// </summary>
    protected IRequestContext RequestContext { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiController"/> class.
    /// </summary>
    /// <param name="requestContext">The request context containing organization and user information.</param>
    public ApiController(IRequestContext requestContext)
    {
        RequestContext = requestContext;
    }

    /// <summary>
    /// Generates a standardized error response based on a list of <see cref="Error"/> instances.
    /// </summary>
    /// <param name="errors">A list of errors to convert into an appropriate HTTP response.</param>
    /// <returns>An <see cref="ActionResult"/> representing the error.</returns>
    protected ActionResult Problem(List<Error> errors)
    {
        if (errors.Count == 0)
        {
            return Problem(); // Generic 500
        }

        return errors.All(error => error.Type is ErrorType.Validation)
            ? ValidationProblem(errors)
            : Problem(errors[0]);
    }

    /// <summary>
    /// Generates a problem response from a single <see cref="Error"/>.
    /// </summary>
    /// <param name="error">The error to translate into a response.</param>
    /// <returns>An <see cref="ObjectResult"/> with appropriate status code and title.</returns>
    private ObjectResult Problem(Error error)
    {
        int statusCode = error.Type switch
        {
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            (ErrorType)RateLimitRefusal.ERROR_TYPE => StatusCodes.Status429TooManyRequests,
            (ErrorType)ServiceUnavailableError.ERROR_TYPE => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError,
        };

        ObjectResult result = Problem(statusCode: statusCode, title: error.Description);

        if (result.Value is ProblemDetails problemDetails)
        {
            if (!string.IsNullOrWhiteSpace(error.Code))
            {
                problemDetails.Extensions["code"] = error.Code;
            }

            if (error.Metadata is not null)
            {
                foreach ((string key, object value) in error.Metadata)
                {
                    if (value is RateLimitRefusal limit)
                    {
                        RateLimitHeaders.WriteRefusal(Response, limit.PolicyName, limit.Quota, limit.WindowSeconds, limit.RetryAfterSeconds);
                        continue;
                    }

                    problemDetails.Extensions[key] = value;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Converts a list of validation errors into a model state dictionary and returns a validation problem result.
    /// </summary>
    /// <param name="errors">A list of validation <see cref="Error"/> instances.</param>
    /// <returns>An <see cref="ActionResult"/> containing validation problem details.</returns>
    private ActionResult ValidationProblem(List<Error> errors)
    {
        var modelState = new ModelStateDictionary();
        errors.ForEach(error => modelState.AddModelError(error.Code, error.Description));

        return ValidationProblem(modelState);
    }
}
