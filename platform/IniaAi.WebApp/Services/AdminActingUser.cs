using System.Security.Claims;
using IniaAi.Application.Common.Interfaces.Services;

namespace IniaAi.WebApp.Services;

/// <summary>
/// The acting user of the admin app: the signed-in user of the session whose event is running, or
/// of the HTTP request when the code runs in a controller or a static page.
/// </summary>
public sealed class AdminActingUser(CircuitServicesAccessor circuit, IHttpContextAccessor httpContext) : IActingUser
{
    public string? UserId =>
        circuit.Services?.GetService<AppState>()?.User?.Id
        ?? httpContext.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
}
