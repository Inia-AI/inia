using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApp.Components.WorkflowEditor.Common;
using System.Text;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

public class AgentTraceDigestTests
{
    private const string FIRST_PASS_RUN_ID = "run-first-pass";
    private const string REDO_RUN_ID = "run-redo";
    private const string ARTIFACT_FILE_PATH = "out/answer.md";

    private static AgentRunTraceFile TraceFile(string path, params string[] lines)
    {
        return new AgentRunTraceFile
        {
            Path = path,
            Content = Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n"),
        };
    }

    [Fact]
    public void Parse_WithoutTheEnvelopeFile_ReturnsNull()
    {
        AgentRunTraceFile subagentOnly = TraceFile("trace/sub/agent-1.jsonl", """{"type":"message"}""");

        Assert.Null(AgentTraceDigest.Parse([subagentOnly]));
    }

    [Fact]
    public void Parse_BuildsTheHeaderFromEnvelopeUsageAndResult()
    {
        AgentRunTraceFile trace = TraceFile(
            AgentTraceDigest.ENVELOPE_FILE_PATH,
            """{"uuid":"1","at":"2026-08-11T10:00:00Z","type":"envelope","modelPin":"claude-sonnet-4-6","attempt":2}""",
            """{"uuid":"2","at":"2026-08-11T10:00:01Z","type":"usage","messageId":"m1","model":"claude-sonnet-4-6","usage":{"input_tokens":100,"cache_creation_input_tokens":500,"cache_read_input_tokens":2000,"output_tokens":40}}""",
            """{"uuid":"3","at":"2026-08-11T10:00:02Z","type":"usage","messageId":"m2","model":"claude-sonnet-4-6","usage":{"input_tokens":25,"output_tokens":10}}""",
            """{"uuid":"4","at":"2026-08-11T10:00:03Z","type":"message","seq":7,"message":{"type":"result","subtype":"success","num_turns":7,"total_cost_usd":1.23}}""");

        AgentTraceDigest? digest = AgentTraceDigest.Parse([trace]);

        Assert.NotNull(digest);
        Assert.Equal("claude-sonnet-4-6", digest.Model);
        Assert.Equal(2, digest.Attempt);
        Assert.Equal(7, digest.Turns);
        Assert.Equal(2625, digest.InputTokens);
        Assert.Equal(2000, digest.CachedInputTokens);
        Assert.Equal(50, digest.OutputTokens);
        Assert.Equal(1.23m, digest.TotalCostUsd);
        Assert.Equal("success", digest.StopReason);
        Assert.Equal(0, digest.SkippedLines);
    }

    [Fact]
    public void Parse_MergesToolCallsAndStagesChronologically()
    {
        AgentRunTraceFile trace = TraceFile(
            AgentTraceDigest.ENVELOPE_FILE_PATH,
            """{"uuid":"1","at":"2026-08-11T10:00:00Z","type":"envelope","modelPin":"m","attempt":1}""",
            """{"uuid":"2","at":"2026-08-11T10:00:05Z","type":"tool_call","toolUseId":"t1","toolName":"Bash","inputTruncated":"{}","outputBytes":2048,"durationMs":30,"errorClass":null}""",
            """{"uuid":"3","at":"2026-08-11T10:00:01Z","type":"stage","event":"started","executionId":"e","attempt":1,"detail":{}}""",
            """{"uuid":"4","at":"2026-08-11T10:00:09Z","type":"stage","event":"settled","executionId":"e","attempt":1,"detail":{"status":"succeeded"}}""");

        AgentTraceDigest? digest = AgentTraceDigest.Parse([trace]);

        Assert.NotNull(digest);
        Assert.Equal(3, digest.Timeline.Count);
        Assert.Equal("started", digest.Timeline[0].Label);
        Assert.Equal(AgentTraceTimelineKind.Stage, digest.Timeline[0].Kind);
        Assert.Equal("Bash", digest.Timeline[1].Label);
        Assert.Equal(AgentTraceTimelineKind.ToolCall, digest.Timeline[1].Kind);
        Assert.Contains("30 ms", digest.Timeline[1].Detail);
        Assert.Equal("settled", digest.Timeline[2].Label);
        Assert.Contains("succeeded", digest.Timeline[2].Detail);
    }

    [Fact]
    public void Parse_ToolCallErrors_SurfaceInTheDetail()
    {
        AgentRunTraceFile trace = TraceFile(
            AgentTraceDigest.ENVELOPE_FILE_PATH,
            """{"uuid":"1","at":"2026-08-11T10:00:00Z","type":"envelope","modelPin":"m","attempt":1}""",
            """{"uuid":"2","at":"2026-08-11T10:00:05Z","type":"tool_call","toolUseId":"t1","toolName":"Read","inputTruncated":"{}","outputBytes":10,"durationMs":5,"errorClass":"tool_error"}""");

        AgentTraceDigest? digest = AgentTraceDigest.Parse([trace]);

        Assert.NotNull(digest);
        AgentTraceTimelineEntry entry = Assert.Single(digest.Timeline);
        Assert.Contains("tool_error", entry.Detail);
    }

