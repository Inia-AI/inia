using System.Collections.Concurrent;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Replay;
using IniaAi.Services.Models.BackgroundJobs;
using IniaAi.Services.Models.BackgroundJobs.Enums;
using IniaAi.Services.Models.Jobs.Enums;

namespace IniaAi.Services.Models.Jobs;

public partial class Job : BackgroundJob
{
    public override BackgroundJobType Type => BackgroundJobType.WorkflowExecution;

    //private readonly IServiceProvider _serviceProvider;

    public string? CounterpartyId { get; set; }

    public List<string>? PeerCompanyIds { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Output { get; set; }

    public string? WorkflowExecutionId { get; set; }

    public List<string> DocumentInfos { get; set; } = [];

    public Dictionary<string, string> Inputs { get; set; } = [];

    public Dictionary<string, object> InputsObject { get; set; } = [];

    public ConcurrentDictionary<string, string> AnonymisationMap { get; private set; } = [];

    public List<string> AiAnswers { get; set; } = [];

    public bool IsConfidential { get; set; } = false;

    /// <summary>
    /// True when the sender answers for the run's documents, as a workflow engineer does when a
    /// report starts from the editor. A stage that checks the run input on the caller's behalf
    /// leaves such a run's documents as sent, and the bytes an email yields inside the run keep
    /// only the refusals that protect the platform.
    /// </summary>
    public bool InputIsVouchedFor { get; set; }

    /// <summary>
    /// The document each anonymised copy was made from, keyed by the copy's id. Anonymisation
    /// repoints the run's document rows at the copies, so a later stage reads this to name the
    /// document the caller actually sent.
    /// </summary>
    public Dictionary<string, string> AnonymisedFrom { get; } = [];

    public bool IsBusy => State is JobState.Preparing
        or JobState.Processing
        or JobState.Anonymizing;

    // public IWorkflowHandler WorkflowHandler { get; init; }

    public Workflow? Workflow { get; set; }

    /// <summary>
    /// Captured external-block outputs to replay (freeze) into this execution when it is a
    /// regression rerun. Null for a normal run. Carried on the job so it survives the in-memory
    /// queue handoff to the JobExecutor and on into the workflow engine.
    /// </summary>
    public IReadOnlyList<ReplayOutput>? ReplayOutputs { get; set; }

    /// <summary>
    /// What the engine does with a freeze-eligible block that <see cref="ReplayOutputs"/> does not
    /// cover. The dormant regression path keeps the default (skip, inconclusive); the freeze
    /// submission option runs it live and discloses coverage.
    /// </summary>
    public ReplayUncoveredBlockPolicy ReplayUncoveredBlockPolicy { get; set; }

    /// <summary>
    /// The job runs a validation or regression rerun: the job executor runs it inside
    /// <c>RerunWrites.Scope</c>, so it saves no change to live data. Held in memory only, since a
    /// workflow job is never rebuilt from the database.
    /// </summary>
    public bool IsRerun { get; set; }

    public Job(
        string organizationId,
        // IServiceProvider serviceProvider,
        string? workflowTypeId) : base(organizationId)
    {
        //_serviceProvider = serviceProvider;

        //WorkflowHandler = _serviceProvider.GetKeyedService<IWorkflowHandler>(workflowTypeId ?? WorkflowType.GENERIC);
        //WorkflowHandler = WorkflowHandler ?? _serviceProvider.GetRequiredKeyedService<IWorkflowHandler>(WorkflowType.GENERIC);
    }

    public override string ToString()
    {
        return $"{Name} ({State})";
    }

    /// <summary>
    /// Clears all work data associated with the job.
    /// </summary>
    public void ClearWorkData()
    {
        Workflow = null;
        Output = null;  // Consider adding
        AnonymisationMap?.Clear();
        DocumentInfos?.Clear();
        Inputs?.Clear();
        AiAnswers?.Clear();
        InputsObject?.Clear();
    }
}
