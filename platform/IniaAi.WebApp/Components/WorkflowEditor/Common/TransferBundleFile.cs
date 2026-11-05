using System.Text.Json;
using System.Text.Json.Serialization;
using IniaAi.WebApi.Contracts.Dto.WorkflowTransfer;
using DomainWorkflow = IniaAi.Domain.Entities.Workflows.Workflow;

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

public enum TransferPayloadKind
{
    Unknown = 0,
    WorkflowJson = 1,
    TransferBundle = 2,
}

public static class TransferBundleFile
{
    public const string FILE_EXTENSION = ".workflowbundle";

    public const string UPLOAD_ACCEPT = FILE_EXTENSION + ",.json";

    public const long MAX_FILE_SIZE_BYTES = 1024 * 1024 * 100;

    private const string LEGACY_CHILDREN_MARKER = "Children";

    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(WorkflowTransferBundle bundle)
    {
        return JsonSerializer.Serialize(bundle, _serializerOptions);
    }

    public static WorkflowTransferBundle? TryDeserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        WorkflowTransferBundle? bundle;
        try
        {
            bundle = JsonSerializer.Deserialize<WorkflowTransferBundle>(StripByteOrderMark(json), _serializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (bundle is null
            || bundle.BundleFormatVersion < 1
            || string.IsNullOrWhiteSpace(bundle.OriginWorkflowTypeId)
            || string.IsNullOrWhiteSpace(bundle.TemplateJson))
        {
            return null;
        }

        return bundle with
        {
            TemplateHashTrail = bundle.TemplateHashTrail ?? [],
            Components = bundle.Components ?? [],
            Categories = bundle.Categories ?? [],
        };
    }

    public static TransferPayloadKind Classify(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return TransferPayloadKind.Unknown;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(StripByteOrderMark(json));
        }
        catch (JsonException)
        {
            return TransferPayloadKind.Unknown;
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return TransferPayloadKind.Unknown;
            }

            if (HasProperty(root, nameof(WorkflowTransferBundle.BundleFormatVersion), JsonValueKind.Number)
                && HasProperty(root, nameof(WorkflowTransferBundle.TemplateJson), JsonValueKind.String))
            {
                return TransferPayloadKind.TransferBundle;
            }

            if (HasProperty(root, nameof(DomainWorkflow.FormatVersion), JsonValueKind.Number)
                || HasProperty(root, nameof(DomainWorkflow.Sections), JsonValueKind.Array)
                || HasProperty(root, LEGACY_CHILDREN_MARKER, JsonValueKind.Array))
            {
                return TransferPayloadKind.WorkflowJson;
            }

            return TransferPayloadKind.Unknown;
        }
    }

    private static bool HasProperty(JsonElement root, string name, JsonValueKind expectedKind)
    {
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == expectedKind)
            {
                return true;
            }
        }

        return false;
    }

    private static string StripByteOrderMark(string json)
    {
        return json.TrimStart((char)0xFEFF);
    }
}
