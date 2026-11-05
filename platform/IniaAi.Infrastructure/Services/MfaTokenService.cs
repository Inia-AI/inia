using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Configuration.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// Implementation of MFA token service using Data Protection API.
/// </summary>
public class MfaTokenService : IMfaTokenService
{
    private readonly IDataProtector _protector;
    private readonly ILogger<MfaTokenService> _logger;
    private readonly TwoFactorSettings _settings;

    public MfaTokenService(
        IDataProtectionProvider dataProtectionProvider,
        ILogger<MfaTokenService> logger,
        IOptions<TwoFactorSettings> settings)
    {
        _protector = dataProtectionProvider.CreateProtector("IniaAi.MfaToken.v1");
        _logger = logger;
        _settings = settings.Value;
    }

    public string GenerateMfaToken(string userId, string email, string? ipAddress, IEnumerable<string>? scopes = null, int? validityMinutes = null)
    {
        int effectiveValidityMinutes = validityMinutes ?? _settings.MfaTokenValidityMinutes;

        var payload = new MfaTokenPayload
        {
            UserId = userId,
            Email = email,
            IpAddress = ipAddress,
            ExpiresAt = DateTime.UtcNow.AddMinutes(effectiveValidityMinutes),
            Scopes = scopes?.ToList() ?? []
        };

        string json = JsonSerializer.Serialize(payload);
        return _protector.Protect(json);
    }

    public MfaTokenPayload? ValidateMfaToken(string token, string? expectedIpAddress = null)
    {
        try
        {
            string json = _protector.Unprotect(token);
            MfaTokenPayload? payload = JsonSerializer.Deserialize<MfaTokenPayload>(json);

            if (payload is null)
            {
                _logger.LogWarning("MFA token deserialization returned null");
                return null;
            }

            // Check expiration
            if (payload.ExpiresAt < DateTime.UtcNow)
            {
                _logger.LogWarning("MFA token expired for user {UserId}", payload.UserId);
                return null;
            }

            // Check IP address matches if enforcement is enabled
            if (expectedIpAddress is not null && payload.IpAddress is not null && payload.IpAddress != expectedIpAddress)
            {
                _logger.LogWarning("MFA token IP mismatch for user {UserId}. Token IP: {TokenIp}, Request IP: {RequestIp}",
                    payload.UserId, payload.IpAddress, expectedIpAddress);

                if (_settings.EnforceIpValidation)
                {
                    _logger.LogWarning("MFA token rejected due to IP mismatch (EnforceIpValidation=true)");
                    return null;
                }
                // When EnforceIpValidation is false, we log but continue - IP can change legitimately (mobile, VPN, etc.)
            }

            return payload;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate MFA token");
            return null;
        }
    }
}
