using IniaAi.Domain.Entities.SharedComponents.Enums;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IniaAi.Infrastructure.Persistence.Serialization;

public static class SharedComponentSlotStatesColumn
{
    private static readonly JsonSerializerOptions _options = BuildOptions();

    public static void Apply(
        PropertyBuilder<Dictionary<string, SharedComponentSlotState>?> property,
        string? providerName)
    {
        SharedComponentJsonDictionaryColumn.Apply(property, providerName, _options);
    }

    private static JsonSerializerOptions BuildOptions()
    {
        var options = new JsonSerializerOptions(JsonDefaults.OptionsWithOriginalKeys);
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }
}
