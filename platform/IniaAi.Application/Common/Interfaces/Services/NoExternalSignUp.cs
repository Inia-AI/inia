using IniaAi.Domain.Entities.UserManagement;

namespace IniaAi.Application.Common.Interfaces.Services;

public sealed class NoExternalSignUp : IExternalSignUp
{
    public Task<ExternalSignUpResult> SignUpAsync(string provider, string email, string? clientAddress, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ExternalSignUpResult.None);
    }
}
