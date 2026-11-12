using IniaAi.Application.Common.Capabilities;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.WebApi.Filters;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace IniaAi.WebApi.Attributes;

/// <summary>
/// The endpoint answers 403 to an organization without this capability
/// (<see cref="OrganizationCapabilities"/>). Only a gated endpoint gets a <see cref="CapabilityGateFilter"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequiresCapabilityAttribute(string capability) : Attribute, IFilterFactory
{
    /// <summary>The capability the organization must have.</summary>
    public string Capability { get; } = capability;

    /// <inheritdoc />
    public bool IsReusable => false;

    /// <inheritdoc />
    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
    {
        return new CapabilityGateFilter(
            Capability,
            serviceProvider.GetRequiredService<IOrganizationCapabilities>(),
            serviceProvider.GetRequiredService<IRequestContext>(),
            serviceProvider.GetRequiredService<ProblemDetailsFactory>(),
            serviceProvider.GetRequiredService<ILogger<CapabilityGateFilter>>());
    }
}
