using IniaAi.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using System.Security.Cryptography;
using System.Text;

namespace IniaAi.WebApp.Attributes;

/// <summary>
/// Authorization attribute that supports both role-based authorization and API key authentication.
/// Allows access if the user has the required roles OR provides a valid API key.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RoleOrMessageProcessorApiKeyAuthorizeAttribute : Attribute, IAuthorizationFilter
{
    private const string ADMITTED_BY_API_KEY = "IniaAi.AdmittedByMessageProcessorApiKey";

    private readonly string[] _roles;

    public RoleOrMessageProcessorApiKeyAuthorizeAttribute(params string[] roles)
    {
        _roles = roles ?? throw new ArgumentNullException(nameof(roles));
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated == true)
        {
            // If no roles specified, allow any authenticated user. Otherwise require a matching role.
            if (_roles.Length == 0 || _roles.Any(context.HttpContext.User.IsInRole))
            {
                return;
            }
        }

        PlatformSettings platformSettings = context
            .HttpContext
            .RequestServices
            .GetRequiredService<IOptions<PlatformSettings>>()
            .Value;
        string? expectedApiKey = platformSettings.MessageProcessor.ApiKey;

        if (string.IsNullOrEmpty(expectedApiKey))
        {
            context.Result = new UnauthorizedObjectResult(new
            {
                error = $"Access denied. Required roles: {string.Join(", ", _roles)} or valid API key."
            });

            return;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue("X-API-Key", out StringValues providedApiKey))
        {
            context.Result = new UnauthorizedObjectResult(new
            {
                error = $"Access denied. Required roles: {string.Join(", ", _roles)} or include X-API-Key header."
            });

            return;
        }

        byte[] expectedApiKeyBytes = Encoding.UTF8.GetBytes(expectedApiKey);
        byte[] providedApiKeyBytes = Encoding.UTF8.GetBytes(providedApiKey!);

        if (!CryptographicOperations.FixedTimeEquals(expectedApiKeyBytes, providedApiKeyBytes))
        {
            context.Result = new UnauthorizedObjectResult(new
            {
                error = "Invalid API Key."
            });
            return;
        }

        context.HttpContext.Items[ADMITTED_BY_API_KEY] = true;
    }

    /// <summary>
    /// Whether this request was admitted by the message processor's API key rather than by a role.
    /// The key carries no user, and acts only for the organization the processor files into.
    /// </summary>
    public static bool AdmittedByApiKey(HttpContext httpContext)
    {
        return httpContext.Items.ContainsKey(ADMITTED_BY_API_KEY);
    }
}
