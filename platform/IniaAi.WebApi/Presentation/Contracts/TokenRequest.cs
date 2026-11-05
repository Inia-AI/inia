using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.WebApi.Presentation.Contracts;

/// <summary>
/// Documents the token endpoint's form fields for Swagger. It is never validated: OpenIddict
/// validates the request first, and model validation would refuse a valid grant with a 400.
/// </summary>
[ValidateNever]
public sealed class TokenRequest
{
    /// <summary>
    /// Gets or sets the user's email address, which acts as the username for authentication.
    /// </summary>
    [FromForm(Name = "username")]
    public string? Username { get; set; }

    /// <summary>
    /// Gets or sets the user's password.
    /// </summary>
    [FromForm(Name = "password")]
    public string? Password { get; set; }

    /// <summary>
    /// Gets or sets the OAuth2 grant type.
    /// Typically set to <c>password</c> for username/password flows.
    /// </summary>
    [FromForm(Name = "grant_type")]
    [Required]
    public string GrantType { get; set; } = "password";

    /// <summary>
    /// Gets or sets the client ID. Required for the client credentials grant; the web app sends
    /// none, because a browser cannot keep a client secret.
    /// </summary>
    [FromForm(Name = "client_id")]
    public string? ClientId { get; set; }

    /// <summary>
    /// Gets or sets the client secret. Required for the client credentials grant; the web app
    /// sends none.
    /// </summary>
    [FromForm(Name = "client_secret")]
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the OAuth2 scope(s) to request.
    /// Scopes are space-separated values that define the access privileges for the token.
    /// </summary>
    [FromForm(Name = "scope")]
    public string? Scope { get; set; }

    /// <summary>
    /// Gets or sets the refresh token used to obtain a new access token.
    /// Required when the grant type is <c>refresh_token</c>.
    /// </summary>
    [FromForm(Name = "refresh_token")]
    public string? RefreshToken { get; set; }
}
