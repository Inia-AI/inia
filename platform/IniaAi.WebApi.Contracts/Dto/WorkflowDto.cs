using IniaAi.WebApi.Contracts.Dto;

namespace IniaAi.WebApi.Models.Dto;

/// <summary>
/// Represents a workflow configuration within the Inia AI Web API.
/// </summary>
public sealed class WorkflowDto
{
    /// <summary>
    /// Gets or sets the unique identifier of the workflow.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the workflow type.
    /// </summary>
    public required string WorkflowTypeId { get; set; }

    /// <summary>
    /// Gets or sets the name of the workflow type.
    /// </summary>
    public string? WorkflowTypeName { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the workflow is available.
    /// </summary>
    public bool IsAvailable { get; set; } = true;

    /// <summary>
    /// Gets or sets the list of document class keys associated with the workflow.
    /// </summary>
    public List<string> DocumentClassKeys { get; set; } = [];

    /// <summary>
    /// Gets or sets the list of variables associated with the workflow.
    /// </summary>
    public List<WorkflowVariableDto> Variables { get; set; } = [];

    /// <summary>
    /// Gets or sets the list of document classes for the workflow.
    /// </summary>
    public List<DocumentClassDto> DocumentClasses { get; set; } = [];

    /// <summary>
    /// Gets or sets the list of counterparty type mappings for the workflow.
    /// </summary>
    public List<string> CounterpartyTypeMappings { get; set; } = [];

    /// <summary>
    /// Indicates whether counterparty selection is mandatory for this workflow.
    /// True when any block in the workflow uses a Counterparty placeholder.
    /// Only populated by <c>GET /v1/workflow-templates/{workflowTypeId}/summary</c>; category
    /// listings do not load blocks and report false regardless.
    /// </summary>
    public bool IsCounterpartyRequired { get; set; }

    /// <summary>
    /// Indicates whether this workflow can consume peer companies, and therefore whether the
    /// peer picker should be offered. True when any block is a Peer Financials block or uses a
    /// PeerCompanies placeholder — either route feeds the selected peers into the run.
    /// Selection stays optional; this only decides whether it is offered.
    /// Only populated by <c>GET /v1/workflow-templates/{workflowTypeId}/summary</c>; category
    /// listings do not load blocks and report false regardless.
    /// </summary>
    public bool SupportsPeerCompanies { get; set; }

    /// <summary>
    /// Indicates whether the platform reads each document a run is sent and decides its class.
    /// When true, choosing a class at submit is optional and pins the document to that class; a
    /// document sent without one is classified by the platform, and the required classes are
    /// checked once the run has read the documents rather than before it starts.
    /// </summary>
    public bool ClassifiesDocuments { get; set; }

    /// <summary>
    /// On a workflow that classifies documents, the keys of the required classes the platform
    /// cannot recognise on its own, because they have no description. A run must be sent a document
    /// chosen as each of them, and a submit without one is refused before the run starts. Empty
    /// when <see cref="ClassifiesDocuments"/> is false, since then every required class must be
    /// chosen.
    /// </summary>
    public List<string> ClassesToChoose { get; set; } = [];

    /// <summary>
    /// The credits a run of this workflow type costs; null when a run of it costs none, which is
    /// always the case where the deployment charges no credits.
    /// </summary>
    public int? CreditCost { get; set; }
}
