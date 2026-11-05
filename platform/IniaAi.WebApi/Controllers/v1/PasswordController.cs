using ErrorOr;
using IniaAi.Application.Commands.UserManagement.ResetPassword;
using IniaAi.Application.Commands.UserManagement.SendPasswordReset;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.WebApi.Configuration;
using IniaAi.WebApi.Contracts.Requests.User;
using IniaAi.WebApi.Controllers.v1.Base;
using IniaAi.WebApi.ExampleProviders.User;
using IniaAi.WebApi.Swagger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IniaAi.WebApi.Controllers.v1;

/// <summary>
/// Handles the forgot-password flow: the request that mails a reset link, and the reset the link's page sends.
/// </summary>
[Route("api/v{version:apiVersion}/passwords")]
public class PasswordsController : ApiController
{
    private readonly ILogger<PasswordsController> _logger;
    private readonly ISender _sender;

    /// <summary>
    /// Initializes a new instance of the <see cref="PasswordsController"/> class.
    /// </summary>
    /// <param name="requestContext">The request context containing organization and user information.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="sender">The sender.</param>
    public PasswordsController(
        IRequestContext requestContext,
        ILogger<PasswordsController> logger,
        ISender sender)
        : base(requestContext)
    {
        _logger = logger;
        _sender = sender;
    }

    /// <summary>
    /// Sends a password reset link to the user's email address.
    /// </summary>
    /// <remarks>
    /// This endpoint does not reveal whether the email exists for security purposes. With no mail
    /// provider or no <c>FrontendBaseUrl</c> configured, nothing is sent.
    /// </remarks>
    /// <param name="request">Forgot password request.</param>
    /// <returns>An HTTP 200 OK if processed successfully.</returns>
    /// <response code="200">Operation succeeded.</response>
    /// <response code="400">Invalid request payload.</response>
    [AllowAnonymous]
    [HttpPost("forgot")]
    [EnableRateLimiting(RateLimitingConfiguration.SIGN_IN_POLICY)]
    [SwaggerRequestExample(typeof(ForgotPasswordRequest), typeof(ForgotPasswordRequestExample))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    public async Task<IActionResult> ForgotPasswordAsync([FromBody] ForgotPasswordRequest request)
    {
        _ = await _sender.Send(new SendPasswordResetEmailCommand(request.Email));

        _logger.LogInformation("Password reset requested for email.");

        return Ok(); // Always return OK to prevent user enumeration
    }

    /// <summary>
    /// Sets a new password with the token from a reset mail.
    /// </summary>
    /// <param name="request">The address, the token and the new password.</param>
    /// <returns>An HTTP 200 OK when the password is set.</returns>
    /// <response code="200">The password is set.</response>
    /// <response code="400">The link is not valid any more, or the password breaks a rule.</response>
    [AllowAnonymous]
    [HttpPost("reset")]
    [EnableRateLimiting(RateLimitingConfiguration.SIGN_IN_POLICY)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    public async Task<IActionResult> ResetPasswordAsync([FromBody] ResetPasswordRequest request)
    {
        ErrorOr<Success> result = await _sender.Send(new ResetPasswordCommand(request.Email, request.Token, request.NewPassword));

        return result.Match(_ => Ok(), Problem);
    }
}
