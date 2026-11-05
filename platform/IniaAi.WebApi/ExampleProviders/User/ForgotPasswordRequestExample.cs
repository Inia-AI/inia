using IniaAi.WebApi.Contracts.Requests.User;
using IniaAi.WebApi.Swagger;

namespace IniaAi.WebApi.ExampleProviders.User;

/// <summary>
/// Provides an example payload for the forgot password request.
/// </summary>
public class ForgotPasswordRequestExample : IExamplesProvider<ForgotPasswordRequest>
{
    /// <inheritdoc />
    public ForgotPasswordRequest GetExamples()
    {
        return new ForgotPasswordRequest
        {
            Email = "user@example.com"
        };
    }
}
