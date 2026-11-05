using IniaAi.Domain.Entities.UserManagement;

namespace IniaAi.Application.Common.Interfaces.Services;

public sealed class NoSessionLimit : ISessionLifetimePolicy
{
    public ValueTask<SessionLifetime> GetAsync(User user, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(SessionLifetime.Unlimited);
    }
}
