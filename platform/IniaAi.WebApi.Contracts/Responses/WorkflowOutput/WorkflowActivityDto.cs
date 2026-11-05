namespace IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

/// <summary>
/// The plain-language activity of a running classic workflow: the ordered step lines of its
/// version's cached activity script and which of them is current. Deliberately unable to carry
/// engine vocabulary — no block or section names, ids, types, prompts, or counts of internal
/// steps ever ride this shape.
/// </summary>
public record WorkflowActivityDto
{
    /// <summary>
    /// The wire schema version, so a client can refuse a shape it does not know.
    /// </summary>
    public int SchemaVersion { get; init; }

    /// <summary>
    /// Index into <see cref="Steps"/> of the step the run is working through now.
    /// </summary>
    public int CurrentStepIndex { get; init; }

    /// <summary>
    /// The run's steps in order, one plain-language line each.
    /// </summary>
    public List<WorkflowActivityStepDto> Steps { get; init; } = [];
}

/// <summary>
/// One step of a classic workflow's activity script.
/// </summary>
public record WorkflowActivityStepDto
{
    /// <summary>
    /// What the system is doing while this step runs, in plain language.
    /// </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// The step's place in the run: <see cref="WorkflowActivityStepStatus.DONE"/> when its work
    /// completed, <see cref="WorkflowActivityStepStatus.ACTIVE"/> while it is provably underway
    /// (several at once under parallel execution), <see cref="WorkflowActivityStepStatus.PENDING"/>
    /// otherwise.
    /// </summary>
    public string Status { get; init; } = WorkflowActivityStepStatus.PENDING;
}

/// <summary>
/// The closed status vocabulary of <see cref="WorkflowActivityStepDto.Status"/>.
/// </summary>
public static class WorkflowActivityStepStatus
{
    /// <summary>The step's work completed.</summary>
    public const string DONE = "Done";

    /// <summary>The step is provably underway and unfinished.</summary>
    public const string ACTIVE = "Active";

    /// <summary>The step has not started as far as the run can prove.</summary>
    public const string PENDING = "Pending";
}
