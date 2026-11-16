using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Common.Interfaces.Services.Runs;

/// <summary>
/// Which web addresses a run's Web API calls may reach, beyond the internal-address rule that every
/// call meets. Asked once when the run starts.
/// </summary>
public interface IWebCallAddresses
{
    /// <summary>
    /// Each scheme, host and port, such as <c>https://api.example.com</c>, that the run's requests may
    /// go to, every redirect included, or null when any public address may be reached.
    /// </summary>
    Task<IReadOnlySet<string>?> AllowedAsync(string organizationId, Workflow workflow, CancellationToken cancellationToken);
}
