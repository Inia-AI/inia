using IniaAi.WebApi.Contracts.Converters;
using System.Text.Json.Serialization;

namespace IniaAi.WebApi.Models.Dto;

/// <summary>
/// Represents a workflow variable configuration within the Inia AI Web API.
/// </summary>
public sealed class WorkflowVariableDto
{
    /// <summary>
    /// Gets or sets the unique identifier of the workflow variable.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Gets or sets the replacement tag used for variable substitution.
    /// </summary>
    public required string ReplacementTag { get; set; }

    /// <summary>
    /// Gets or sets the display name of the variable.
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the description of the variable.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the type of the variable.
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the variable is required.
    /// </summary>
    public bool IsRequired { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the variable is hidden.
    /// </summary>
    public bool IsHidden { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the variable is confidential.
    /// </summary>
    public bool IsConfidential { get; set; }

    /// <summary>
    /// Gets or sets the default value of the variable.
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Gets or sets the dictionary of options for dropdown or multi-select variables.
    /// Key represents the internal value, Value represents the display name.
    /// </summary>
    [JsonConverter(typeof(WorkflowVariableOptionsConverter))]
    public Dictionary<string, string> Options { get; set; } = [];
}
