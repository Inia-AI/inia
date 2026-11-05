using IniaAi.Common.Serialization;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.Text;

namespace IniaAi.Domain.Serialization;

/// <summary>
/// Produces a deterministic JSON representation of a workflow template that is identical
/// for two semantically equal templates regardless of in-memory ordering, entity Ids,
/// audit timestamps, or execution state. <see cref="ToCanonicalJson"/> is the PAYLOAD form
/// (stored as the version payload and served as the template representation — it keeps the
/// shared-component link fields and the stable ids so restores and API round-trips preserve
/// linkage and instance identity), while <see cref="ComputeContentHash(Workflow)"/> hashes the
/// stricter HASH form with both suppressed — linking, breaking out, or backfilling a stable id
/// never changes a template's content hash.
/// <see cref="ToComponentCanonicalJson(Block)"/>/<see cref="ToComponentCanonicalJson(Section)"/>
/// canonicalize a single entity for shared-component definition payloads, always in the HASH
/// form: a definition never embeds instance link state.
/// </summary>
public sealed class WorkflowCanonicalizer
{
    public const string CONTENT_HASH_PATTERN = "^[0-9a-f]{64}$";

    private static readonly string SECTIONS = nameof(Workflow.Sections);
    private static readonly string BLOCKS = nameof(Section.Blocks);
    private static readonly string VARIABLES = nameof(Workflow.Variables);
    private static readonly string DOCUMENT_CLASSES = nameof(Workflow.DocumentClasses);
    private static readonly string TEXT_INPUTS = nameof(Block.TextInputs);
    private static readonly string RAG_SETTINGS = nameof(Block.RagSettings);
    private static readonly string SUPPORTED_DOCUMENT_CLASSES = nameof(Block.SupportedDocumentClasses);
    private static readonly string ORDER = nameof(Block.Order);
    private static readonly string REPLACEMENT_TAG = nameof(Block.ReplacementTag);
    private static readonly string KEY = nameof(DocumentClass.Key);
    private static readonly string REGRESSION_FIELDS = nameof(Workflow.RegressionFields);
    private static readonly string SIGNATURE_FIELDS = nameof(Workflow.SignatureFields);
    private static readonly string WORKER_SKILLS = nameof(AgentProperties.WorkerSkills);
    private static readonly string NAME = nameof(WorkflowRegressionField.Name);

    private static readonly JsonSerializerSettings _payloadSettings = new()
    {
        ContractResolver = new CanonicalContractResolver(payloadProfile: true),
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
    };

    private static readonly JsonSerializerSettings _hashSettings = new()
    {
        ContractResolver = new CanonicalContractResolver(payloadProfile: false),
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
    };

    public string ToCanonicalJson(Workflow workflow)
    {
        return Canonical(workflow, _payloadSettings);
    }

    public string ToHashCanonicalJson(Workflow workflow)
    {
        return Canonical(workflow, _hashSettings);
    }

    public string ToComponentCanonicalJson(Block block)
    {
        return Canonical(block, _hashSettings);
    }

    public string ToComponentCanonicalJson(Section section)
    {
        return Canonical(section, _hashSettings);
    }

    public string ComputeContentHash(Workflow workflow)
    {
        return ComputeContentHash(ToHashCanonicalJson(workflow));
    }

    /// <summary>
    /// A cheap answer to "did anything change since I last looked": the hash form serialized
    /// straight into SHA-256, with no canonical ordering and no intermediate strings, which keeps a
    /// large template off the large-object heap. It is not a content identity, because two equal
    /// templates can order their lists differently; compare it only with an earlier fingerprint of
    /// the same in-memory workflow.
    /// </summary>
    public string ComputeChangeFingerprint(Workflow workflow)
    {
        using var sha256 = SHA256.Create();
        using (var hashing = new CryptoStream(Stream.Null, sha256, CryptoStreamMode.Write))
        using (var writer = new StreamWriter(hashing, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        using (var json = new JsonTextWriter(writer))
        {
            JsonSerializer.Create(_hashSettings).Serialize(json, workflow);
        }

        return Convert.ToHexStringLower(sha256.Hash!);
    }

    /// <summary>
    /// The content hash a stored workflow version has when today's model reads it. A version saved
    /// before the model dropped a field keeps that field in its payload and in its stored hash, and
    /// the live template, written by today's model, has neither, so the live template compares
    /// against this hash. The stored hash stays the version's identity. Null when the payload
    /// cannot be read.
    /// </summary>
    public string? ComputeStoredContentHash(string versionJson)
    {
        return Workflow.FromJson(versionJson) is { } workflow ? ComputeContentHash(workflow) : null;
    }

    public string ComputeContentHash(string canonicalJson)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson));

        return Convert.ToHexStringLower(hash);
    }

    private static string Canonical(object entity, JsonSerializerSettings settings)
    {
        string stripped = JsonConvert.SerializeObject(entity, Formatting.None, settings);
        JToken canonical = Canonicalize(JsonText.Parse(stripped), arrayName: null);

        return canonical.ToString(Formatting.None);
    }

    private static JToken Canonicalize(JToken node, string? arrayName)
    {
        switch (node)
        {
            case JObject obj:
                var canonicalObject = new JObject();
                foreach (JProperty property in obj.Properties()
                    .OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    canonicalObject.Add(property.Name, Canonicalize(property.Value, property.Name));
                }

                return canonicalObject;

            case JArray array:
                List<JToken> items = [.. array.Select(item => Canonicalize(item, null))];

                return new JArray(SortItems(arrayName, items));

            default:
                return node;
        }
    }

    private static IEnumerable<JToken> SortItems(string? arrayName, List<JToken> items)
    {
        if (arrayName == SECTIONS || arrayName == BLOCKS)
        {
            return items.OrderBy(item => (int?)item[ORDER] ?? 0);
        }

        if (arrayName == VARIABLES || arrayName == TEXT_INPUTS)
        {
            return items.OrderBy(
                item => (string?)item[REPLACEMENT_TAG] ?? string.Empty,
                StringComparer.Ordinal);
        }

        if (arrayName == DOCUMENT_CLASSES)
        {
            return items.OrderBy(item => (string?)item[KEY] ?? string.Empty, StringComparer.Ordinal);
        }

        if (arrayName == REGRESSION_FIELDS || arrayName == SIGNATURE_FIELDS || arrayName == WORKER_SKILLS)
        {
            return items.OrderBy(item => (string?)item[NAME] ?? string.Empty, StringComparer.Ordinal);
        }

        if (arrayName == SUPPORTED_DOCUMENT_CLASSES)
        {
            return items.OrderBy(item => (string?)item ?? string.Empty, StringComparer.Ordinal);
        }

        if (arrayName == RAG_SETTINGS)
        {
            return items.OrderBy(item => item.ToString(Formatting.None), StringComparer.Ordinal);
        }

        return items;
    }
}
