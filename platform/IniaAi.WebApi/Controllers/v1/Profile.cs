using ErrorOr;
using IniaAi.Application.Auditing;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApi.Attributes;
using IniaAi.WebApi.Contracts.Requests.User;
using IniaAi.WebApi.Controllers.v1.Base;
using IniaAi.WebApi.ExampleProviders.User;
using IniaAi.WebApi.Swagger;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace IniaAi.WebApi.Controllers.v1;

/// <summary>
/// Handles logged on user details, changing of password, and ending every session.
/// </summary>
[Route("api/v{version:apiVersion}/profiles")]
public class ProfileController : ApiController
{
    private readonly UserManager<User> _userManager;
    private readonly IUserSessionRevoker _sessionRevoker;
    private readonly IAuthenticationAuditLog _audit;
    private readonly ILogger<ProfileController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileController"/> class.
    /// </summary>
    /// <param name="requestContext">The request context containing organization and user information.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="sessionRevoker">Ends the user's API sessions after a password change or on request.</param>
    /// <param name="audit">Records a sign-out from every device.</param>
    /// <param name="logger">The logger instance.</param>
    public ProfileController(
        IRequestContext requestContext,
        UserManager<User> userManager,
        IUserSessionRevoker sessionRevoker,
        IAuthenticationAuditLog audit,
        ILogger<ProfileController> logger)
        : base(requestContext)
    {
        _userManager = userManager;
        _sessionRevoker = sessionRevoker;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>
    /// Changes the password for the authenticated user.
    /// </summary>
    /// <remarks>
    /// This endpoint does not require the X-Tenant-Id header as it operates on the user account globally.
    /// Password changes apply across all tenants the user belongs to.
    /// A successful change ends every API session of the user, this one included, so the user signs
    /// in again with the new password.
    /// </remarks>
    /// <param name="request">Change password request.</param>
    /// <returns>An HTTP 200 OK if password change succeeded.</returns>
    /// <response code="200">Password successfully changed.</response>
    /// <response code="400">Invalid current password or password policy not met.</response>
    /// <response code="401">User is not authenticated.</response>
    [HttpPost("change-password")]
    [TenantOptional]
    [SwaggerRequestExample(typeof(ChangePasswordRequest), typeof(ChangePasswordRequestExample))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePasswordAsync([FromBody] ChangePasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(UserId))
        {
            _logger.LogWarning("Change password attempt without valid user identity.");

            return Unauthorized(Error.Unauthorized(description: "User identity not found."));
        }

        User? user = await _userManager.FindByIdAsync(UserId);
        if (user is null)
        {
            _logger.LogWarning("Change password attempt for non-existent user. UserId: {UserId}", UserId);

            return Unauthorized(Error.Unauthorized(description: "User not found."));
        }

        IdentityResult result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            _logger.LogWarning(
                "Password change failed for UserId: {UserId}. Errors: {Errors}",
                UserId,
                string.Join("; ", result.Errors.Select(e => e.Description)));

            var errors = result.Errors
                .Select(e => Error.Validation(e.Code, e.Description))
                .ToList();

            return Problem(errors);
        }

        await _sessionRevoker.RevokeAllAsync(user.Id, HttpContext.RequestAborted);
        _logger.LogInformation("Password successfully changed for UserId: {UserId}", UserId);

        return Ok(new { Message = "Password changed successfully." });
    }

    /// <summary>
    /// Ends every session of the authenticated user, on every device, this one included.
    /// </summary>
    /// <remarks>
    /// This endpoint does not require the X-Tenant-Id header: the sessions belong to the user, not to
    /// one tenant. Every API token of the user stops working at once, the calling one included, so the
    /// caller signs in again. A new security stamp ends the user's admin app sign-in at its next
    /// stamp check.
    /// </remarks>
    /// <response code="204">Every session of the user ended.</response>
    /// <response code="401">User is not authenticated.</response>
    [HttpPost("sign-out-everywhere")]
    [TenantOptional]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SignOutEverywhereAsync()
    {
        if (string.IsNullOrWhiteSpace(UserId))
        {
            return Unauthorized(Error.Unauthorized(description: "User identity not found."));
        }

        User? user = await _userManager.FindByIdAsync(UserId);
        if (user is null)
        {
            return Unauthorized(Error.Unauthorized(description: "User not found."));
        }

        _ = await _userManager.UpdateSecurityStampAsync(user);
        await _sessionRevoker.RevokeAllAsync(user.Id, HttpContext.RequestAborted);
        await _audit.RecordAsync(new AuthenticationAuditEvent(AuthenticationAuditKind.SignedOutEverywhere, user.Id, ClientAddress: ClientAddress), HttpContext.RequestAborted);

        return NoContent();
    }
}
