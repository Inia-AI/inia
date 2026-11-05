using ErrorOr;
using IniaAi.Domain.Entities.Workflows;
using System.Text.Json;

namespace IniaAi.Application.Workflows.Interfaces;

/// <summary>
/// Service for validating workflow variables.
/// </summary>
public interface IWorkflowVariableValidationService
{
    /// <summary>
    /// Validates workflow variables against their definitions.
    /// </summary>
    /// <param name="workflow">The workflow containing variable definitions.</param>
    /// <param name="variables">The variable values to validate.</param>
    /// <returns>A list of validation errors, if any.</returns>
    List<Error> ValidateVariables(Workflow workflow, Dictionary<string, JsonElement>? variables);

    /// <summary>
    /// Converts workflow variable values to strings for job inputs.
    /// </summary>
    /// <param name="variables">The variable values to convert.</param>
    /// <returns>A dictionary of string values ready for job inputs.</returns>
    Dictionary<string, string> ConvertVariablesToStrings(Dictionary<string, JsonElement>? variables);

    Dictionary<string, object> ConvertVariablesToObjects(Dictionary<string, JsonElement>? variables);
}
