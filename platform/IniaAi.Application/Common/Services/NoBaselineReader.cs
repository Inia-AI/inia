using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.Testing;
using System.Collections.ObjectModel;

namespace IniaAi.Application.Common.Services;

/// <summary>
/// The baseline reads in a build without the validation module: no star is verified and no
/// component change affects a baseline, whatever the kept tables hold.
/// </summary>
public sealed class NoBaselineReader : IBaselineReader
{
    public Task<IReadOnlyDictionary<string, bool>> VerifiedAsync(
        string organizationId,
        IReadOnlyCollection<string> executionIds,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyDictionary<string, bool>>(ReadOnlyDictionary<string, bool>.Empty);
    }

    public Task<IReadOnlyList<WorkflowBaselineRegistration>> ActiveRegistrationsAsync(
        IReadOnlyCollection<string> workflowTypeIds,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<WorkflowBaselineRegistration>>([]);
    }
}
