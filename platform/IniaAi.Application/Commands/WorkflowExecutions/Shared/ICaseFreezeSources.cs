using ErrorOr;
using IniaAi.Domain.Entities.Workflows;
using System.Text.Json;

namespace IniaAi.Application.Commands.WorkflowExecutions.Shared;

/// <summary>
/// Serves <see cref="FreezeSource.Case"/>. The validation module serves it; a build without the
/// module refuses it before anything is stored or started (<see cref="NoCaseFreezeSources"/>).
/// </summary>
public interface ICaseFreezeSources
{
    /// <summary>Success when this build serves a case freeze; otherwise why it refuses one.</summary>
    ErrorOr<Success> Serves();

    /// <summary>Resolves the case into what the engine replays.</summary>
    Task<ErrorOr<ResolvedFreeze>> ResolveAsync(
        FreezeSource.Case source,
        string organizationId,
        Workflow targetWorkflow,
        string? targetCounterpartyId,
        Dictionary<string, JsonElement>? targetVariables,
        CancellationToken cancellationToken);
}