    [Fact]
    public void Parse_BashToolCall_PrefersTheAgentsDescription()
    {
        AgentRunTraceFile trace = TraceFile(
            AgentTraceDigest.ENVELOPE_FILE_PATH,
            """{"uuid":"1","at":"2026-08-11T10:00:00Z","type":"envelope","modelPin":"m","attempt":1}""",
            """{"uuid":"2","at":"2026-08-11T10:00:05Z","type":"tool_call","toolUseId":"t1","toolName":"Bash","inputTruncated":"{\"command\":\"find . -name '*.md' | head -20\",\"description\":\"Find skill files\"}","outputBytes":26,"durationMs":71,"errorClass":null}""");

        AgentTraceDigest? digest = AgentTraceDigest.Parse([trace]);

        Assert.NotNull(digest);
        AgentTraceTimelineEntry entry = Assert.Single(digest.Timeline);
        Assert.NotNull(entry.Detail);
        Assert.Contains("Find skill files", entry.Detail);
        Assert.Contains("71 ms", entry.Detail);
        Assert.DoesNotContain("find . -name", entry.Detail);
    }

    [Fact]
    public void Parse_ReadToolCall_ShowsThePathAndRange()
    {
        AgentRunTraceFile trace = TraceFile(
            AgentTraceDigest.ENVELOPE_FILE_PATH,
            """{"uuid":"1","at":"2026-08-11T10:00:00Z","type":"envelope","modelPin":"m","attempt":1}""",
            """{"uuid":"2","at":"2026-08-11T10:00:05Z","type":"tool_call","toolUseId":"t1","toolName":"Read","inputTruncated":"{\"file_path\":\"docs/report.pdf\",\"offset\":10,\"limit\":40}","outputBytes":169,"durationMs":3,"errorClass":null}""");

        AgentTraceDigest? digest = AgentTraceDigest.Parse([trace]);

        Assert.NotNull(digest);
        AgentTraceTimelineEntry entry = Assert.Single(digest.Timeline);
        Assert.NotNull(entry.Detail);
        Assert.Contains("docs/report.pdf", entry.Detail);
        Assert.Contains("offset 10", entry.Detail);
        Assert.Contains("limit 40", entry.Detail);
    }

    [Fact]
    public void Parse_OtherToolCall_ShowsTheCompactInputJson()
    {
        AgentRunTraceFile trace = TraceFile(
            AgentTraceDigest.ENVELOPE_FILE_PATH,
            """{"uuid":"1","at":"2026-08-11T10:00:00Z","type":"envelope","modelPin":"m","attempt":1}""",
            """{"uuid":"2","at":"2026-08-11T10:00:05Z","type":"tool_call","toolUseId":"t1","toolName":"Grep","inputTruncated":"{\"pattern\":\"MARKER\",\"path\":\".\"}","outputBytes":80,"durationMs":9,"errorClass":null}""");

        AgentTraceDigest? digest = AgentTraceDigest.Parse([trace]);

        Assert.NotNull(digest);
        AgentTraceTimelineEntry entry = Assert.Single(digest.Timeline);
        Assert.NotNull(entry.Detail);
        Assert.Contains("\"pattern\":\"MARKER\"", entry.Detail);
    }

    [Fact]
    public void Parse_InputCutMidJsonByTheRuntimeCap_ShowsTheRawTextInsteadOfHidingIt()
    {
        AgentRunTraceFile trace = TraceFile(
            AgentTraceDigest.ENVELOPE_FILE_PATH,
            """{"uuid":"1","at":"2026-08-11T10:00:00Z","type":"envelope","modelPin":"m","attempt":1}""",
            """{"uuid":"2","at":"2026-08-11T10:00:05Z","type":"tool_call","toolUseId":"t1","toolName":"Bash","inputTruncated":"{\"command\":\"cat << 'EOF' > out.txt\\nlong content that got cu","outputBytes":0,"durationMs":12,"errorClass":null}""");

        AgentTraceDigest? digest = AgentTraceDigest.Parse([trace]);

        Assert.NotNull(digest);
        AgentTraceTimelineEntry entry = Assert.Single(digest.Timeline);
        Assert.NotNull(entry.Detail);
        Assert.Contains("long content that got cu", entry.Detail);
    }

    [Fact]
    public void Parse_AssistantThinkingAndProse_LandInTheTimelineInOrder()
    {
        AgentRunTraceFile trace = TraceFile(
            AgentTraceDigest.ENVELOPE_FILE_PATH,
            """{"uuid":"1","at":"2026-08-11T10:00:00Z","type":"envelope","modelPin":"m","attempt":1}""",
            """{"uuid":"2","at":"2026-08-11T10:00:03Z","type":"message","seq":1,"message":{"type":"assistant","message":{"role":"assistant","content":[{"type":"thinking","thinking":"The marker sits in the report body.","signature":"sig"},{"type":"text","text":"Reading the document now."},{"type":"tool_use","id":"t1","name":"Read","input":{}}]}}}""",
            """{"uuid":"3","at":"2026-08-11T10:00:04Z","type":"message","seq":2,"message":{"type":"system","subtype":"thinking_tokens","estimated_tokens":8}}""");

        AgentTraceDigest? digest = AgentTraceDigest.Parse([trace]);

        Assert.NotNull(digest);
        Assert.Equal(2, digest.Timeline.Count);
        Assert.Equal(AgentTraceTimelineKind.Thinking, digest.Timeline[0].Kind);
        Assert.Equal("thinking", digest.Timeline[0].Label);
        Assert.Equal("The marker sits in the report body.", digest.Timeline[0].Detail);
        Assert.Equal(AgentTraceTimelineKind.AssistantText, digest.Timeline[1].Kind);
        Assert.Equal("assistant", digest.Timeline[1].Label);
        Assert.Equal("Reading the document now.", digest.Timeline[1].Detail);
    }

