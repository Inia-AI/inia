using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.UserManagement;
using Microsoft.AspNetCore.Identity;

namespace IniaAi.Application.Commands.UserManagement.ResetPassword;

/// <summary>
/// Sets the new password when the token is valid for the address. An unknown address and a wrong,
/// expired or used token get one answer, so the reset page does not reveal which accounts exist.
/// A password that breaks a rule gets that rule back. A new password ends the user's API sessions.
/// </summary>
public class ResetPasswordCommandHandler(UserManager<User> userManager, IUserSessionRevoker sessionRevoker) : IRequestHandler<ResetPasswordCommand, ErrorOr<Success>>
{
    public const string INVALID_LINK = "PasswordReset.InvalidLink";

    private static readonly Error _invalidLink = Error.Validation(INVALID_LINK, "This reset link is not valid any more. Ask for a new one.");

    public async Task<ErrorOr<Success>> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        User? user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return _invalidLink;
        }

        IdentityResult result = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (result.Succeeded)
        {
            await sessionRevoker.RevokeAllAsync(user.Id, cancellationToken);

            return Result.Success;
        }

        string invalidTokenCode = userManager.ErrorDescriber.InvalidToken().Code;
        if (result.Errors.Any(error => error.Code == invalidTokenCode))
        {
            return _invalidLink;
        }

        return result.Errors.Select(error => Error.Validation(error.Code, error.Description)).ToList();
    }
}
