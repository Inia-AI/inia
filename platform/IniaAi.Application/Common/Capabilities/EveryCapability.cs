namespace IniaAi.Application.Common.Capabilities;

public sealed class EveryCapability : IOrganizationCapabilities
{
    public ValueTask<IReadOnlySet<string>> GetAsync(string organizationId, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<IReadOnlySet<string>>(OrganizationCapabilities.All);
    }
}