    [Fact]
    public void Parse_EmptyToolInput_LeavesTheMetricsOnlyDetail()
    {
        AgentRunTraceFile trace = TraceFile(
            AgentTraceDigest.ENVELOPE_FILE_PATH,
            """{"uuid":"1","at":"2026-08-11T10:00:00Z","type":"envelope","modelPin":"m","attempt":1}""",
            """{"uuid":"2","at":"2026-08-11T10:00:05Z","type":"tool_call","toolUseId":"t1","toolName":"StructuredOutput","inputTruncated":"{}","outputBytes":41,"durationMs":4,"errorClass":null}""");

        AgentTraceDigest? digest = AgentTraceDigest.Parse([trace]);

        Assert.NotNull(digest);
        AgentTraceTimelineEntry entry = Assert.Single(digest.Timeline);
        Assert.Equal("4 ms · 41 B", entry.Detail);
    }

    [Fact]
    public void Parse_WithEnvelopesFromTwoRuns_ReadsTheLatestCollected()
    {
        AgentRunTraceFile stale = TraceFile(
            AgentTraceDigest.ENVELOPE_FILE_PATH,
            """{"uuid":"1","at":"2026-08-10T10:00:00Z","type":"envelope","modelPin":"stale-model","attempt":1}""");
        stale.CollectedAt = new DateTime(2026, 8, 10, 10, 0, 30, DateTimeKind.Utc);
        AgentRunTraceFile fresh = TraceFile(
            AgentTraceDigest.ENVELOPE_FILE_PATH,
            """{"uuid":"2","at":"2026-08-11T10:00:00Z","type":"envelope","modelPin":"fresh-model","attempt":1}""");
        fresh.CollectedAt = new DateTime(2026, 8, 11, 10, 0, 30, DateTimeKind.Utc);

        AgentTraceDigest? digest = AgentTraceDigest.Parse([stale, fresh]);

        Assert.NotNull(digest);
        Assert.Equal("fresh-model", digest.Model);
    }

    [Fact]
    public void Parse_MalformedLines_AreCountedNotFatal()
    {
        AgentRunTraceFile trace = TraceFile(
            AgentTraceDigest.ENVELOPE_FILE_PATH,
            "this is not json",
            """{"uuid":"1","at":"2026-08-11T10:00:00Z","type":"envelope","modelPin":"m","attempt":1}""");

        AgentTraceDigest? digest = AgentTraceDigest.Parse([trace]);

        Assert.NotNull(digest);
        Assert.Equal("m", digest.Model);
        Assert.Equal(1, digest.SkippedLines);
    }

    [Fact]
    public void Parse_ResultExtras_CarryLatencySplitModelUsageAndDenials()
    {
        AgentRunTraceFile trace = TraceFile(
            AgentTraceDigest.ENVELOPE_FILE_PATH,
            """{"uuid":"1","at":"2026-08-11T10:00:00Z","type":"envelope","modelPin":"m","attempt":1}""",
            """{"uuid":"2","at":"2026-08-11T10:00:05Z","type":"tool_call","toolUseId":"t1","toolName":"Bash","inputTruncated":"{}","outputBytes":10,"durationMs":700,"errorClass":null}""",
            """{"uuid":"3","at":"2026-08-11T10:00:09Z","type":"message","seq":9,"message":{"type":"result","subtype":"success","num_turns":3,"total_cost_usd":0.5,"duration_ms":45800,"duration_api_ms":31620,"modelUsage":{"haiku-4-5":{"inputTokens":400,"outputTokens":2800,"cacheReadInputTokens":39650,"costUSD":0.067},"sonnet-4-6":{"inputTokens":90,"outputTokens":120,"cacheReadInputTokens":0,"costUSD":0.433}},"permission_denials":[{"tool_name":"Write","tool_use_id":"t9","tool_input":{"file_path":"/etc/passwd"}}]}}""");

        AgentTraceDigest? digest = AgentTraceDigest.Parse([trace]);

        Assert.NotNull(digest);
        Assert.Equal(45800, digest.WallDurationMs);
        Assert.Equal(31620, digest.ApiDurationMs);
        Assert.Equal(700, digest.ToolTimeMs);
        Assert.Equal(2, digest.ModelUsages.Count);
        AgentTraceModelUsage first = digest.ModelUsages[0];
        Assert.Equal("haiku-4-5", first.Model);
        Assert.Equal(400, first.InputTokens);
        Assert.Equal(39650, first.CacheReadInputTokens);
        Assert.Equal(0.067m, first.CostUsd);
        AgentTracePermissionDenial denial = Assert.Single(digest.PermissionDenials);
        Assert.Equal("Write", denial.Tool);
        Assert.NotNull(denial.InputSummary);
        Assert.Contains("/etc/passwd", denial.InputSummary);
    }

