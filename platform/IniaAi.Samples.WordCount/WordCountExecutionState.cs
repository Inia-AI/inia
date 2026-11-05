using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates.Abstractions;
using Newtonsoft.Json;

namespace IniaAi.Samples.WordCount;

/// <summary>The text of a Count Words block with the earlier blocks' outputs filled in.</summary>
public sealed class WordCountExecutionState : BlockPropertiesExecutionStateBase<WordCountExecutionState>
{
    [JsonProperty(nameof(Text), NullValueHandling = NullValueHandling.Ignore)]
    public string? Text { get; set; }

    [JsonConstructor]
    [System.Text.Json.Serialization.JsonConstructor]
    public WordCountExecutionState()
    {
    }

    public WordCountExecutionState(WordCountProperties source)
    {
        Text = source.TextTemplate;
    }
}
