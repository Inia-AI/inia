using System.ComponentModel.DataAnnotations;

namespace IniaAi.WebApi.Models.Requests;

/// <summary>
/// Represents a request to refresh an access token using a provided refresh token.
/// </summary>
public sealed class RefreshTokenRequest
{
    /// <summary>
    /// Gets or sets the refresh token used to obtain a new access token.
    /// </summary>
    [Required]
    [MinLength(30)]
    public required string RefreshToken { get; set; }
}
