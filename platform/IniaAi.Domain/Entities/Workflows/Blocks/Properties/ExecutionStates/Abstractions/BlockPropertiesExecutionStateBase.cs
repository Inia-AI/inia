using IniaAi.Common.Reflection;
using IniaAi.Common.Serialization;
using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates.Abstractions;

/// <summary>
/// Base class for typed BlockProperties execution states.
/// Provides reflection-based placeholder replacement for all string, dictionary, and list properties.
/// </summary>
public abstract class BlockPropertiesExecutionStateBase<T> : JsonSerializable<T>, ISanitizationWarningSink
    where T : class
{
    /// <summary>
    /// Non-fatal notices collected during placeholder replacement (e.g. a pre-escaped value
    /// substituted verbatim into a JSON-sanitized property). Persisted with the executed state;
    /// the executing strategy may also surface them to its logs.
    /// </summary>
    [PlaceholderReplacementIgnore]
    [JsonProperty(nameof(SanitizationWarnings), NullValueHandling = NullValueHandling.Ignore)]
    public List<string> SanitizationWarnings { get; set; } = [];

    public void AddSanitizationWarning(string warning)
    {
        SanitizationWarnings.Add(warning);
    }

    public bool ShouldSerializeSanitizationWarnings()
    {
        return SanitizationWarnings.Count > 0;
    }

    public void ReplaceAllPlaceholders(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        PlaceholderReflectionHelper.ReplaceAll(this, asPlaceHolder, inputs, stringComparison);
    }
}
