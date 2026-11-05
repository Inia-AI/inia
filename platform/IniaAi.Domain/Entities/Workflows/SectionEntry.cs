using IniaAi.Common.Serialization;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows;

public sealed record SectionEntry
{
    [JsonProperty(nameof(SlotTag))]
    public string SlotTag { get; init; } = string.Empty;

    [JsonProperty(nameof(Kind))]
    [JsonConverter(typeof(TolerantStringEnumConverter))]
    public SectionEntryKind Kind { get; init; }

    [JsonProperty(nameof(SharedComponentId), NullValueHandling = NullValueHandling.Ignore)]
    public string? SharedComponentId { get; init; }

    [JsonProperty(nameof(Bindings), NullValueHandling = NullValueHandling.Ignore)]
    public Dictionary<string, string>? Bindings { get; init; }

    public bool ShouldSerializeBindings()
    {
        return Bindings is { Count: > 0 };
    }
}
