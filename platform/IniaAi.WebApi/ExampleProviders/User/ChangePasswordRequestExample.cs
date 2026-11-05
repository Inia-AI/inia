using IniaAi.WebApi.Contracts.Requests.User;
using IniaAi.WebApi.Swagger;

namespace IniaAi.WebApi.ExampleProviders.User;

/// <summary>
/// Provides an example payload for the change password request.
/// </summary>
public class ChangePasswordRequestExample : IExamplesProvider<ChangePasswordRequest>
{
    /// <inheritdoc />
    public ChangePasswordRequest GetExamples()
    {
        return new ChangePasswordRequest
        {
            CurrentPassword = "OldPassword123!",
            NewPassword = "NewSecurePassword456!"
        };
    }
}

