using System.Text.Json;
using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Responses.WorkflowExecutions;

/// <summary>
/// The inputs a re-run of an execution would submit, checked against the workflow as it is now.
/// The workflow can change after a run: it can gain a required variable, drop a variable, or need
/// a document class the run did not have. This tells the caller whether the original inputs still
/// start a run, and what the user must fill in when they do not.
/// </summary>
public record ReRunInputsDto
{
    /// <summary>
    /// The id of the workflow's current template. Submit the re-run with this id.
    /// </summary>
    public string WorkflowId { get; init; } = default!;

    /// <summary>
    /// The workflow type of the execution and of its current template.
    /// </summary>
    public string WorkflowTypeId { get; init; } = default!;

    /// <summary>
    /// True when the inputs in <see cref="Variables"/> and the run's input documents start a run
    /// as they are. False when <see cref="VariablesNeedingInput"/>,
    /// <see cref="DocumentClassesNeedingInput"/> or <see cref="UnavailableDocuments"/> lists
    /// something the user must deal with first.
    /// </summary>
    public bool IsReady { get; init; }

    /// <summary>
    /// The variable values to submit, keyed by the current template's replacement tags: the run's
    /// values for variables the template still has, and the template's default for a variable the
    /// run had no value for. A variable listed in <see cref="VariablesNeedingInput"/> is absent.
    /// Confidential values are never returned: a confidential variable the run had a value for is
    /// listed in <see cref="VariablesNeedingInput"/>, required or not.
    /// </summary>
    public List<ReRunVariableValueDto> Variables { get; init; } = [];

    /// <summary>
    /// Variables the user must fill in before the re-run can start.
    /// </summary>
    public List<ReRunVariableNeedingInputDto> VariablesNeedingInput { get; init; } = [];

    /// <summary>
    /// Variables the run had no value for that take the template's default.
    /// </summary>
    public List<ReRunVariableChangeDto> DefaultedVariables { get; init; } = [];

    /// <summary>
    /// Variables the run had a value for that the workflow no longer has. Their values are dropped.
    /// </summary>
    public List<ReRunVariableChangeDto> RemovedVariables { get; init; } = [];

    /// <summary>
    /// Document classes the run's input documents no longer satisfy, for example a required class
    /// added since the run.
    /// </summary>
    public List<ReRunDocumentClassNeedingInputDto> DocumentClassesNeedingInput { get; init; } = [];

    /// <summary>
    /// The run's input documents that no longer exist, for example a document deleted after the
    /// run. A re-run cannot send them, and they do not count toward
    /// <see cref="DocumentClassesNeedingInput"/>.
    /// </summary>
    public List<ReRunUnavailableDocumentDto> UnavailableDocuments { get; init; } = [];
}

/// <summary>
/// An input document of the run that a re-run can no longer send.
/// </summary>
public record ReRunUnavailableDocumentDto
{
    /// <summary>
    /// The document's id.
    /// </summary>
    public string DocumentId { get; init; } = default!;

    /// <summary>
    /// The document's file name with its extension; null when the document row is gone.
    /// </summary>
    public string? FileName { get; init; }
}

/// <summary>
/// One variable value of a re-run.
/// </summary>
public record ReRunVariableValueDto
{
    /// <summary>
    /// The variable's replacement tag in the current template.
    /// </summary>
    public string ReplacementTag { get; init; } = default!;

    /// <summary>
    /// The value in the shape a submission takes it: a string, a number, or an array of strings.
    /// </summary>
    public JsonElement Value { get; init; }
}

/// <summary>
/// A variable the user must fill in before a re-run can start.
/// </summary>
public record ReRunVariableNeedingInputDto
{
    /// <summary>
    /// The variable's replacement tag in the current template.
    /// </summary>
    public string ReplacementTag { get; init; } = default!;

    /// <summary>
    /// The variable's display name in the current template.
    /// </summary>
    public string DisplayName { get; init; } = default!;

    /// <summary>
    /// Why the variable needs input.
    /// </summary>
    public ReRunInputReason Reason { get; init; }

    /// <summary>
    /// The validation message a submission without a usable value gets.
    /// </summary>
    public string Message { get; init; } = default!;
}

/// <summary>
/// A variable whose value a re-run changes from the original run.
/// </summary>
public record ReRunVariableChangeDto
{
    /// <summary>
    /// The variable's replacement tag.
    /// </summary>
    public string ReplacementTag { get; init; } = default!;

    /// <summary>
    /// The variable's display name.
    /// </summary>
    public string DisplayName { get; init; } = default!;
}

/// <summary>
/// A document class the run's input documents no longer satisfy.
/// </summary>
public record ReRunDocumentClassNeedingInputDto
{
    /// <summary>
    /// The document class key in the current template.
    /// </summary>
    public string Key { get; init; } = default!;

    /// <summary>
    /// The document class display name in the current template.
    /// </summary>
    public string DisplayName { get; init; } = default!;

    /// <summary>
    /// The validation message a submission with these documents gets.
    /// </summary>
    public string Message { get; init; } = default!;
}
