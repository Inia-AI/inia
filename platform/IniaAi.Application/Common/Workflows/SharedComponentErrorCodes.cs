using ErrorOr;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;

namespace IniaAi.Application.Common.Workflows;

/// <summary>
/// Error-code family for the shared-components surface. Validation-channel errors embed the code in
/// the message (the JSON path is the ValidationProblemDetails key and that channel carries no
/// metadata extensions); conflict/not-found-channel errors use these as the ErrorOr code directly.
/// Channel rules: <see cref="TARGET_NOT_FOUND"/> is 404-only (the locator was well-formed but
/// addressed nothing), while <see cref="INVALID_TARGET_LOCATOR"/> is 400-only (the locator itself
/// is malformed or ambiguous — fix the request, don't re-read the template).
/// <see cref="PAYLOAD_ABSENT"/> is 404 on reads (the content sub-resource does not exist) and 409
/// on write preconditions (the operation conflicts with the component's stored state).
/// </summary>
public static class SharedComponentErrorCodes
{
    public const string INVALID_LINK_SHAPE = "SharedComponent.InvalidLinkShape";
    public const string UNKNOWN_COMPONENT = "SharedComponent.UnknownComponent";
    public const string KIND_MISMATCH = "SharedComponent.KindMismatch";
    public const string ALREADY_SHARED = "SharedComponent.AlreadyShared";
    public const string RETIRED = "SharedComponent.Retired";
    public const string CONTENT_MISMATCH = "SharedComponent.ContentMismatch";
    public const string PAYLOAD_ABSENT = "SharedComponent.PayloadAbsent";
    public const string TARGET_NOT_FOUND = "SharedComponent.TargetNotFound";
    public const string INVALID_TARGET_LOCATOR = "SharedComponent.InvalidTargetLocator";
    public const string CORRUPT_DEFINITION = "SharedComponent.CorruptDefinition";
    public const string NO_VERSIONS = "SharedComponent.NoVersions";
    public const string UNKNOWN_VERSION = "SharedComponent.UnknownVersion";
    public const string HEAD_MOVED = "SharedComponent.HeadMoved";
    public const string CONCURRENT_APPEND = "SharedComponent.ConcurrentAppend";
    public const string CONFLICTING_DECISIONS = "SharedComponent.ConflictingDecisions";
    public const string ORPHANS_NEED_ACKNOWLEDGEMENT = "SharedComponent.OrphansNeedAcknowledgement";
    public const string OVERWRITES_LOCAL_ENTRIES = "SharedComponent.OverwritesLocalEntries";
    public const string UNKNOWN_CATEGORY = "SharedComponent.UnknownCategory";
    public const string CATEGORY_IN_USE = "SharedComponent.CategoryInUse";
    public const string DIVERGED_LINEAGE = "SharedComponent.DivergedLineage";
    public const string BUNDLE_FORMAT_UNSUPPORTED = "SharedComponent.BundleFormatUnsupported";
    public const string BUNDLE_TYPE_NAME_MISSING = "SharedComponent.BundleTypeNameMissing";
    public const string AGENT_RUNTIME_DISABLED = "SharedComponent.AgentRuntimeDisabled";
    public const string AGENT_EXPANSION_SCOPE = "SharedComponent.AgentExpansionScope";
    public const string AGENT_BLOCK_INVALID = "SharedComponent.AgentBlockInvalid";

    public static Error UnknownComponent(string sharedComponentId)
    {
        return Error.NotFound(
            UNKNOWN_COMPONENT,
            $"No shared component with id '{sharedComponentId}' exists.");
    }

    public static Error AgentViolation(AgentBlockGuard.AgentBlockViolation violation)
    {
        string code = violation.Kind switch
        {
            AgentBlockGuard.AgentViolationKind.RuntimeDisabled => AGENT_RUNTIME_DISABLED,
            AgentBlockGuard.AgentViolationKind.LoopScope => AGENT_EXPANSION_SCOPE,
            _ => AGENT_BLOCK_INVALID,
        };

        return Error.Validation(code, violation.Message);
    }

    public static bool IsStaleHeadToken(Error error)
    {
        return error.Code is HEAD_MOVED or CONCURRENT_APPEND
            || SaveWorkflowCommandHandler.IsVersionConflict(error);
    }
}