    [Fact]
    public void Parse_InitMessage_CarriesTheRunEnvironment()
    {
        AgentRunTraceFile trace = TraceFile(
            AgentTraceDigest.ENVELOPE_FILE_PATH,
            """{"uuid":"1","at":"2026-08-11T10:00:00Z","type":"envelope","modelPin":"m","attempt":1}""",
            """{"uuid":"2","at":"2026-08-11T10:00:01Z","type":"message","seq":0,"message":{"type":"system","subtype":"init","tools":["Bash","Read"],"skills":["income-statement"],"permissionMode":"dontAsk","claude_code_version":"2.1.220","cwd":"/workspaces/run-x"}}""");

        AgentTraceDigest? digest = AgentTraceDigest.Parse([trace]);

        Assert.NotNull(digest);
        Assert.Equal(["Bash", "Read"], digest.AvailableTools);
        Assert.Equal(["income-statement"], digest.LoadedSkills);
        Assert.Equal("dontAsk", digest.PermissionMode);
        Assert.Equal("2.1.220", digest.ClaudeCodeVersion);
    }

    [Fact]
    public void Parse_EnvelopeSkills_ShowTheRunsSkillsNotEverySkillTheCliFound()
    {
        AgentRunTraceFile trace = TraceFile(
            AgentTraceDigest.ENVELOPE_FILE_PATH,
            """{"uuid":"1","at":"2026-10-22T11:54:29Z","type":"envelope","modelPin":"m","attempt":1,"skills":["skills:run-a-command"]}""",
            """{"uuid":"2","at":"2026-10-22T11:54:30Z","type":"message","seq":0,"message":{"type":"system","subtype":"init","tools":["Bash","Read","Skill"],"skills":["deep-research","skills:run-a-command","claude-api","loop"]}}""");

        AgentTraceDigest? digest = AgentTraceDigest.Parse([trace]);

        Assert.NotNull(digest);
        Assert.Equal(["skills:run-a-command"], digest.LoadedSkills);
    }

    [Fact]
    public void Parse_EnvelopeWithNoSkills_ShowsNoneEvenWhenTheCliFoundSome()
    {
        AgentRunTraceFile trace = TraceFile(
            AgentTraceDigest.ENVELOPE_FILE_PATH,
            """{"uuid":"1","at":"2026-10-22T11:54:29Z","type":"envelope","modelPin":"m","attempt":1,"skills":[]}""",
            """{"uuid":"2","at":"2026-10-22T11:54:30Z","type":"message","seq":0,"message":{"type":"system","subtype":"init","tools":["Bash","Read"],"skills":["deep-research","claude-api"]}}""");

        AgentTraceDigest? digest = AgentTraceDigest.Parse([trace]);

        Assert.NotNull(digest);
        Assert.Empty(digest.LoadedSkills);
    }

    [Fact]
    public void Parse_OverlongThinking_IsCappedPerRow()
    {
        string longThinking = new('x', 6_000);
        AgentRunTraceFile trace = TraceFile(
            AgentTraceDigest.ENVELOPE_FILE_PATH,
            """{"uuid":"1","at":"2026-08-11T10:00:00Z","type":"envelope","modelPin":"m","attempt":1}""",
            """{"uuid":"2","at":"2026-08-11T10:00:03Z","type":"message","seq":1,"message":{"type":"assistant","message":{"role":"assistant","content":[{"type":"thinking","thinking":"THINKING_PAYLOAD","signature":"sig"}]}}}"""
                .Replace("THINKING_PAYLOAD", longThinking));

        AgentTraceDigest? digest = AgentTraceDigest.Parse([trace]);

        Assert.NotNull(digest);
        AgentTraceTimelineEntry entry = Assert.Single(digest.Timeline);
        Assert.NotNull(entry.Detail);
        Assert.EndsWith("… (truncated)", entry.Detail);
        Assert.Equal(4_000, entry.Detail.Length);
    }

    [Fact]
    public void Parse_BashToolCall_WithoutADescription_FallsBackToTheCommand()
    {
        AgentRunTraceFile trace = TraceFile(
            AgentTraceDigest.ENVELOPE_FILE_PATH,
            """{"uuid":"1","at":"2026-08-11T10:00:00Z","type":"envelope","modelPin":"m","attempt":1}""",
            """{"uuid":"2","at":"2026-08-11T10:00:05Z","type":"tool_call","toolUseId":"t1","toolName":"Bash","inputTruncated":"{\"command\":\"grep -r MARKER .\"}","outputBytes":26,"durationMs":40,"errorClass":null}""");

        AgentTraceDigest? digest = AgentTraceDigest.Parse([trace]);

        Assert.NotNull(digest);
        AgentTraceTimelineEntry entry = Assert.Single(digest.Timeline);
        Assert.NotNull(entry.Detail);
        Assert.Contains("grep -r MARKER .", entry.Detail);
    }

