using IniaAi.Common.Reflection;
using IniaAi.Common.Serialization;
using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows;

public class TextInputExecutionState : JsonSerializable<TextInputExecutionState>
{
    [JsonProperty(nameof(Value))]
    public string? Value { get; set; }

    [JsonConstructor]
    internal TextInputExecutionState()
    {
    }

    public TextInputExecutionState(TextInput blockInput)
    {
        Value = blockInput.Value;
    }

    public void ReplaceAllPlaceholders(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        PlaceholderReflectionHelper.ReplaceAll(this, asPlaceHolder, inputs, stringComparison);
    }
}
