using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.UserManagement.SendPasswordReset;

/// <summary>
/// Command to initiate sending a password reset email.
/// </summary>
public class SendPasswordResetEmailCommand : IRequest<ErrorOr<Success>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SendPasswordResetEmailCommand"/> class.
    /// </summary>
    /// <param name="email">The address the requester typed.</param>
    public SendPasswordResetEmailCommand(string email)
    {
        Email = email;
    }

    /// <summary>
    /// Gets the address the requester typed. It only finds the account: the mail and its link carry
    /// the address stored on that account.
    /// </summary>
    public string Email { get; }
}