    [Fact]
    public void Parse_SubagentTranscript_RendersItsTimelineLabeledByTheAgent()
    {
        AgentRunTraceFile envelope = TraceFile(
            AgentTraceDigest.ENVELOPE_FILE_PATH,
            """{"uuid":"1","at":"2026-08-11T10:00:00Z","type":"envelope","modelPin":"m","attempt":1}""");
        AgentRunTraceFile sub = TraceFile(
            "trace/sub/agent-researcher.jsonl",
            """{"type":"assistant","timestamp":"2026-08-11T10:00:03Z","message":{"role":"assistant","content":[{"type":"thinking","thinking":"Where is the balance sheet?","signature":"s"},{"type":"text","text":"Opening the filing."},{"type":"tool_use","id":"u1","name":"Bash","input":{"command":"ls out","description":"List the working set"}}]}}""",
            """{"type":"user","timestamp":"2026-08-11T10:00:04Z","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"u1","content":"notes.txt"}]}}""");

        AgentTraceDigest? digest = AgentTraceDigest.Parse([envelope, sub]);

        Assert.NotNull(digest);
        Assert.Empty(digest.Timeline);
        AgentTraceSubagentTimeline group = Assert.Single(digest.SubagentTimelines);
        Assert.Equal("researcher", group.Agent);
        Assert.Equal(3, group.Timeline.Count);
        Assert.Equal(AgentTraceTimelineKind.Thinking, group.Timeline[0].Kind);
        Assert.Equal(AgentTraceTimelineKind.AssistantText, group.Timeline[1].Kind);
        Assert.Equal(AgentTraceTimelineKind.ToolCall, group.Timeline[2].Kind);
        Assert.Equal("Bash", group.Timeline[2].Label);
        Assert.NotNull(group.Timeline[2].Detail);
        Assert.Contains("List the working set", group.Timeline[2].Detail);
    }

    [Fact]
    public void Parse_SubagentFileWithoutRenderableContent_AddsNoGroup()
    {
        AgentRunTraceFile envelope = TraceFile(
            AgentTraceDigest.ENVELOPE_FILE_PATH,
            """{"uuid":"1","at":"2026-08-11T10:00:00Z","type":"envelope","modelPin":"m","attempt":1}""");
        AgentRunTraceFile emptySub = TraceFile("trace/sub/agent-1.jsonl", """{"type":"user"}""");

        AgentTraceDigest? digest = AgentTraceDigest.Parse([envelope, emptySub]);

        Assert.NotNull(digest);
        Assert.Empty(digest.SubagentTimelines);
    }

    [Fact]
    public void Parse_MultipleSubagents_EachGetsItsOwnGroupOrderedByAgent()
    {
        AgentRunTraceFile envelope = TraceFile(
            AgentTraceDigest.ENVELOPE_FILE_PATH,
            """{"uuid":"1","at":"2026-08-11T10:00:00Z","type":"envelope","modelPin":"m","attempt":1}""");
        AgentRunTraceFile arbiter = TraceFile(
            "trace/sub/agent-arbiter.jsonl",
            """{"type":"assistant","timestamp":"2026-08-11T10:01:00Z","message":{"role":"assistant","content":[{"type":"text","text":"Reconciling."}]}}""");
        AgentRunTraceFile extractor = TraceFile(
            "trace/sub/agent-extractor.jsonl",
            """{"type":"assistant","timestamp":"2026-08-11T10:00:30Z","message":{"role":"assistant","content":[{"type":"text","text":"Pulling figures."}]}}""");

        AgentTraceDigest? digest = AgentTraceDigest.Parse([envelope, arbiter, extractor]);

        Assert.NotNull(digest);
        Assert.Equal(2, digest.SubagentTimelines.Count);
        Assert.Equal("arbiter", digest.SubagentTimelines[0].Agent);
        Assert.Equal("extractor", digest.SubagentTimelines[1].Agent);
    }

    [Fact]
    public void Parse_SubagentPathWithEmptyId_AddsNoGroup()
    {
        AgentRunTraceFile envelope = TraceFile(
            AgentTraceDigest.ENVELOPE_FILE_PATH,
            """{"uuid":"1","at":"2026-08-11T10:00:00Z","type":"envelope","modelPin":"m","attempt":1}""");
        AgentRunTraceFile emptyId = TraceFile(
            "trace/sub/agent-.jsonl",
            """{"type":"assistant","timestamp":"2026-08-11T10:00:03Z","message":{"role":"assistant","content":[{"type":"text","text":"Orphan work."}]}}""");

        AgentTraceDigest? digest = AgentTraceDigest.Parse([envelope, emptyId]);

        Assert.NotNull(digest);
        Assert.Empty(digest.SubagentTimelines);
    }

    [Fact]
    public void Parse_TwoFilesForOneSubagent_UsesTheNewestCollected()
    {
        AgentRunTraceFile envelope = TraceFile(
            AgentTraceDigest.ENVELOPE_FILE_PATH,
            """{"uuid":"1","at":"2026-08-11T10:00:00Z","type":"envelope","modelPin":"m","attempt":1}""");
        AgentRunTraceFile stale = TraceFile(
            "trace/sub/agent-1.jsonl",
            """{"type":"assistant","timestamp":"2026-08-11T10:00:03Z","message":{"role":"assistant","content":[{"type":"text","text":"stale attempt"}]}}""");
        stale.CollectedAt = new DateTime(2026, 8, 11, 10, 0, 30, DateTimeKind.Utc);
        AgentRunTraceFile fresh = TraceFile(
            "trace/sub/agent-1.jsonl",
            """{"type":"assistant","timestamp":"2026-08-11T10:00:04Z","message":{"role":"assistant","content":[{"type":"text","text":"fresh attempt"}]}}""");
        fresh.CollectedAt = new DateTime(2026, 8, 11, 11, 0, 30, DateTimeKind.Utc);

        AgentTraceDigest? digest = AgentTraceDigest.Parse([envelope, stale, fresh]);

        Assert.NotNull(digest);
        AgentTraceSubagentTimeline group = Assert.Single(digest.SubagentTimelines);
        AgentTraceTimelineEntry entry = Assert.Single(group.Timeline);
        Assert.Equal("fresh attempt", entry.Detail);
    }

