namespace IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

/// <summary>
/// A workflow that a finished run leads to: one of its inputs is made by the run's workflow. It names the workflow to
/// start and the documents of the run to attach to it by id.
/// </summary>
public sealed record NextWorkflowDto
{
    /// <summary>Gets the type's current workflow, which a new run of the type names.</summary>
    public string WorkflowId { get; init; } = default!;

    /// <summary>Gets the identifier of the workflow type. It stays the same when the type is renamed.</summary>
    public string WorkflowTypeId { get; init; } = default!;

    /// <summary>Gets the name of the workflow type.</summary>
    public string WorkflowTypeName { get; init; } = default!;

    /// <summary>
    /// Gets the credits a run of the type costs; null when a run of it costs none, which is always the case where the
    /// deployment charges no credits.
    /// </summary>
    public int? CreditCost { get; init; }

    /// <summary>
    /// Gets the documents of the run to attach: the outputs that the next workflow's inputs name as made by this run's
    /// workflow, then the run's inputs whose class the next workflow also has. Each carries the class of the next
    /// workflow it goes to.
    /// </summary>
    public List<NextWorkflowDocumentDto> Documents { get; init; } = [];
}

/// <summary>A document of a finished run that a next workflow takes, with the class of that workflow it goes to.</summary>
public sealed record NextWorkflowDocumentDto
{
    /// <summary>Gets the unique identifier of the document.</summary>
    public string FileId { get; init; } = default!;

    /// <summary>Gets the name of the document file.</summary>
    public string Name { get; init; } = default!;

    /// <summary>Gets the file extension (e.g., ".xlsm", ".pdf").</summary>
    public string Extension { get; init; } = default!;

    /// <summary>Gets the document class of the next workflow that the document goes to.</summary>
    public string DocumentClass { get; init; } = default!;
}
