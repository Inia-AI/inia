using IniaAi.Domain.Entities.UserManagement;

namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// How long a user's session may last. The token endpoint asks at every sign-in and every refresh:
/// a refresh past the absolute limit, counted from the session's first sign-in, is refused, and no
/// token it issues outlives that limit. The idle lifetime replaces the configured refresh-token
/// lifetime, so a session left unused for longer ends. The default sets neither.
/// </summary>
public interface ISessionLifetimePolicy
{
    ValueTask<SessionLifetime> GetAsync(User user, CancellationToken cancellationToken);
}

/// <param name="AbsoluteLimit">The longest a session lasts from its first sign-in, or null for no limit.</param>
/// <param name="IdleLifetime">The refresh token's lifetime for this user, or null for the configured one.</param>
public sealed record SessionLifetime(TimeSpan? AbsoluteLimit, TimeSpan? IdleLifetime)
{
    public static readonly SessionLifetime Unlimited = new(null, null);
}