    [Fact]
    public void Parse_BashToolCall_WithABlankDescription_FallsBackToTheCommand()
    {
        AgentRunTraceFile trace = TraceFile(
            AgentTraceDigest.ENVELOPE_FILE_PATH,
            """{"uuid":"1","at":"2026-08-11T10:00:00Z","type":"envelope","modelPin":"m","attempt":1}""",
            """{"uuid":"2","at":"2026-08-11T10:00:05Z","type":"tool_call","toolUseId":"t1","toolName":"Bash","inputTruncated":"{\"command\":\"ls -la\",\"description\":\"   \"}","outputBytes":26,"durationMs":40,"errorClass":null}""");

        AgentTraceDigest? digest = AgentTraceDigest.Parse([trace]);

        Assert.NotNull(digest);
        AgentTraceTimelineEntry entry = Assert.Single(digest.Timeline);
        Assert.NotNull(entry.Detail);
        Assert.Contains("ls -la", entry.Detail);
    }

    private static AgentRunTraceFile RunPackFile(
        string runExecutionId,
        int attempt,
        DateTime collectedAt,
        string path,
        params string[] lines)
    {
        AgentRunTraceFile file = TraceFile(path, lines);
        file.RunExecutionId = runExecutionId;
        file.Attempt = attempt;
        file.CollectedAt = collectedAt;

        return file;
    }

    private static List<AgentRunTraceFile> FirstPassPack()
    {
        DateTime collectedAt = new(2026, 8, 11, 10, 5, 0, DateTimeKind.Utc);

        return
        [
            RunPackFile(
                FIRST_PASS_RUN_ID,
                1,
                collectedAt,
                AgentTraceDigest.ENVELOPE_FILE_PATH,
                """{"uuid":"1","at":"2026-08-11T10:00:00Z","type":"envelope","modelPin":"claude-sonnet-4-6","attempt":1}""",
                """{"uuid":"2","at":"2026-08-11T10:00:01Z","type":"usage","messageId":"m1","model":"claude-sonnet-4-6","usage":{"input_tokens":100,"cache_creation_input_tokens":0,"cache_read_input_tokens":900,"output_tokens":40}}""",
                """{"uuid":"3","at":"2026-08-11T10:00:02Z","type":"stage","event":"started","executionId":"e","attempt":1,"detail":{}}""",
                """{"uuid":"4","at":"2026-08-11T10:00:03Z","type":"tool_call","toolUseId":"t1","toolName":"Bash","inputTruncated":"{}","outputBytes":12,"durationMs":40,"errorClass":null}""",
                """{"uuid":"5","at":"2026-08-11T10:00:04Z","type":"message","seq":5,"message":{"type":"result","subtype":"success","num_turns":3,"total_cost_usd":0.25}}"""),
            RunPackFile(FIRST_PASS_RUN_ID, 1, collectedAt, ARTIFACT_FILE_PATH, "the member's first answer"),
        ];
    }

    /// <summary>
    /// The redo the verifier bought in a later round, collected after the first pass — which is
    /// what decides the leg a combined parse resolves the shared envelope path to.
    /// </summary>
    private static List<AgentRunTraceFile> RedoPack()
    {
        DateTime collectedAt = new(2026, 8, 11, 11, 30, 0, DateTimeKind.Utc);

        return
        [
            RunPackFile(
                REDO_RUN_ID,
                2,
                collectedAt,
                AgentTraceDigest.ENVELOPE_FILE_PATH,
                """{"uuid":"1","at":"2026-08-11T11:00:00Z","type":"envelope","modelPin":"claude-opus-4-6","attempt":2}""",
                """{"uuid":"2","at":"2026-08-11T11:00:01Z","type":"usage","messageId":"m1","model":"claude-opus-4-6","usage":{"input_tokens":700,"cache_creation_input_tokens":300,"cache_read_input_tokens":4000,"output_tokens":90}}""",
                """{"uuid":"3","at":"2026-08-11T11:00:02Z","type":"stage","event":"started","executionId":"e","attempt":2,"detail":{}}""",
                """{"uuid":"4","at":"2026-08-11T11:00:03Z","type":"tool_call","toolUseId":"t2","toolName":"Read","inputTruncated":"{}","outputBytes":80,"durationMs":250,"errorClass":null}""",
                """{"uuid":"5","at":"2026-08-11T11:00:04Z","type":"stage","event":"settled","executionId":"e","attempt":2,"detail":{"status":"succeeded"}}""",
                """{"uuid":"6","at":"2026-08-11T11:00:05Z","type":"message","seq":6,"message":{"type":"result","subtype":"error_max_turns","num_turns":9,"total_cost_usd":1.75}}"""),
            RunPackFile(REDO_RUN_ID, 2, collectedAt, ARTIFACT_FILE_PATH, "the redone answer"),
        ];
    }

