using IniaAi.WebApi.Models.Responses;
using IniaAi.WebApi.Swagger;

namespace IniaAi.WebApi.ExampleProviders.Authentication;

/// <summary>
/// Provides an example instance of <see cref="TokenResponse"/> for Swagger documentation.
/// </summary>
public class TokenResponseExample : IExamplesProvider<TokenResponse>
{
    /// <summary>
    /// Returns a sample <see cref="TokenResponse"/> object with pre-populated data.
    /// </summary>
    /// <returns>A <see cref="TokenResponse"/> instance containing example data.</returns>
    public TokenResponse GetExamples()
    {
        return new TokenResponse
        {
            AccessToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
            RefreshToken = "d12ec4e3-5592-4d3d-90f2-c5c1989a92d1",
            TokenType = "Bearer",
            ExpiresIn = 120,
            Scope = "api offline_access"
        };
    }
}
