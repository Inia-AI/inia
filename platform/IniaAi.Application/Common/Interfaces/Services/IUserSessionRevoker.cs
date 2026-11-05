namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// Ends every API session of a user at once: each access and refresh token issued to them stops
/// working on its next use, so the user signs in again and gets their current roles and
/// organizations. Call it whenever an account change takes access away.
/// </summary>
public interface IUserSessionRevoker
{
    Task RevokeAllAsync(string userId, CancellationToken cancellationToken = default);
}