    /// <summary>
    /// Why the panel parses per run. Every pack of a redone member names its files identically, so
    /// handing both runs' files to one Parse call resolves the single envelope path to the
    /// newest-collected pack and reports that leg alone as if it were the whole block: the redo's
    /// attempt, model, stop reason, tokens and $1.75 stand in for a block that actually ran twice
    /// and cost $2.00, and the first pass leaves no trace on screen at all.
    /// </summary>
    [Fact]
    public void Parse_EveryRunsFilesAtOnce_ReportsTheNewestLegAloneAsTheWholeBlock()
    {
        List<AgentRunTraceFile> everyFile = [.. FirstPassPack(), .. RedoPack()];

        AgentTraceDigest? digest = AgentTraceDigest.Parse(everyFile);

        Assert.NotNull(digest);
        Assert.Equal("claude-opus-4-6", digest.Model);
        Assert.Equal(2, digest.Attempt);
        Assert.Equal(9, digest.Turns);
        Assert.Equal("error_max_turns", digest.StopReason);
        Assert.Equal(1.75m, digest.TotalCostUsd);
        Assert.Equal(5000, digest.InputTokens);
        Assert.Equal(4000, digest.CachedInputTokens);
        Assert.Equal(90, digest.OutputTokens);
        Assert.Equal(250, digest.ToolTimeMs);
        Assert.Equal(3, digest.Timeline.Count);
        Assert.DoesNotContain(digest.Timeline, entry => entry.Label == "Bash");
    }

    /// <summary>
    /// The split the panel makes: the same files grouped by their run before parsing give one
    /// digest per leg, each figure belonging to the run that earned it, and a spend the reader can
    /// add up to what the block was actually billed. Goes through the panel's own splitter, so a
    /// panel that stopped splitting fails here.
    /// </summary>
    [Fact]
    public void Split_PerRunPacks_GiveEachLegItsOwnFigures()
    {
        List<AgentRunTraceFile> everyFile = [.. FirstPassPack(), .. RedoPack()];

        List<AgentTracePanelRun> runs = AgentTracePanelRuns.Split(everyFile, passes: null);
        List<AgentTraceDigest?> digests = [.. runs.Select(static run => run.Digest)];

        Assert.Equal(2, digests.Count);
        Assert.Equal([FIRST_PASS_RUN_ID, REDO_RUN_ID], runs.Select(static run => run.RunExecutionId));

        AgentTraceDigest? firstPass = digests[0];
        Assert.NotNull(firstPass);
        Assert.Equal("claude-sonnet-4-6", firstPass.Model);
        Assert.Equal(1, firstPass.Attempt);
        Assert.Equal(3, firstPass.Turns);
        Assert.Equal("success", firstPass.StopReason);
        Assert.Equal(0.25m, firstPass.TotalCostUsd);
        Assert.Equal(1000, firstPass.InputTokens);
        Assert.Equal(900, firstPass.CachedInputTokens);
        Assert.Equal(40, firstPass.OutputTokens);
        Assert.Equal(40, firstPass.ToolTimeMs);
        Assert.Equal(2, firstPass.Timeline.Count);
        Assert.Contains(firstPass.Timeline, entry => entry.Label == "Bash");

        AgentTraceDigest? redo = digests[1];
        Assert.NotNull(redo);
        Assert.Equal("claude-opus-4-6", redo.Model);
        Assert.Equal(2, redo.Attempt);
        Assert.Equal(9, redo.Turns);
        Assert.Equal("error_max_turns", redo.StopReason);
        Assert.Equal(1.75m, redo.TotalCostUsd);
        Assert.Equal(5000, redo.InputTokens);
        Assert.Equal(4000, redo.CachedInputTokens);
        Assert.Equal(90, redo.OutputTokens);
        Assert.Equal(250, redo.ToolTimeMs);
        Assert.Equal(3, redo.Timeline.Count);
        Assert.DoesNotContain(redo.Timeline, entry => entry.Label == "Bash");

        Assert.Equal(2.00m, digests.Sum(digest => digest?.TotalCostUsd ?? 0m));
    }

    /// <summary>
    /// The ledger is what the round labels and the panel's total come from: the runs sort by the
    /// round that bought them, each header names it, and the title quotes what the runtime
    /// charged rather than what the traces happen to say.
    /// </summary>
    [Fact]
    public void Split_WithTheBlocksRunLedger_LabelsEachRunsRoundAndTotalsTheLedgersSpend()
    {
        List<AgentRunTraceFile> everyFile = [.. RedoPack(), .. FirstPassPack()];
        List<AgentRunPass> passes =
        [
            new() { ExecutionId = REDO_RUN_ID, Round = 2, EstimatedCostUsd = 0.5m },
            new() { ExecutionId = FIRST_PASS_RUN_ID, Round = 1, EstimatedCostUsd = 0.25m },
        ];

        List<AgentTracePanelRun> runs = AgentTracePanelRuns.Split(everyFile, passes);

        Assert.Equal([1, 2], runs.Select(static run => run.Pass!.Round));
        Assert.StartsWith("round 1 · pass 1 · ", AgentTracePanelRuns.Header(runs[0]), StringComparison.Ordinal);
        Assert.StartsWith("round 2 · pass 2 · ", AgentTracePanelRuns.Header(runs[1]), StringComparison.Ordinal);
        Assert.Equal(" · $0.75 total", AgentTracePanelRuns.LedgerSpend(runs));
    }

