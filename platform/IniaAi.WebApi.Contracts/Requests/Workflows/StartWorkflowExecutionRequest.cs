using System.Text.Json;

namespace IniaAi.WebApi.Contracts.Requests;

/// <summary>
/// Represents a request to initiate a workflow execution.
/// </summary>
public sealed class StartWorkflowExecutionRequest
{
    /// <summary>
    /// Gets or sets the unique identifier of the workflow definition to execute.
    /// </summary>
    public required string WorkflowId { get; set; }

    /// <summary>
    /// Gets or sets the list of file identifiers associated with the workflow with classification, and associated tags.
    /// </summary>
    public List<PostDocumentInfoRequest>? Files { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the counterparty involved in the workflow.
    /// </summary>
    public string? CounterpartyId { get; set; }

    /// <summary>
    /// Gets or sets the identifiers of the peer companies associated with the workflow.
    /// </summary>
    public List<string>? PeerCompanyIds { get; set; }

    /// <summary>
    /// Gets or sets the workflow variable values.
    /// The key is the variable's ReplacementTag and the value can be:
    /// - String: for String, Number, DateTime types
    /// - Array: for List, Dropdown, MultiSelect types
    /// - Number: for Number type
    /// - Boolean: for boolean values
    /// </summary>
    public Dictionary<string, JsonElement>? Variables { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the workflow execution is marked as confidential.
    /// </summary>
    public bool IsConfidential { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether attachments are extracted from every email
    /// document of this run (each becomes its own document) in addition to rendering the email
    /// bodies to PDF. When false, email bodies are still rendered to PDF but attachments are
    /// left inside the stored email. Extraction happens inside the execution, not in this request.
    /// </summary>
    public bool ExpandEmails { get; set; }

    /// <summary>
    /// Gets or sets the document class assigned to every file extracted from the run's emails
    /// (attachments, archive entries, downloads). Only meaningful with
    /// <see cref="ExpandEmails"/>; the body PDF inherits the email document's own class instead.
    /// </summary>
    public string? AttachmentsDocumentClass { get; set; }

    /// <summary>
    /// Gets or sets the freeze option: the identifier of a completed execution whose external-fetch
    /// outputs (web search, exchange rate, external API blocks) this execution replays instead of
    /// fetching live. The submission must match that execution on workflow type, counterparty and
    /// variable values, or the start is refused. The resulting execution is a test artifact marked
    /// with replay provenance and coverage; it is never a normal report.
    /// </summary>
    public string? FreezeSourceExecutionId { get; set; }
}
