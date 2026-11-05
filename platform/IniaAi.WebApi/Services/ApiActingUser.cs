using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Services.Abstractions.Services;

namespace IniaAi.WebApi.Services;

/// <summary>
/// The acting user of an API request: the signed-in caller named by the token.
/// </summary>
public sealed class ApiActingUser(ICurrentUserService currentUser) : IActingUser
{
    /// <inheritdoc />
    public string? UserId => currentUser.GetUserId();
}
