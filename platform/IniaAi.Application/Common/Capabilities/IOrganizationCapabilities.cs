namespace IniaAi.Application.Common.Capabilities;

/// <summary>
/// What an organization may do, as <see cref="OrganizationCapabilities"/> names. The platform grants
/// every one in <see cref="OrganizationCapabilities.All"/>; a closed module may take some away, and add
/// one the platform grants to no one.
/// </summary>
public interface IOrganizationCapabilities
{
    ValueTask<IReadOnlySet<string>> GetAsync(string organizationId, CancellationToken cancellationToken);

    /// <summary>
    /// Why the organization lacks the capability, in words for the person refused, with the next
    /// step; null when the refusal's general text fits.
    /// </summary>
    ValueTask<string?> RefusalAsync(string organizationId, string capability, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<string?>(null);
    }
}
