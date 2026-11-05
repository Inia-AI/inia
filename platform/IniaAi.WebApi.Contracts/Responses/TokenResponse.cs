using System.Text.Json.Serialization;

namespace IniaAi.WebApi.Models.Responses;

/// <summary>
/// Represents the token response returned after successful authentication.
/// </summary>
public class TokenResponse
{
    /// <summary>
    /// Gets or sets the JWT access token.
    /// This token is used to authorize requests to protected API endpoints.
    /// </summary>
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; set; }

    /// <summary>
    /// Gets or sets the refresh token.
    /// This token can be used to obtain a new access token without requiring re-authentication.
    /// </summary>
    [JsonPropertyName("refresh_token")]
    public required string RefreshToken { get; set; }

    /// <summary>
    /// Gets or sets the type of token.
    /// Typically this is "Bearer", indicating the Authorization header format: "Bearer {token}".
    /// </summary>
    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "Bearer";

    /// <summary>
    /// Gets or sets the number of seconds until the access token expires.
    /// </summary>
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    /// <summary>
    /// Gets or sets the OAuth2 scopes granted for the access token.
    /// Scopes define the level of access the token provides.
    /// </summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }
}
