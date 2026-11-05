namespace IniaAi.WebApi.Contracts.Responses.Signatures;

/// <summary>
/// Response shape for the signature-test-replay block/field picker. Lists every AI Query block
/// in a persisted execution snapshot with its captured output and the resolved signature-field
/// values used at execution time. The user/system prompts are deliberately omitted — those are
/// Inia IP and never leave the backend.
/// </summary>
public sealed record ExecutionBlocksDto(
    string WorkflowExecutionId,
    string? WorkflowTypeId,
    IReadOnlyList<ExecutionBlockDto> Blocks);

/// <summary>
/// A single AI Query block inside an execution snapshot. <see cref="BlockId"/> is the
/// execution-clone Id (stable within the snapshot) — the FE round-trips it back for replay.
/// </summary>
public sealed record ExecutionBlockDto(
    string BlockId,
    string BlockName,
    string? SectionTitle,
    string? ReplacementTag,
    string? RawResponse,
    IReadOnlyList<ReplayableSignatureFieldDto> SignatureFields);

/// <summary>
/// A signature field that the user may replay against this block. <see cref="CapturedResolvedValue"/>
/// is the exact string that fed the prompt template at execution time; it's null for pre-capture
/// runs (executions that predate the capture code deployment), in which case <see cref="CanReplay"/>
/// is false and the FE disables the Run button for this field.
/// </summary>
public sealed record ReplayableSignatureFieldDto(
    string PlaceholderName,
    string DisplayName,
    string? Description,
    string DefaultValue,
    string? CapturedResolvedValue,
    bool CanReplay);
