namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// The user on whose behalf the current code runs: the signed-in caller of an API request or the
/// user of an admin-app session. Empty for a background job, which acts for no one.
/// </summary>
public interface IActingUser
{
    string? UserId { get; }
}
