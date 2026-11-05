using IniaAi.WebApi.Models.Requests;
using IniaAi.WebApi.Swagger;

namespace IniaAi.WebApi.ExampleProviders.Authentication;

/// <summary>
/// Provides an example instance of <see cref="RefreshTokenRequest"/> for Swagger documentation.
/// </summary>
public class RefreshTokenRequestExample : IExamplesProvider<RefreshTokenRequest>
{
    /// <summary>
    /// Returns a sample <see cref="RefreshTokenRequest"/> object with pre-populated data.
    /// </summary>
    /// <returns>A <see cref="RefreshTokenRequest"/> instance containing example data.</returns>
    public RefreshTokenRequest GetExamples()
    {
        return new RefreshTokenRequest
        {
            RefreshToken = "d4a0c6b0-1234-4d80-a7e2-ff292a1a5c62"
        };
    }
}
