namespace IniaAi.WebApi.Contracts.Shared.Users;

/// <summary>
/// Represents resolved user information for display purposes.
/// </summary>
public class ResolvedUserDto
{
    /// <summary>
    /// Gets or sets the username that was resolved.
    /// </summary>
    public string Username { get; set; } = default!;

    /// <summary>
    /// Gets or sets the full name of the user for display.
    /// </summary>
    public string? FullName { get; set; }
}
