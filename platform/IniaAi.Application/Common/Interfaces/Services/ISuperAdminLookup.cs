namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// Whether a user holds the super admin role, read from the stored role assignments, so a check
/// that only the user id reaches still sees the caller's role.
/// </summary>
public interface ISuperAdminLookup
{
    Task<bool> IsSuperAdminAsync(string userId, CancellationToken cancellationToken);
}
