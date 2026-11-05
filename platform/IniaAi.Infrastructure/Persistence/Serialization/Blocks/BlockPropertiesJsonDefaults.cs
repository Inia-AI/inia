using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace IniaAi.Infrastructure.Persistence.Serialization.Blocks;

public static class BlockPropertiesJsonDefaults
{
    private const string EXECUTION_STATE_PROPERTY_NAME = "ExecutionState";

    /// <summary>
    /// Default options used by UserData persistence. Serializes BlockProperties including
    /// the runtime ExecutionState.
    /// </summary>
    public static readonly JsonSerializerOptions Options;

    /// <summary>
    /// Options used by Core persistence (workflow templates). Excludes the runtime
    /// ExecutionState property from BlockProperties — templates have no execution state.
    /// </summary>
    public static readonly JsonSerializerOptions CoreOptions;

    static BlockPropertiesJsonDefaults()
    {
        Options = new JsonSerializerOptions(JsonDefaults.OptionsWithOriginalKeys);
        Options.Converters.Add(new BlockPropertiesJsonConverter());

        CoreOptions = new JsonSerializerOptions(JsonDefaults.OptionsWithOriginalKeys);
        CoreOptions.Converters.Add(new BlockPropertiesJsonConverter());
        CoreOptions.TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { StripExecutionStateFromBlockProperties }
        };
    }

    private static void StripExecutionStateFromBlockProperties(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind is not JsonTypeInfoKind.Object)
        {
            return;
        }

        if (!typeof(BlockProperties).IsAssignableFrom(typeInfo.Type))
        {
            return;
        }

        for (int i = typeInfo.Properties.Count - 1; i >= 0; i--)
        {
            // The options camel-case property names, so the JSON name is "executionState";
            // an ordinal compare against the CLR name silently disabled the strip.
            if (string.Equals(typeInfo.Properties[i].Name, EXECUTION_STATE_PROPERTY_NAME, StringComparison.OrdinalIgnoreCase))
            {
                typeInfo.Properties.RemoveAt(i);
            }
        }
    }
}
