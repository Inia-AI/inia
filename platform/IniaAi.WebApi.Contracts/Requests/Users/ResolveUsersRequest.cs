namespace IniaAi.WebApi.Contracts.Requests.Users;

/// <summary>
/// Request to resolve usernames to their display information (full name).
/// Only resolves users who have created workflow reports in the current tenant.
/// </summary>
public class ResolveUsersRequest
{
    /// <summary>
    /// The list of usernames to resolve.
    /// </summary>
    public List<string> Usernames { get; set; } = [];
}
