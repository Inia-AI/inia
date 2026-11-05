using ErrorOr;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// Parses an author-typed output schema before a run burns on it: the wire's
/// <c>outputFormat.schema</c> is a JSON Schema object, and a malformed one would burn a full
/// run before the runtime rejected it. Pure and consumer-agnostic — callers convert the
/// errors into their own failure flavour.
/// </summary>
public static class AgentOutputSchemas
{
    public const string ERROR_INVALID_JSON = "AgentRun.OutputSchemaInvalidJson";
    public const string ERROR_NOT_AN_OBJECT = "AgentRun.OutputSchemaNotAnObject";

    /// <summary>
    /// The original <see cref="JsonException"/> rides the <see cref="ERROR_INVALID_JSON"/>
    /// error's metadata under this key, so callers keep the parser's failure detail.
    /// </summary>
    public const string EXCEPTION_METADATA_KEY = "exception";

    /// <summary>
    /// Blank input is a valid "no schema" and parses to null. Text that is not JSON returns
    /// <see cref="ERROR_INVALID_JSON"/>; a JSON value that is not an object returns
    /// <see cref="ERROR_NOT_AN_OBJECT"/>.
    /// </summary>
    public static ErrorOr<JsonObject?> Parse(string? outputSchema)
    {
        if (string.IsNullOrWhiteSpace(outputSchema))
        {
            return (JsonObject?)null;
        }

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(outputSchema);
        }
        catch (JsonException exception)
        {
            return Error.Validation(
                ERROR_INVALID_JSON,
                "The output schema is not valid JSON",
                new Dictionary<string, object> { [EXCEPTION_METADATA_KEY] = exception });
        }

        if (parsed is not JsonObject schema)
        {
            return Error.Validation(ERROR_NOT_AN_OBJECT, "The output schema must be a JSON object");
        }

        return schema;
    }
}
