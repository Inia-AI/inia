using System.ComponentModel.DataAnnotations;
using System.Text;

namespace IniaAi.Configuration.Security;

/// <summary>
/// Configuration settings for JWT access-token issuance.
/// </summary>
public class JwtSettings : IValidatableObject
{
    /// <summary>
    /// The section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Jwt";

    private const int MINIMUM_SECRET_KEY_BYTES = 32;
    private const string SECRET_KEY_SETTING = $"{SectionName}:{nameof(SecretKey)}";
    private const string GENERATE_HINT = "Generate one with `openssl rand -base64 48`.";

    /// <summary>
    /// Access-token validity in minutes. Positive-required and validated at startup:
    /// a missing or non-positive value would otherwise silently mint instantly-expired tokens.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int TokenValidityInMinutes { get; set; }

    /// <summary>
    /// Refresh-token validity in minutes. A session refreshed within it continues, so this is how
    /// long a session may sit unused. Defaults to <see cref="TokenValidityInMinutes"/>.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int? RefreshTokenValidityInMinutes { get; set; }

    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(TokenValidityInMinutes);

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromMinutes(RefreshTokenValidityInMinutes ?? TokenValidityInMinutes);

    /// <summary>
    /// Symmetric signing key for session and run tokens. Each deployment supplies its own:
    /// no settings file ships one, and startup refuses a missing or short key.
    /// </summary>
    [Required(ErrorMessage = $"{SECRET_KEY_SETTING} is required. {GENERATE_HINT}")]
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Issuer stamped into issued tokens and enforced by token validation.
    /// </summary>
    [Required]
    public string Issuer { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrEmpty(SecretKey) && Encoding.UTF8.GetByteCount(SecretKey) < MINIMUM_SECRET_KEY_BYTES)
        {
            yield return new ValidationResult(
                $"{SECRET_KEY_SETTING} must be at least {MINIMUM_SECRET_KEY_BYTES} bytes. {GENERATE_HINT}",
                [nameof(SecretKey)]);
        }
    }
}
