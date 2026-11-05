using ErrorOr;
using IniaAi.Application.Commands.TwoFactor.DisableTotp;
using IniaAi.Application.Commands.TwoFactor.EnableTotp;
using IniaAi.Application.Commands.TwoFactor.RegenerateRecoveryCodes;
using IniaAi.Application.Commands.TwoFactor.RemoveAllTrustedDevices;
using IniaAi.Application.Commands.TwoFactor.RemoveTrustedDevice;
using IniaAi.Application.Commands.TwoFactor.SetupTotp;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Queries.TwoFactor.GetTrustedDevices;
using IniaAi.Application.Queries.TwoFactor.GetTwoFactorStatus;
using IniaAi.WebApi.Contracts.Requests.TwoFactor;
using IniaAi.WebApi.Contracts.Responses.TwoFactor;
using IniaAi.WebApi.Controllers.v1.Base;
using Microsoft.AspNetCore.Mvc;

namespace IniaAi.WebApi.Controllers.v1;

/// <summary>
/// Manages two-factor authentication setup and configuration for users.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/auth/2fa")]
public class TwoFactorController : ApiController
{
    private readonly ISender _sender;

    /// <summary>
    /// Initializes a new instance of the <see cref="TwoFactorController"/> class.
    /// </summary>
    public TwoFactorController(
        IRequestContext requestContext,
        ISender sender)
        : base(requestContext)
    {
        _sender = sender;
    }

    /// <summary>
    /// Gets the current 2FA status for the authenticated user.
    /// </summary>
    /// <returns>The user's 2FA configuration status.</returns>
    [HttpGet("status")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TwoFactorStatusResponse))]
    public async Task<IActionResult> GetStatus()
    {
        var query = new GetTwoFactorStatusQuery(OrganizationId, UserId);
        ErrorOr<TwoFactorStatusResponse> result = await _sender.Send(query);

        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Initiates TOTP setup by generating a new authenticator key.
    /// </summary>
    /// <returns>The shared key and QR code URI for authenticator app configuration.</returns>
    [HttpPost("totp/setup")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TotpSetupResponse))]
    public async Task<IActionResult> SetupTotp()
    {
        var command = new SetupTotpCommand(
            OrganizationId,
            UserId,
            ClientAddress,
            Request.Headers.UserAgent.ToString());

        ErrorOr<TotpSetupResponse> result = await _sender.Send(command);

        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Enables TOTP-based 2FA after verifying the provided code.
    /// </summary>
    /// <param name="request">The verification code from the authenticator app.</param>
    /// <returns>Recovery codes on successful enablement.</returns>
    [HttpPost("totp/enable")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(EnableTotpResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> EnableTotp([FromBody] EnableTotpRequest request)
    {
        var command = new EnableTotpCommand(
            OrganizationId,
            UserId,
            request.Code,
            ClientAddress,
            Request.Headers.UserAgent.ToString());

        ErrorOr<EnableTotpResponse> result = await _sender.Send(command);

        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Disables 2FA for the authenticated user after verifying the provided code.
    /// </summary>
    /// <param name="request">The verification code to confirm disable.</param>
    /// <returns>Success status.</returns>
    [HttpPost("totp/disable")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DisableTotp([FromBody] DisableTotpRequest request)
    {
        var command = new DisableTotpCommand(
            OrganizationId,
            UserId,
            request.Code,
            ClientAddress,
            Request.Headers.UserAgent.ToString());

        ErrorOr<Success> result = await _sender.Send(command);

        return result.Match<IActionResult>(
            _ => Ok(new { message = "Two-factor authentication has been disabled" }),
            Problem);
    }

    /// <summary>
    /// Generates new recovery codes, invalidating any existing ones.
    /// </summary>
    /// <returns>New set of recovery codes.</returns>
    [HttpPost("recovery-codes/regenerate")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(EnableTotpResponse))]
    public async Task<IActionResult> RegenerateRecoveryCodes()
    {
        var command = new RegenerateRecoveryCodesCommand(
            OrganizationId,
            UserId,
            ClientAddress,
            Request.Headers.UserAgent.ToString());

        ErrorOr<EnableTotpResponse> result = await _sender.Send(command);

        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Gets the list of trusted devices for the authenticated user.
    /// </summary>
    /// <returns>List of trusted devices.</returns>
    [HttpGet("trusted-devices")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<TrustedDeviceResponse>))]
    public async Task<IActionResult> GetTrustedDevices()
    {
        var query = new GetTrustedDevicesQuery(OrganizationId, UserId);
        ErrorOr<List<TrustedDeviceResponse>> result = await _sender.Send(query);

        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Removes a trusted device.
    /// </summary>
    /// <param name="deviceId">The device ID to remove.</param>
    /// <returns>Success status.</returns>
    [HttpDelete("trusted-devices/{deviceId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveTrustedDevice(string deviceId)
    {
        var command = new RemoveTrustedDeviceCommand(
            OrganizationId,
            UserId,
            deviceId,
            ClientAddress,
            Request.Headers.UserAgent.ToString());

        ErrorOr<Deleted> result = await _sender.Send(command);

        return result.Match<IActionResult>(
            _ => Ok(new { message = "Device removed" }),
            Problem);
    }

    /// <summary>
    /// Removes all trusted devices for the authenticated user.
    /// </summary>
    /// <returns>Success status.</returns>
    [HttpDelete("trusted-devices")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveAllTrustedDevices()
    {
        var command = new RemoveAllTrustedDevicesCommand(
            OrganizationId,
            UserId,
            ClientAddress,
            Request.Headers.UserAgent.ToString());

        ErrorOr<Success> result = await _sender.Send(command);

        return result.Match<IActionResult>(
            _ => Ok(new { message = "All trusted devices removed" }),
            Problem);
    }
}
