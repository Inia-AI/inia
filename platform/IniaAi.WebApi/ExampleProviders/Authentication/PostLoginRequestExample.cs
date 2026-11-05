using IniaAi.WebApi.Presentation.Contracts;
using IniaAi.WebApi.Swagger;
using OpenIddict.Abstractions;

namespace IniaAi.WebApi.ExampleProviders.Authentication;

/// <summary>
/// Provides an example instance of <see cref="TokenRequest"/> for Swagger documentation.
/// </summary>
public class PostLoginRequestExample : IExamplesProvider<TokenRequest>
{
    /// <summary>
    /// Returns a sample <see cref="TokenRequest"/> object with pre-populated data.
    /// </summary>
    /// <returns>A <see cref="TokenRequest"/> instance containing example data.</returns>
    public TokenRequest GetExamples()
    {
        return new TokenRequest
        {
            Username = "user@example.com",
            Password = "P@ssw0rd!",
            GrantType = OpenIddictConstants.GrantTypes.Password,
            Scope = "offline_access api"
        };
    }
}
