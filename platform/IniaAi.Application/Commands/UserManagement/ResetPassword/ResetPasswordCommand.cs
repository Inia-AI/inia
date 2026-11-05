using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.UserManagement.ResetPassword;

/// <summary>
/// Sets a new password with the token a reset mail carried.
/// </summary>
public record ResetPasswordCommand(string Email, string Token, string NewPassword) : IRequest<ErrorOr<Success>>;
