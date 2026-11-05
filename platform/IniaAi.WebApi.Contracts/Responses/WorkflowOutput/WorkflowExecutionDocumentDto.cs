namespace IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

/// <summary>
/// Represents document information used in a workflow execution,
/// including execution-specific settings and basic document metadata.
/// </summary>
public record WorkflowExecutionDocumentDto
{
    /// <summary>
    /// Gets the unique identifier of the document.
    /// </summary>
    public string FileId { get; init; } = default!;

    /// <summary>
    /// Gets the name of the document file.
    /// </summary>
    public string Name { get; init; } = default!;

    /// <summary>
    /// Gets the file extension (e.g., ".pdf", ".docx").
    /// </summary>
    public string Extension { get; init; } = default!;

    /// <summary>
    /// Gets the size of the document in bytes.
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// Gets the document class selected for this execution.
    /// </summary>
    public string DocumentClass { get; init; } = default!;

    /// <summary>
    /// Human-readable display name of the document class, resolved from the workflow snapshot
    /// at the time of the execution. Falls back to <see cref="DocumentClass"/> when the snapshot
    /// is missing or the class has no display name.
    /// </summary>
    public string? DocumentClassDisplayName { get; init; }

    /// <summary>
    /// Gets the document tags applied for this execution.
    /// </summary>
    public List<string> DocumentTags { get; init; } = [];

    /// <summary>
    /// Gets whether OCR was enabled for this document in this execution.
    /// </summary>
    public bool ShouldUseOcr { get; init; }

    /// <summary>
    /// Gets the sources associated with the document.
    /// </summary>
    public List<string> Sources { get; init; } = [];

    /// <summary>
    /// Gets whether this document should be displayed in the final output.
    /// </summary>
    public bool ShouldBeDisplayedInOutput { get; init; } = true;

    /// <summary>
    /// Gets whether this document was produced by the workflow (output) rather than provided as input.
    /// </summary>
    public bool IsOutputDocument { get; init; }

    /// <summary>
    /// Gets when the run took this document out of its input before processing it; null for a
    /// document the run used.
    /// </summary>
    public DateTime? ExcludedAt { get; init; }

    /// <summary>
    /// Gets the stable code that says why the run took this document out of its input, such as
    /// <c>Unreadable</c>, <c>NotExpectedDocument</c> or <c>NotNeeded</c>; null for a document the
    /// run used.
    /// </summary>
    public string? ExclusionReason { get; init; }
}
