using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Responses.Signatures;

/// <summary>
/// Full signature surface for a single workflow. <see cref="Fields"/> carries each
/// workflow-level field exactly once (definition + current override state — the single
/// source of truth); <see cref="Sections"/> preserves the block structure for
/// navigation, with blocks referencing fields by name only. Scoped by the stable
/// <see cref="WorkflowTypeId"/>; the Core workflow GUID is deliberately not exposed
/// as it regenerates on every Save-As-Core and isn't meaningful to clients.
/// </summary>
public record WorkflowSignaturesDto(
    string WorkflowTypeId,
    string WorkflowName,
    int ReviewThreshold,
    IReadOnlyList<SignatureFieldDto> Fields,
    IReadOnlyList<SignatureSectionDto> Sections);

/// <summary>
/// A logical grouping of signature-bearing blocks (one per child workflow).
/// <see cref="SectionId"/> is the current-session Core GUID — valid for constructing
/// a subsequent <c>PUT</c> payload, but regenerated on Save-As-Core.
/// </summary>
public record SignatureSectionDto(
    string SectionId,
    string SectionTitle,
    int SectionOrder,
    IReadOnlyList<SignatureBlockDto> Blocks);

/// <summary>
/// A single block whose templates reference one or more workflow-level signature
/// fields, named in <see cref="FieldNames"/> (join against
/// <see cref="WorkflowSignaturesDto.Fields"/> by name, ordered by first appearance in
/// the block's templates). <see cref="BlockId"/> is the block's <c>StableBlockId</c> —
/// durable across saves, clones, and re-uploads — used for navigation only; write-side
/// endpoints key on the field name. <see cref="BlockType"/> is the
/// <c>nameof(BlockTypes.X)</c> string (e.g. <c>"AiQuery"</c>, <c>"Merge"</c>) so the FE
/// can branch on block kind, e.g. hiding the AI-prompt-replay Test button on Merge
/// blocks which have no prompt to replay.
/// </summary>
public record SignatureBlockDto(
    string BlockId,
    string BlockName,
    string BlockType,
    string ReplacementTag,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> FieldNames);

/// <summary>
/// A single workflow-level signature field, merged with any client override value.
/// <see cref="Name"/> is the durable identity every write-side endpoint keys on.
/// <see cref="UsedByBlockIds"/> lists the <c>StableBlockId</c> of every block whose
/// templates reference the field, in (section, block) order — one shared value applies
/// to all of them. When a <see cref="PendingSignatureOverrideDto"/> exists for the
/// field it is returned in <see cref="Pending"/>; otherwise that slot is
/// <see langword="null"/>.
/// </summary>
public record SignatureFieldDto(
    string Name,
    string DisplayName,
    string Description,
    string DefaultValue,
    string Value,
    bool IsOverridden,
    IReadOnlyList<string> UsedByBlockIds,
    PendingSignatureOverrideDto? Pending = null);

/// <summary>
/// A client-proposed override waiting for reviewer votes. Carried on the field
/// when one exists. <see cref="MyVote"/> reflects the requester's own vote, if any;
/// <see langword="null"/> means the requester has not voted. The proposer's
/// auto-seeded approve vote makes <see cref="MyVote"/> = <see cref="SignatureVoteKind.Approve"/>
/// for the proposer.
/// </summary>
public record PendingSignatureOverrideDto(
    string Id,
    string ProposerUserId,
    string? ProposedValue,
    DateTime CreatedAt,
    int ApproveCount,
    int RejectCount,
    SignatureVoteKind? MyVote,
    IReadOnlyList<VoteCommentDto> Comments,
    IReadOnlyList<SuggestionDto> Suggestions);

/// <summary>
/// A free-form comment left on a vote. Only votes whose <c>Comment</c> is non-null
/// surface here. Ordered by <see cref="CreatedAt"/> ascending.
/// </summary>
public record VoteCommentDto(
    string VoteId,
    string VoterUserId,
    SignatureVoteKind VoteKind,
    string Comment,
    DateTime CreatedAt);

/// <summary>
/// An alternative value suggested by a reviewer alongside their vote. Only votes
/// whose <c>SuggestedValue</c> is non-null surface here. Ordered by
/// <see cref="CreatedAt"/> ascending.
/// </summary>
public record SuggestionDto(
    string VoteId,
    string VoterUserId,
    string SuggestedValue,
    string? Comment,
    DateTime CreatedAt);
