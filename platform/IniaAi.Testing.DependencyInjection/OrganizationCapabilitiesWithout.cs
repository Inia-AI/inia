using IniaAi.Application.Common.Capabilities;

namespace IniaAi.Testing.DependencyInjection;

/// <summary>Every capability but one, as a closed module that takes one away would answer.</summary>
public sealed class OrganizationCapabilitiesWithout(string removed) : IOrganizationCapabilities
{
    private readonly IReadOnlySet<string> _granted = OrganizationCapabilities.All.Where(capability => capability != removed).ToHashSet();

    public ValueTask<IReadOnlySet<string>> GetAsync(string organizationId, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(_granted);
    }
}
