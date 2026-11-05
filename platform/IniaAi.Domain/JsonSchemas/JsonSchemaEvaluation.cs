using Corvus.Text.Json;
using Corvus.Text.Json.RuntimeEvaluator;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.Text;
using Validator = Corvus.Text.Json.Validator;

namespace IniaAi.Domain.JsonSchemas;

/// <summary>
/// The one place the platform compiles a JSON Schema and checks an instance against it.
/// Callers own their own error wording, because the messages reach different audiences:
/// prompt engineers authoring an output format, or the model itself in a corrective turn.
/// </summary>
public static class JsonSchemaEvaluation
{
    private const string LOCATION_SEPARATOR = "/";
    private const string SCHEMA_ADDRESS_PREFIX = "https://schemas.inia.invalid/sha256/";
    private const string SUBSCHEMA_SUMMARY = "The value was expected to match the subschema.";
    private const string NO_DETAIL = "the response does not match the schema";
    private const string PROPERTY_NOT_ALLOWED = "the schema does not allow this property";
    private const string ITEM_NOT_ALLOWED = "the schema does not allow this item";
    private const string VALUE_NOT_ALLOWED = "the schema does not allow this value";
    private const string VALUE_NOT_LISTED = "the value is not one of the values the schema lists";
    private const string ENUM = "enum";
    private const string PROPERTIES = "properties";
    private const string PATTERN_PROPERTIES = "patternProperties";
    private const string ADDITIONAL_PROPERTIES = "additionalProperties";
    private const string UNEVALUATED_PROPERTIES = "unevaluatedProperties";
    private const string ITEMS = "items";
    private const string PREFIX_ITEMS = "prefixItems";
    private const string ADDITIONAL_ITEMS = "additionalItems";
    private const string UNEVALUATED_ITEMS = "unevaluatedItems";

    /// <summary>
    /// A schema that omits <c>$schema</c> is read as draft 2020-12; a schema that declares its
    /// own dialect still wins. A <c>$ref</c> resolves only inside the schema, because a schema
    /// that a prompt engineer writes must not make the server fetch a URL or read a file. A
    /// <c>format</c> stays an annotation, as draft 2020-12 has it.
    /// </summary>
    private static readonly Validator.JsonSchema.Options _options = new(
        alwaysAssertFormat: false,
        allowFileSystemAndHttpResolution: false,
        defaultDialect: JsonSchemaDialect.Draft202012);

    /// <summary>
    /// Compiles schema text. Throws when the text is not a usable schema, which is a
    /// configuration fault rather than a failed instance.
    /// </summary>
    public static CompiledJsonSchema CompileSchema(string schemaJson)
    {
        // The library keeps a compiled schema for the life of the process under the address it is
        // given, and returns the kept one for that address. The address is the hash of the text,
        // so a changed schema never gets the rules of an earlier text.
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(schemaJson)));
        return new CompiledJsonSchema(Validator.JsonSchema.FromText(schemaJson, canonicalUri: SCHEMA_ADDRESS_PREFIX + hash, options: _options));
    }

    /// <summary>
    /// Returns one message per violation, empty when the instance satisfies the schema.
    /// Takes the already-parsed instance rather than its source text: Newtonsoft accepts
    /// input System.Text.Json rejects, such as trailing commas and comments, so re-reading
    /// the original text here would throw on responses the caller already accepted.
    /// </summary>
    public static IReadOnlyList<string> FindViolations(CompiledJsonSchema schema, JToken instance)
    {
        return [.. Evaluate(schema, instance).Select(static violation => violation.ToString())];
    }

    /// <summary>
    /// The violations at the deepest failing locations, empty when the instance satisfies the
    /// schema. A node that fails because a member fails carries only a summary of the member's
    /// failure, which would point the reader at the whole parent, so it is left out.
    /// </summary>
    public static IReadOnlyList<JsonSchemaViolation> FindLeafViolations(CompiledJsonSchema schema, JToken instance)
    {
        IReadOnlyList<JsonSchemaViolation> violations = Evaluate(schema, instance);
        var ancestors = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonSchemaViolation violation in violations)
        {
            string location = violation.Location;
            for (int end = location.LastIndexOf(LOCATION_SEPARATOR, StringComparison.Ordinal); end >= 0; end = end == 0 ? -1 : location.LastIndexOf(LOCATION_SEPARATOR, end - 1, StringComparison.Ordinal))
            {
                _ = ancestors.Add(location[..end]);
            }
        }

        return [.. violations.Where(violation => !ancestors.Contains(violation.Location))];
    }

    private static IReadOnlyList<JsonSchemaViolation> Evaluate(CompiledJsonSchema schema, JToken instance)
    {
        using JsonSchemaResultsCollector collector = JsonSchemaResultsCollector.Create(JsonSchemaResultsLevel.Detailed);
        if (schema.Schema.Validate(instance.ToString(Formatting.None), collector))
        {
            return [];
        }

        List<JsonSchemaViolation> violations = [];
        foreach (JsonSchemaResultsCollector.Result result in collector.EnumerateResults())
        {
            string message = result.GetMessageText();
            if (result.IsMatch || message == SUBSCHEMA_SUMMARY)
            {
                continue;
            }

            violations.Add(new JsonSchemaViolation(result.GetDocumentEvaluationLocationText(), Worded(message, result.GetSchemaEvaluationLocationText())));
        }

        if (violations.Count == 0)
        {
            violations.Add(new JsonSchemaViolation(string.Empty, NO_DETAIL));
        }

        return [.. violations.Distinct()];
    }

    /// <summary>
    /// The library's own message, except where it says nothing or misleads: a <c>false</c>
    /// schema fails with no message, and an <c>enum</c> mismatch reads as a failed subschema.
    /// </summary>
    private static string Worded(string message, string schemaLocation)
    {
        string[] segments = schemaLocation.Split(LOCATION_SEPARATOR, StringSplitOptions.RemoveEmptyEntries);
        string keyword = segments.Length > 0 ? segments[^1] : string.Empty;
        string parent = segments.Length > 1 ? segments[^2] : string.Empty;

        if (keyword == ENUM)
        {
            return VALUE_NOT_LISTED;
        }

        if (message.Length > 0)
        {
            return message;
        }

        if (keyword is ADDITIONAL_PROPERTIES or UNEVALUATED_PROPERTIES || parent is PROPERTIES or PATTERN_PROPERTIES)
        {
            return PROPERTY_NOT_ALLOWED;
        }

        return keyword is ITEMS or ADDITIONAL_ITEMS or UNEVALUATED_ITEMS || parent == PREFIX_ITEMS ? ITEM_NOT_ALLOWED : VALUE_NOT_ALLOWED;
    }
}

/// <summary>
/// A JSON Schema compiled once, to check any number of instances.
/// </summary>
public sealed class CompiledJsonSchema
{
    internal CompiledJsonSchema(Validator.JsonSchema schema)
    {
        Schema = schema;
    }

    internal Validator.JsonSchema Schema { get; }
}

/// <summary>
/// One schema failure: the JSON Pointer of the failing instance node (empty for the root) and
/// its message.
/// </summary>
public sealed record JsonSchemaViolation(string Location, string Message)
{
    public override string ToString()
    {
        return string.IsNullOrEmpty(Location) ? Message : $"{Location}: {Message}";
    }
}
