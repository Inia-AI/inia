using System.Text.Json;

namespace IniaAi.WebApi.Contracts.Requests.WorkflowExecutions;

/// <summary>
/// JSON metadata for the submit workflow execution endpoint.
/// Sent as the "metadata" part of a multipart/form-data request.
/// </summary>
public sealed class SubmitWorkflowExecutionRequest
{
    /// <summary>
    /// Gets or sets the workflow type identifier to execute (e.g., "CorporatesCreditReport").
    /// </summary>
    public required string WorkflowTypeId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the counterparty involved in the workflow.
    /// </summary>
    public string? CounterpartyId { get; set; }

    /// <summary>
    /// Gets or sets the identifiers of the peer companies associated with the workflow.
    /// Maximum of 3 peer companies allowed.
    /// </summary>
    public List<string>? PeerCompanyIds { get; set; }

    /// <summary>
    /// Gets or sets per-file metadata, matched to binary file parts by array index.
    /// If null or fewer entries than files, unmatched files have no document class.
    /// </summary>
    public List<SubmitFileMetadata>? Files { get; set; }

    /// <summary>
    /// Gets or sets workflow variable overrides.
    /// Only variables that differ from the workflow defaults need to be supplied.
    /// The key is the variable's ReplacementTag.
    /// </summary>
    public Dictionary<string, JsonElement>? Variables { get; set; }

    /// <summary>
    /// The freeze option: the identifier of a completed execution whose external-fetch outputs
    /// (web search, exchange rate, external API blocks) this execution replays instead of fetching
    /// live. The submission must match that execution on workflow type, counterparty and variable
    /// values, or the start is refused. Mutually exclusive with <see cref="FreezeSourceCaseId"/>.
    /// </summary>
    public string? FreezeSourceExecutionId { get; set; }

    /// <summary>
    /// The freeze option served through a validation case: a case imported from another
    /// deployment replays its stored captures, a native case replays its baseline execution.
    /// The submission must match the case's re-run recipe on workflow type, counterparty and
    /// variable values, or the start is refused. Mutually exclusive with
    /// <see cref="FreezeSourceExecutionId"/>.
    /// </summary>
    public string? FreezeSourceCaseId { get; set; }
}
