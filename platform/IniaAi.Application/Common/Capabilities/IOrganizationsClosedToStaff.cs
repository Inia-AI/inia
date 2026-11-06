namespace IniaAi.Application.Common.Capabilities;

/// <summary>
/// The organizations Inia staff reach only with the organization's own permission, which every read
/// across organizations, and a super admin's reach, leave out. The platform closes none; a closed
/// module may.
/// </summary>
public interface IOrganizationsClosedToStaff
{
    /// <summary>
    /// The ids of the organizations closed to the staff user, as a query on the request's own UserData
    /// context for the caller to compose into its read; null when none is closed. A null user has no
    /// permission from any organization.
    /// </summary>
    IQueryable<string>? ClosedTo(string? userId);

    /// <summary>
    /// The organizations closed to every staff user whatever grants they hold, for a read that spans
    /// organizations: a support grant lets one person into one account, never into a batch read.
    /// </summary>
    IQueryable<string>? ClosedToAllStaff()
    {
        return ClosedTo(null);
    }
}

public sealed class NoOrganizationsClosedToStaff : IOrganizationsClosedToStaff
{
    public IQueryable<string>? ClosedTo(string? userId)
    {
        return null;
    }
}
