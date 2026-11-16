using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Common.Interfaces.Services.Runs;

public sealed class AnyPublicWebAddress : IWebCallAddresses
{
    public Task<IReadOnlySet<string>?> AllowedAsync(string organizationId, Workflow workflow, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlySet<string>?>(null);
    }
}
