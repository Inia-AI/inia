using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json.Linq;
using System.Text;

namespace IniaAi.Domain.Tests.Serialization;

public class AgentRunTraceExportTests
{
    private const string ENVELOPE_PATH = "trace/trace.ndjson";
    private const string SUBAGENT_PATH = "trace/sub/agent-abc123.jsonl";
    private const string ARTIFACT_PATH = "out/result.xlsm";

    private static readonly string _ndjson =
        "{\"type\":\"envelope\",\"modelPin\":\"haiku\",\"attempt\":0}\n"
        + "{\"type\":\"tool_call\",\"toolName\":\"Bash\",\"durationMs\":12}\n";

    private static readonly byte[] _artifactBytes = [0, 1, 2, 3, 250, 255];

    [Fact]
    public void ExportWithTraces_TracePackContent_IsReadableJsonArrayNotBase64()
    {
        string exported = BuildWorkflowWithTrace().ToExportJson(includeRunTraces: true);

        JToken content = ContentTokenFor(exported, ENVELOPE_PATH);

        Assert.Equal(JTokenType.Array, content.Type);
        // The array carries the parsed NDJSON lines, so the offline reader sees structured records.
        Assert.Equal("envelope", (string?)content[0]?["type"]);
        Assert.Equal("Bash", (string?)content[1]?["toolName"]);
        // The base64 of the raw bytes must not appear anywhere — that is the opaque shape we replaced.
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(_ndjson)), exported);
    }

    [Fact]
    public void ExportWithTraces_SubagentTranscript_IsReadableJsonArray()
    {
        string exported = BuildWorkflowWithTrace().ToExportJson(includeRunTraces: true);

        Assert.Equal(JTokenType.Array, ContentTokenFor(exported, SUBAGENT_PATH).Type);
    }

    [Fact]
    public void ExportWithTraces_OutArtifactContent_StaysBase64()
    {
        string exported = BuildWorkflowWithTrace().ToExportJson(includeRunTraces: true);

        JToken content = ContentTokenFor(exported, ARTIFACT_PATH);

        Assert.Equal(JTokenType.String, content.Type);
        Assert.Equal(Convert.ToBase64String(_artifactBytes), (string?)content);
    }

    [Fact]
    public void RoundTrip_RebuildsTraceBytesTheReaderCanParse_AndArtifactBytesVerbatim()
    {
        string exported = BuildWorkflowWithTrace().ToExportJson(includeRunTraces: true);

        Workflow? restored = Workflow.FromExportJson(exported);

        Assert.NotNull(restored);
        AgentRunTraceFile envelope = TraceFileFor(restored, ENVELOPE_PATH);
        foreach (string line in Encoding.UTF8.GetString(envelope.Content).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            _ = JToken.Parse(line);
        }

        Assert.Equal("envelope", (string?)JToken.Parse(
            Encoding.UTF8.GetString(envelope.Content).Split('\n')[0])["type"]);
        Assert.Equal(_artifactBytes, TraceFileFor(restored, ARTIFACT_PATH).Content);
    }

    [Fact]
    public void RoundTrip_InlinedTraceRow_StaysInternallyConsistentOnSha()
    {
        string exported = BuildWorkflowWithTrace().ToExportJson(includeRunTraces: true);

        Workflow? restored = Workflow.FromExportJson(exported);

        Assert.NotNull(restored);
        AgentRunTraceFile envelope = TraceFileFor(restored, ENVELOPE_PATH);
        Assert.Equal(envelope.Content.LongLength, envelope.SizeBytes);
        Assert.Equal(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(envelope.Content)),
            envelope.Sha256);
    }

    [Fact]
    public void ExportWithTraces_NonJsonTracePath_FallsBackToBase64()
    {
        Workflow workflow = BuildWorkflow(out Block block);
        block.AgentRunTraceFiles.Add(TraceFile("trace/notes.txt", [.. "not json at all"u8]));

        string exported = workflow.ToExportJson(includeRunTraces: true);

        Assert.Equal(JTokenType.String, ContentTokenFor(exported, "trace/notes.txt").Type);
    }

    [Fact]
    public void RoundTrip_LegacyBase64TraceContent_StillImports()
    {
        // A pack exported before the readable-JSON shaping wrote every content as base64, trace
        // rows included. That shape must still import so old downloads keep opening.
        string legacy = BuildWorkflowWithTrace().ToExportJson(includeRunTraces: true);
        JObject root = JObject.Parse(legacy);
        JToken envelopeContent = root.SelectTokens("$..AgentRunTraceFiles[*]")
            .First(token => (string?)token["Path"] == ENVELOPE_PATH)["Content"]!;
        envelopeContent.Replace(Convert.ToBase64String(Encoding.UTF8.GetBytes(_ndjson)));

        Workflow? restored = Workflow.FromExportJson(root.ToString());

        Assert.NotNull(restored);
        Assert.Equal(_ndjson, Encoding.UTF8.GetString(TraceFileFor(restored, ENVELOPE_PATH).Content));
    }

    [Fact]
    public void RoundTrip_KeepsWhenTheTraceWasCollected_InUtc()
    {
        var collectedAt = new DateTime(2024, 3, 12, 10, 0, 0, DateTimeKind.Utc);
        Workflow workflow = BuildWorkflow(out Block block);
        AgentRunTraceFile trace = TraceFile(ENVELOPE_PATH, Encoding.UTF8.GetBytes(_ndjson));
        trace.CollectedAt = collectedAt;
        block.AgentRunTraceFiles.Add(trace);

        Workflow? restored = Workflow.FromExportJson(workflow.ToExportJson(includeRunTraces: true));

        Assert.NotNull(restored);
        DateTime restoredAt = TraceFileFor(restored, ENVELOPE_PATH).CollectedAt;
        Assert.Equal(collectedAt, restoredAt);
        Assert.Equal(DateTimeKind.Utc, restoredAt.Kind);
    }

    [Fact]
    public void RoundTrip_KeepsADateInATraceLineAsWritten()
    {
        const string datedLine = "{\"type\":\"envelope\",\"startedAt\":\"2024-03-12T10:15:00.000+02:00\"}";
        Workflow workflow = BuildWorkflow(out Block block);
        block.AgentRunTraceFiles.Add(TraceFile(ENVELOPE_PATH, Encoding.UTF8.GetBytes(datedLine)));

        Workflow? restored = Workflow.FromExportJson(workflow.ToExportJson(includeRunTraces: true));

        Assert.NotNull(restored);
        Assert.Equal(datedLine, Encoding.UTF8.GetString(TraceFileFor(restored, ENVELOPE_PATH).Content));
    }

    private static Workflow BuildWorkflowWithTrace()
    {
        Workflow workflow = BuildWorkflow(out Block block);
        block.AgentRunTraceFiles.Add(TraceFile(ENVELOPE_PATH, Encoding.UTF8.GetBytes(_ndjson)));
        block.AgentRunTraceFiles.Add(TraceFile(SUBAGENT_PATH, Encoding.UTF8.GetBytes(_ndjson)));
        block.AgentRunTraceFiles.Add(TraceFile(ARTIFACT_PATH, _artifactBytes));
        return workflow;
    }

    private static Workflow BuildWorkflow(out Block block)
    {
        var workflow = new Workflow("Report") { WorkflowTypeId = "t" };
        var section = new Section("Body", workflow);
        block = new Block(BlockTypes.Agent, section, "block1") { Name = "Agent" };
        return workflow;
    }

    private static AgentRunTraceFile TraceFile(string path, byte[] content)
    {
        return new AgentRunTraceFile
        {
            OrganizationId = "org",
            BlockId = "block1",
            RunExecutionId = "run1",
            Path = path,
            SizeBytes = content.LongLength,
            Sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content)),
            Content = content,
        };
    }

    private static JToken ContentTokenFor(string exported, string path)
    {
        return JObject.Parse(exported)
            .SelectTokens("$..AgentRunTraceFiles[*]")
            .First(token => (string?)token["Path"] == path)["Content"]!;
    }

    private static AgentRunTraceFile TraceFileFor(Workflow workflow, string path)
    {
        return workflow.Sections
            .SelectMany(static section => section.Blocks)
            .SelectMany(static block => block.AgentRunTraceFiles)
            .First(file => file.Path == path);
    }
}
