using IniaAi.Common.Serialization;
using IniaAi.Domain.Entities.AgentRuntime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.Text;

namespace IniaAi.Domain.Entities.Workflows;

/// <summary>
/// Export-only shaping of an <see cref="AgentRunTraceFile"/>. A run's <c>trace/</c> diagnostics are
/// newline-delimited JSON, so a downloaded snapshot inlines them as a readable JSON array instead of
/// the opaque base64 a <see cref="byte"/> array otherwise serializes to — the point of embedding a
/// pack is to read how the agent produced its numbers offline. <c>out/</c> artifacts stay base64:
/// they are member work-product that may be binary, and the live resume path re-verifies their
/// sha256 byte-for-byte. The reverse read rebuilds the newline-delimited bytes so the editor's trace
/// panel parses an uploaded export exactly as it parses a freshly collected pack, and re-hashes the
/// rebuilt content so the row stays internally consistent. A base64 string still reads back verbatim,
/// so a pack exported before this shaping existed still imports.
///
/// The emitted <c>Sha256</c>/<c>SizeBytes</c> describe the run's originally collected bytes as
/// provenance; a row rebuilt from an inlined array recomputes them over the normalized content, so
/// the two differ by design when intra-line whitespace was dropped — trace shas are never verified,
/// only <c>out/</c> shas are. This changes only opt-in trace <em>content</em> shape, not the template
/// schema, so it deliberately does not bump <c>CURRENT_FORMAT_VERSION</c>: an old build reading a new
/// export fails safe (the array will not bind to a <c>byte</c> array, so <c>FromExportJson</c> returns
/// null) rather than every export — plain workflows included — being rejected by a version bump.
/// </summary>
internal sealed class AgentRunTraceFileExportConverter : JsonConverter<AgentRunTraceFile>
{
    // The sidecar's kind-by-prefix convention (diagnostics under trace/, artifacts under out/) is
    // owned by AgentRunFilePaths in IniaAi.Common.Interfaces, which references Domain — so this
    // Domain-level converter cannot read it without a reference cycle. Keep the literal local; a
    // trace/ path this misses only falls back to base64, never corrupts.
    private const string TRACE_PATH_PREFIX = "trace/";

    public override void WriteJson(JsonWriter writer, AgentRunTraceFile? value, JsonSerializer serializer)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        writer.WriteStartObject();
        WriteString(writer, nameof(AgentRunTraceFile.Id), value.Id);
        WriteString(writer, nameof(AgentRunTraceFile.OrganizationId), value.OrganizationId);
        WriteString(writer, nameof(AgentRunTraceFile.BlockId), value.BlockId);
        WriteString(writer, nameof(AgentRunTraceFile.RunExecutionId), value.RunExecutionId);
        writer.WritePropertyName(nameof(AgentRunTraceFile.Attempt));
        writer.WriteValue(value.Attempt);
        WriteString(writer, nameof(AgentRunTraceFile.Path), value.Path);
        WriteString(writer, nameof(AgentRunTraceFile.Sha256), value.Sha256);
        writer.WritePropertyName(nameof(AgentRunTraceFile.SizeBytes));
        writer.WriteValue(value.SizeBytes);
        writer.WritePropertyName(nameof(AgentRunTraceFile.Content));
        WriteContent(writer, value);
        writer.WritePropertyName(nameof(AgentRunTraceFile.CollectedAt));
        writer.WriteValue(value.CollectedAt);
        writer.WriteEndObject();
    }

    public override AgentRunTraceFile? ReadJson(
        JsonReader reader,
        Type objectType,
        AgentRunTraceFile? existingValue,
        bool hasExistingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
        {
            return null;
        }

        JObject o = JsonText.LoadObject(reader);
        var file = new AgentRunTraceFile
        {
            Id = (string?)o[nameof(AgentRunTraceFile.Id)] ?? Guid.NewGuid().ToString(),
            OrganizationId = (string?)o[nameof(AgentRunTraceFile.OrganizationId)] ?? string.Empty,
            BlockId = (string?)o[nameof(AgentRunTraceFile.BlockId)],
            RunExecutionId = (string?)o[nameof(AgentRunTraceFile.RunExecutionId)] ?? string.Empty,
            Attempt = (int?)o[nameof(AgentRunTraceFile.Attempt)] ?? 0,
            Path = (string?)o[nameof(AgentRunTraceFile.Path)] ?? string.Empty,
            Sha256 = (string?)o[nameof(AgentRunTraceFile.Sha256)] ?? string.Empty,
            SizeBytes = (long?)o[nameof(AgentRunTraceFile.SizeBytes)] ?? 0,
            CollectedAt = o[nameof(AgentRunTraceFile.CollectedAt)]?.ToObject<DateTime?>(serializer) ?? DateTime.UtcNow,
        };

        ReadContent(file, o[nameof(AgentRunTraceFile.Content)]);
        return file;
    }

    private static void WriteContent(JsonWriter writer, AgentRunTraceFile file)
    {
        if (file.Path.StartsWith(TRACE_PATH_PREFIX, StringComparison.Ordinal)
            && TryReadNdjsonLines(file.Content, out JArray lines))
        {
            lines.WriteTo(writer);
            return;
        }

        writer.WriteValue(file.Content);
    }

    private static void ReadContent(AgentRunTraceFile file, JToken? contentToken)
    {
        if (contentToken is JArray lines)
        {
            string text = string.Join('\n', lines.Select(static line => line.ToString(Formatting.None)));
            file.Content = Encoding.UTF8.GetBytes(text);
            file.SizeBytes = file.Content.LongLength;
            file.Sha256 = Convert.ToHexStringLower(SHA256.HashData(file.Content));
            return;
        }

        string? base64 = (string?)contentToken;
        file.Content = string.IsNullOrEmpty(base64) ? [] : Convert.FromBase64String(base64);
    }

    /// <summary>
    /// Parses newline-delimited JSON into an array of its line tokens, or reports it is not
    /// inlineable so the caller falls back to base64 — an empty file, or a <c>trace/</c> path whose
    /// bytes are not valid line-wise JSON, keeps the opaque encoding rather than corrupt output.
    /// </summary>
    private static bool TryReadNdjsonLines(byte[] content, out JArray lines)
    {
        lines = [];
        if (content.Length == 0)
        {
            return false;
        }

        var parsed = new JArray();
        foreach (string line in Encoding.UTF8.GetString(content).Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                parsed.Add(JsonText.Parse(line));
            }
            catch (JsonException)
            {
                return false;
            }
        }

        if (parsed.Count == 0)
        {
            return false;
        }

        lines = parsed;
        return true;
    }

    private static void WriteString(JsonWriter writer, string propertyName, string? value)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteValue(value);
    }
}
