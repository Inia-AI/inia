using IniaAi.Domain.Entities.Testing;

namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// The starred baselines as the validation module reads them, for the open reads that show them:
/// a report's star and the baselines a shared-component change affects. A build without the
/// module reports neither (<see cref="Common.Services.NoBaselineReader"/>): a starred report
/// answers no verification, and a component change lists no affected baseline.
/// </summary>
public interface IBaselineReader
{
    /// <summary>
    /// Whether each starred execution's baseline is verified, by execution id. An execution the
    /// reader does not judge is absent, and its star answers no verification.
    /// </summary>
    Task<IReadOnlyDictionary<string, bool>> VerifiedAsync(
        string organizationId,
        IReadOnlyCollection<string> executionIds,
        CancellationToken cancellationToken);

    /// <summary>The active baseline registrations of the given workflow types.</summary>
    Task<IReadOnlyList<WorkflowBaselineRegistration>> ActiveRegistrationsAsync(
        IReadOnlyCollection<string> workflowTypeIds,
        CancellationToken cancellationToken);
}
