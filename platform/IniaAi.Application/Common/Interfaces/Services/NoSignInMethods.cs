using IniaAi.Domain.Entities.UserManagement;

namespace IniaAi.Application.Common.Interfaces.Services;

public sealed class NoSignInMethods : ISignInMethods
{
    public Task SigningInAsync(User user, string method, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
