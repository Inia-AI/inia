using IniaAi.Domain.Entities.Workflows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Domain.Proposals;

/// <summary>
/// The JSON vocabulary a proposal is built and applied in: the workflow export's own keys, which
/// keys are noise, and how a value is read and compared. Dates stay strings throughout, so a value
/// read from a file and the same value read from a live object compare equal.
/// </summary>
internal static class ProposalJson
{
    public const string SECTIONS = "Sections";
    public const string BLOCKS = "Blocks";
    public const string VARIABLES = "Variables";
    public const string DOCUMENT_CLASSES = "DocumentClasses";
    public const string SIGNATURE_FIELDS = "SignatureFields";
    public const string REGRESSION_FIELDS = "RegressionFields";
    public const string PROPERTIES = "Properties";
    public const string PROPERTIES_PREFIX = "Properties.";
    public const string TYPE = "Type";
    public const string ORDER = "Order";
    public const string NAME = "Name";
    public const string TITLE = "Title";
    public const string TAG = "ReplacementTag";
    public const string KEY = "Key";
    public const string ID = "Id";
    public const string STABLE_BLOCK_ID = "StableBlockId";
    public const string STABLE_SECTION_ID = "StableSectionId";
    public const string SHARED_COMPONENT_ID = "SharedComponentId";
    public const string SHARED_COMPONENT_PREFIX = "SharedComponent";
    public const string NEW_KEY_PREFIX = "new-";

    /// <summary>Row ids, ownership and audit stamps: never content, at any depth.</summary>
    private static readonly HashSet<string> _noiseKeys = new(StringComparer.Ordinal)
    {
        ID, "BlockId", "WorkflowId", "SectionId", "BlockResponseId", "WorkflowTypeId",
        "CreatedAt", "UpdatedAt", "UpdatedByUserId", "IsDeleted", "UserId", "OrganizationId",
        "ExecutionState",
    };

    /// <summary>Run evidence a test run leaves on a block: never part of a proposal.</summary>
    public static readonly HashSet<string> EvidenceKeys = new(StringComparer.Ordinal)
    {
        "ExecutionState", "BlockResponse", "AgentRunTraceFiles", "CitationTrace",
    };

    private static readonly HashSet<string> _newNodeKeys = new(StringComparer.Ordinal)
    {
        ID, STABLE_BLOCK_ID, STABLE_SECTION_ID, "BlockId", "SectionId", "WorkflowId", "BlockResponseId",
        "ExecutionState", "BlockResponse", "AgentRunTraceFiles", "CitationTrace",
    };

    private static readonly HashSet<string> _rowIdKeys = new(StringComparer.Ordinal) { ID, "BlockId" };

    public static readonly JsonSerializerSettings ModelSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
    };

    private static readonly JsonLoadSettings _loadSettings = new() { LineInfoHandling = LineInfoHandling.Ignore };

    /// <summary>Reads JSON text with dates and decimals left as written.</summary>
    public static JToken Parse(string json)
    {
        using var reader = new JsonTextReader(new StringReader(json))
        {
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Decimal,
        };

        return JToken.ReadFrom(reader, _loadSettings);
    }

    public static JObject ParseObject(string json)
    {
        return Parse(json) as JObject ?? throw new JsonReaderException("The JSON is not an object");
    }

    /// <summary>A live object as the workflow export spells it, run evidence included.</summary>
    public static JObject ToJson(object value)
    {
        return ParseObject(JsonConvert.SerializeObject(value, Workflow.ExportWithRunTracesSettings));
    }

    public static JsonSerializer Serializer()
    {
        return JsonSerializer.Create(Workflow.ExportWithRunTracesSettings);
    }

    /// <summary>Overwrites the named members of a live object from a JSON object.</summary>
    public static void Populate(JObject members, object target)
    {
        JsonSerializerSettings settings = new(Workflow.ExportWithRunTracesSettings)
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,
        };
        JsonSerializer.Create(settings).Populate(members.CreateReader(), target);
    }

    public static T Deserialize<T>(JToken json)
    {
        return json.ToObject<T>(Serializer())
            ?? throw new JsonSerializationException($"The value is not a {typeof(T).Name}");
    }

    /// <summary>A copy without noise keys at any depth, and with nulls treated as absent.</summary>
    public static JToken? Normalize(JToken? token)
    {
        if (token is null || token.Type == JTokenType.Null)
        {
            return null;
        }

        JToken copy = token.DeepClone();
        Strip(copy, _noiseKeys);
        return copy;
    }

    /// <summary>A copy without the named keys at any depth.</summary>
    public static JToken Without(JToken token, IReadOnlySet<string> keys)
    {
        JToken copy = token.DeepClone();
        Strip(copy, keys);
        return copy;
    }

    public static IReadOnlySet<string> NoiseKeys => _noiseKeys;

    /// <summary>
    /// A node the agent added, made ready to become a new node: no row ids and no stable ids, at
    /// the node's level and on its rows and blocks, so each gets its own when it is created.
    /// </summary>
    public static JToken AsNewNode(JToken node)
    {
        return Without(node, _newNodeKeys);
    }

    /// <summary>A field value with its rows' ids dropped, so each row is created anew.</summary>
    public static JToken WithoutRowIds(JToken value)
    {
        return Without(value, _rowIdKeys);
    }

    public static bool Same(JToken? left, JToken? right)
    {
        return JToken.DeepEquals(Normalize(left), Normalize(right));
    }

    public static string? Text(JToken? token)
    {
        JToken? normalized = Normalize(token);
        return normalized?.ToString(Formatting.None);
    }

    public static string? StringOf(JToken? token)
    {
        return token is JValue { Type: JTokenType.String } value ? (string?)value : null;
    }

    public static bool IsSharedComponentKey(string key)
    {
        return key.StartsWith(SHARED_COMPONENT_PREFIX, StringComparison.Ordinal);
    }

    public static bool HasSharedComponentFields(JObject node)
    {
        return node.Properties().Any(property => IsSharedComponentKey(property.Name) && property.Value.Type != JTokenType.Null);
    }

    /// <summary>
    /// Removes the keys at every depth except inside a <c>Properties</c> object, whose members are
    /// the block's own settings: there only run evidence goes, because an id inside a setting
    /// (a verification criterion's, say) is content.
    /// </summary>
    private static void Strip(JToken token, IReadOnlySet<string> keys)
    {
        if (token is JObject obj)
        {
            foreach (JProperty property in obj.Properties().ToList())
            {
                if (keys.Contains(property.Name) || property.Value.Type == JTokenType.Null)
                {
                    property.Remove();
                }
                else if (property.Name == PROPERTIES && property.Value is JObject settings)
                {
                    StripSettings(settings, keys);
                }
                else
                {
                    Strip(property.Value, keys);
                }
            }
        }
        else if (token is JArray array)
        {
            foreach (JToken item in array)
            {
                Strip(item, keys);
            }
        }
    }

    private static void StripSettings(JObject settings, IReadOnlySet<string> keys)
    {
        foreach (JProperty property in settings.Properties().ToList())
        {
            if (property.Value.Type == JTokenType.Null || (EvidenceKeys.Contains(property.Name) && keys.Contains(property.Name)))
            {
                property.Remove();
            }
        }
    }
}
