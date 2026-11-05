namespace IniaAi.WebApi.Contracts.Shared.Users;

/// <summary>
/// Represents basic user information, including ID, email, username, and name details.
/// </summary>
public class UserDto
{
    /// <summary>
    /// Gets or sets the unique identifier of the user.
    /// </summary>
    public string Id { get; set; } = default!;

    /// <summary>
    /// Gets or sets the email address of the user.
    /// </summary>
    public string Email { get; set; } = default!;

    /// <summary>
    /// Gets or sets the username of the user.
    /// </summary>
    public string Username { get; set; } = default!;

    /// <summary>
    /// Gets or sets the first name of the user.
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>
    /// Gets or sets the last name of the user.
    /// </summary>
    public string? LastName { get; set; }

    /// <summary>
    /// Gets or sets the middle name of the user (optional).
    /// </summary>
    public string? MiddleName { get; set; }

    /// <summary>
    /// Gets or sets the full name of the user (computed from first, middle, and last names).
    /// </summary>
    public string FullName { get; set; } = default!;
}