    /// <summary>
    /// The desk-facing story of a redone member reads off the header alone: a redo leg names
    /// the mode it actually ran (resumed against scratch), and a leg that settled failed names
    /// the runtime's subtype — the shape where a deadline-killed resume leg sits
    /// beside the resumed leg that continued its session.
    /// </summary>
    [Fact]
    public void Header_NamesTheActualRedoModeAndAFailedLegsSubtype()
    {
        List<AgentTracePanelRun> runs = AgentTracePanelRuns.Split(
            [.. RedoPack(), .. FirstPassPack()],
            [
                new() { ExecutionId = FIRST_PASS_RUN_ID, Round = 1, Status = nameof(AgentRunStatus.Succeeded) },
                new()
                {
                    ExecutionId = REDO_RUN_ID,
                    Round = 2,
                    Status = nameof(AgentRunStatus.Failed),
                    FailureSubtype = "deadline_exceeded",
                    ActualRedoMode = nameof(VerificationRedoMode.Revise),
                },
            ]);

        Assert.StartsWith("round 1 · pass 1 · ", AgentTracePanelRuns.Header(runs[0]), StringComparison.Ordinal);
        Assert.StartsWith(
            "round 2 · pass 2 · resumed · failed (deadline_exceeded) · ",
            AgentTracePanelRuns.Header(runs[1]),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A scratch leg says so in the same slot, so resumed and scratch legs of one member read
    /// apart at a glance.
    /// </summary>
    [Fact]
    public void Header_NamesAScratchLegAsScratch()
    {
        List<AgentTracePanelRun> runs = AgentTracePanelRuns.Split(
            RedoPack(),
            [new()
            {
                ExecutionId = REDO_RUN_ID,
                Round = 2,
                Status = nameof(AgentRunStatus.Succeeded),
                ActualRedoMode = nameof(VerificationRedoMode.Scratch),
            }]);

        Assert.StartsWith(
            "round 2 · pass 2 · scratch · ", AgentTracePanelRuns.Header(runs[0]), StringComparison.Ordinal);
    }

    /// <summary>
    /// A block whose consumer holds no ledger still renders: no round to sort or label by, and a
    /// title that says nothing about spend rather than claiming a zero total.
    /// </summary>
    [Fact]
    public void Split_WithNoRunLedger_KeepsTheAttemptOrderAndQuotesNoSpend()
    {
        List<AgentTracePanelRun> runs =
            AgentTracePanelRuns.Split([.. RedoPack(), .. FirstPassPack()], passes: null);

        Assert.All(runs, static run => Assert.Null(run.Pass));
        Assert.Equal([FIRST_PASS_RUN_ID, REDO_RUN_ID], runs.Select(static run => run.RunExecutionId));
        Assert.StartsWith("pass 1 · ", AgentTracePanelRuns.Header(runs[0]), StringComparison.Ordinal);
        Assert.Equal(string.Empty, AgentTracePanelRuns.LedgerSpend(runs));
    }

    [Fact]
    public void Parse_AfterWorkflowExportRoundTrip_ReadsTheReadableJsonTracePack()
    {
        var workflow = new Workflow("Report") { WorkflowTypeId = "t" };
        var section = new Section("Body", workflow);
        var block = new Block(BlockTypes.Agent, section, "block1") { Name = "Agent" };
        block.AgentRunTraceFiles.Add(TraceFile(
            AgentTraceDigest.ENVELOPE_FILE_PATH,
            """{"type":"envelope","modelPin":"claude-sonnet-5","attempt":0}""",
            """{"type":"message","at":"2026-09-03T10:00:00Z","message":{"type":"result","subtype":"success","num_turns":3,"total_cost_usd":0.05}}"""));
        block.AgentRunTraceFiles.Add(TraceFile(
            "trace/sub/agent-xyz.jsonl",
            """{"type":"assistant","timestamp":"2026-09-03T10:00:01Z","message":{"content":[{"type":"tool_use","name":"Bash","input":{"description":"say hi","command":"echo hi"}}]}}"""));

        // The download inlines the pack as readable JSON; the upload rebuilds it — the reader must
        // parse the round-tripped pack exactly as it parses a freshly collected one.
        Workflow? restored = Workflow.FromExportJson(workflow.ToExportJson(includeRunTraces: true));
        Assert.NotNull(restored);
        List<AgentRunTraceFile> files =
            [.. restored.Sections.SelectMany(static s => s.Blocks).SelectMany(static b => b.AgentRunTraceFiles)];

        AgentTraceDigest? digest = AgentTraceDigest.Parse(files);

        Assert.NotNull(digest);
        Assert.Equal("claude-sonnet-5", digest.Model);
        Assert.Equal(3, digest.Turns);
        AgentTraceSubagentTimeline subagent = Assert.Single(digest.SubagentTimelines);
        Assert.Equal("xyz", subagent.Agent);
        Assert.Contains(subagent.Timeline, entry => entry.Detail == "say hi");
    }
}
