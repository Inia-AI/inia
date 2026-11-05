using ErrorOr;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Application.Common.Workflows;

/// <summary>
/// Validates the structural integrity of a workflow template before it is persisted.
/// Hard failures (parse errors, null collections, ambiguous block references) become
/// validation errors; soft issues (cross-section tag reuse, unresolved placeholders)
/// become warnings that are returned to the caller without blocking the save.
/// </summary>
public interface IWorkflowTemplateStructuralValidator
{
    /// <summary>
    /// Parses the JSON payload and runs all structural checks.
    /// </summary>
    WorkflowStructuralValidationResult Validate(string workflowJson);

    /// <summary>
    /// Runs the structural checks against an already-deserialized workflow.
    /// </summary>
    WorkflowStructuralValidationResult Validate(Workflow workflow);
}

/// <summary>
/// A non-blocking structural issue found in a workflow template.
/// </summary>
/// <param name="Path">JSON-path-style location of the issue (e.g. <c>Sections[2].Blocks[1]</c>).</param>
/// <param name="Message">Human-readable description of the issue.</param>
public record WorkflowStructuralValidationWarning(string Path, string Message)
{
    /// <summary>A save's own warnings, which name their block in the message rather than by path.</summary>
    public static IEnumerable<WorkflowStructuralValidationWarning> FromSaveWarnings(IReadOnlyList<string>? warnings)
    {
        return (warnings ?? []).Select(static warning => new WorkflowStructuralValidationWarning(string.Empty, warning));
    }
}

/// <summary>
/// Outcome of a structural validation pass.
/// </summary>
/// <param name="Workflow">The deserialized workflow when parsing succeeded; otherwise null.</param>
/// <param name="Errors">Blocking validation errors; the template must not be saved when non-empty.</param>
/// <param name="Warnings">Non-blocking issues to surface to the caller.</param>
public record WorkflowStructuralValidationResult(
    Workflow? Workflow,
    List<Error> Errors,
    List<WorkflowStructuralValidationWarning> Warnings)
{
    /// <summary>
    /// True when no blocking errors were found.
    /// </summary>
    public bool IsValid => Errors.Count == 0;
}
