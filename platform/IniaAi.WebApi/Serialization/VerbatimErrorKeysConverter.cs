using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace IniaAi.WebApi.Serialization;

/// <summary>
/// Writes the <c>errors</c> map of a validation refusal with every key exactly as the server named it:
/// an error code (<c>General.Validation</c>), a request property (<c>Name</c>), a JSON path
/// (<c>$.count</c>) or an OData option (<c>$filter</c>). The API's camelCase dictionary-key policy
/// would otherwise print <c>general.Validation</c> beside the <c>General.NotFound</c> code of every
/// other refusal. The model-state 400, the OData query filter and <c>ApiController.Problem</c> all
/// serialize through this one converter.
/// </summary>
public sealed class VerbatimErrorKeysConverter : JsonConverter<ValidationProblemDetails>
{
    private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> _verbatimOptions = new();

    /// <inheritdoc />
    public override ValidationProblemDetails? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return JsonSerializer.Deserialize<ValidationProblemDetails>(ref reader, VerbatimCopyOf(options));
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ValidationProblemDetails value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, VerbatimCopyOf(options));
    }

    private static JsonSerializerOptions VerbatimCopyOf(JsonSerializerOptions options)
    {
        return _verbatimOptions.GetValue(options, static source =>
        {
            var copy = new JsonSerializerOptions(source)
            {
                DictionaryKeyPolicy = null,
            };

            foreach (JsonConverter converter in copy.Converters.OfType<VerbatimErrorKeysConverter>().ToList())
            {
                _ = copy.Converters.Remove(converter);
            }

            return copy;
        });
    }
}
