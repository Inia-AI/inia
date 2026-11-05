using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Responses.SharedComponents;

/// <summary>
/// The result of sweeping every workflow template for content that repeats verbatim without being
/// in the Library yet.
/// </summary>
/// <param name="Suggestions">
/// One entry per repeated content hash, ordered by how many places it occurs in, descending.
/// </param>
/// <param name="ScannedWorkflowTypeCount">How many workflow types were read to produce this result.</param>
/// <param name="ElapsedMilliseconds">How long the scan took, so callers can see what it cost.</param>
public record SharedComponentSuggestionsResponse(
    IReadOnlyList<SharedComponentSuggestionDto> Suggestions,
    int ScannedWorkflowTypeCount,
    long ElapsedMilliseconds);

/// <summary>
/// One group of template nodes that share identical normalized content.
/// </summary>
/// <param name="Kind">Whether the repeated content is a block or a whole section.</param>
/// <param name="ContentHash">The normalized content hash every node in the group shares.</param>
/// <param name="SuggestedName">A display name taken from the first named node, offered as a default.</param>
/// <param name="CrossType">Whether the group spans more than one workflow type.</param>
/// <param name="ExistingComponentId">
/// The active component whose head already matches this content, when one exists. Present means
/// attach every node to it; absent means promote one node and attach the rest.
/// </param>
/// <param name="ExistingComponentName">The display name of <paramref name="ExistingComponentId"/>, when set.</param>
/// <param name="Nodes">Every place this content occurs, ordered by workflow type then path.</param>
public record SharedComponentSuggestionDto(
    SharedComponentKind Kind,
    string ContentHash,
    string SuggestedName,
    bool CrossType,
    string? ExistingComponentId,
    string? ExistingComponentName,
    IReadOnlyList<SharedComponentSuggestionNodeDto> Nodes);

/// <summary>
/// One template node carrying the repeated content, addressed the way promote expects.
/// </summary>
/// <param name="WorkflowTypeId">The workflow type holding this node.</param>
/// <param name="WorkflowTypeName">That workflow type's display name.</param>
/// <param name="TargetPath">The positional path to the node, for templates without stable ids.</param>
/// <param name="TargetStableId">
/// The node's stable id when it has one. Prefer it over <paramref name="TargetPath"/> — a path
/// shifts as soon as anything before it is inserted or removed.
/// </param>
/// <param name="ProvenanceComponentId">
/// The component this node was broken out of, when it carries that provenance. A broken-out copy no
/// longer tracks its component, so it is a legitimate candidate again.
/// </param>
/// <param name="ProvenanceComponentName">The display name of <paramref name="ProvenanceComponentId"/>, when set.</param>
/// <param name="SectionTitle">The title of the section holding the node, or of the node itself for a section.</param>
/// <param name="SectionOrder">That section's order within the template.</param>
/// <param name="BlockName">The block's name, or null when the node is a section.</param>
/// <param name="BlockOrder">The block's order within its section, or null when the node is a section.</param>
/// <param name="WorkflowTypeCategoryName">The category the workflow type files under, when set.</param>
public record SharedComponentSuggestionNodeDto(
    string WorkflowTypeId,
    string WorkflowTypeName,
    string TargetPath,
    string? TargetStableId,
    string? ProvenanceComponentId,
    string? ProvenanceComponentName,
    string SectionTitle,
    int SectionOrder,
    string? BlockName,
    int? BlockOrder,
    string? WorkflowTypeCategoryName);
