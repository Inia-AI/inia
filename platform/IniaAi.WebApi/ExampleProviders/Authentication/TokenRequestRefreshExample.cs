using IniaAi.WebApi.Presentation.Contracts;
using IniaAi.WebApi.Swagger;

namespace IniaAi.WebApi.ExampleProviders.Authentication;

/// <summary>
/// Provides a Swagger example for refresh token requests.
/// </summary>
public class TokenRequestRefreshExample : IExamplesProvider<TokenRequest>
{
    /// <inheritdoc />
    public TokenRequest GetExamples()
    {
        return new TokenRequest
        {
            GrantType = "refresh_token",
            RefreshToken = "XXX"
        };
    }
}

