using ErrorOr;
using IniaAi.Domain.Entities.Workflows;
using System.Text.Json;

namespace IniaAi.Application.Commands.WorkflowExecutions.Shared;

/// <summary>
/// The case freeze option in a build without the validation module: the request contract keeps
/// the field, so a submission naming a case is refused rather than run without its replay.
/// </summary>
public sealed class NoCaseFreezeSources : ICaseFreezeSources
{
    public const string NOT_IN_BUILD_CODE = "WorkflowExecution.FreezeSourceCaseNotInBuild";

    private static readonly Error _notInBuild = Error.Validation(NOT_IN_BUILD_CODE, "Replay from a validation case is not part of this build.");

    public ErrorOr<Success> Serves()
    {
        return _notInBuild;
    }

    public Task<ErrorOr<ResolvedFreeze>> ResolveAsync(
        FreezeSource.Case source,
        string organizationId,
        Workflow targetWorkflow,
        string? targetCounterpartyId,
        Dictionary<string, JsonElement>? targetVariables,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<ErrorOr<ResolvedFreeze>>(_notInBuild);
    }
}
