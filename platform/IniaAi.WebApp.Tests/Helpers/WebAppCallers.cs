using IniaAi.Configuration;
using IniaAi.WebApp.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace IniaAi.WebApp.Tests.Helpers;

/// <summary>
/// The callers a WebApp controller serves: a signed-in user with roles, and the message processor's
/// client, which carries no user and is admitted by its API key.
/// </summary>
public static class WebAppCallers
{
    private const string API_KEY_HEADER = "X-API-Key";

    public static ClaimsPrincipal SignedIn(string userId, params string[] roles)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId), .. roles.Select(role => new Claim(ClaimTypes.Role, role))],
            "test"));
    }

    public static ControllerContext SignedInRequest(string userId, params string[] roles)
    {
        return new ControllerContext { HttpContext = new DefaultHttpContext { User = SignedIn(userId, roles) } };
    }

    /// <summary>
    /// A request carrying the processor's API key, admitted by the real authorization attribute as the
    /// controllers see it in the app.
    /// </summary>
    public static ControllerContext ApiKeyRequest(PlatformSettings platformSettings)
    {
        var request = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(Options.Create(platformSettings)).BuildServiceProvider(),
        };
        request.Request.Headers[API_KEY_HEADER] = platformSettings.MessageProcessor.ApiKey;

        var authorization = new AuthorizationFilterContext(new ActionContext(request, new RouteData(), new ActionDescriptor()), []);
        new RoleOrMessageProcessorApiKeyAuthorizeAttribute().OnAuthorization(authorization);

        if (authorization.Result is not null)
        {
            throw new InvalidOperationException("The API key was refused; set PlatformSettings.MessageProcessor.ApiKey.");
        }

        return new ControllerContext { HttpContext = request };
    }
}
