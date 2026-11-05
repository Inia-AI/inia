using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Domain.Entities.AgentRuntime;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

public enum AgentTraceTimelineKind
{
    Stage,
    ToolCall,
    Thinking,
    AssistantText,
}

public sealed record AgentTraceTimelineEntry(
    DateTimeOffset? At,
    AgentTraceTimelineKind Kind,
    string Label,
    string? Detail);

public sealed record AgentTraceModelUsage(
    string Model,
    long InputTokens,
    long OutputTokens,
    long CacheReadInputTokens,
    decimal? CostUsd);

public sealed record AgentTracePermissionDenial(string Tool, string? InputSummary);

public sealed record AgentTraceSubagentTimeline(
    string Agent,
    IReadOnlyList<AgentTraceTimelineEntry> Timeline);

/// <summary>
/// The editor panel's parsed view over a collected trace pack: a summary header and a
/// chronological timeline — tool calls, stages, and the agent's own thinking and prose —
/// extracted from the pack's <c>trace/trace.ndjson</c>.
/// Tolerant by design — a malformed line is counted and skipped, never fatal, because the raw
/// files remain the evidence of record and the digest is only a reading aid.
/// </summary>
public sealed class AgentTraceDigest
{
    public const string ENVELOPE_FILE_PATH = "trace/trace.ndjson";

    private const string RESULT_MESSAGE_TYPE = "result";
    private const string ASSISTANT_MESSAGE_TYPE = "assistant";
    private const string SYSTEM_MESSAGE_TYPE = "system";
    private const string INIT_SUBTYPE = "init";
    private const string THINKING_BLOCK_TYPE = "thinking";
    private const string TEXT_BLOCK_TYPE = "text";
    private const string TOOL_USE_BLOCK_TYPE = "tool_use";
    private const string SUBAGENT_PATH_PREFIX = "trace/sub/agent-";
    private const string SUBAGENT_PATH_SUFFIX = ".jsonl";
    private const string THINKING_LABEL = "thinking";
    private const string ASSISTANT_LABEL = "assistant";
    private const string TRUNCATION_SUFFIX = "… (truncated)";

    private const int TEXT_DETAIL_MAX_CHARS = 4_000;

    private static readonly string[] _readRangeKeys = ["offset", "limit", "pages"];

    public string? Model { get; init; }
    public int Attempt { get; init; }
    public int? Turns { get; init; }
    public long InputTokens { get; init; }
    public long CachedInputTokens { get; init; }
    public long OutputTokens { get; init; }
    public decimal? TotalCostUsd { get; init; }
    public string? StopReason { get; init; }
    public long? WallDurationMs { get; init; }
    public long? ApiDurationMs { get; init; }
    public long ToolTimeMs { get; init; }
    public IReadOnlyList<AgentTraceModelUsage> ModelUsages { get; init; } = [];
    public IReadOnlyList<AgentTracePermissionDenial> PermissionDenials { get; init; } = [];
    public IReadOnlyList<string> AvailableTools { get; init; } = [];
    public IReadOnlyList<string> LoadedSkills { get; init; } = [];
    public string? PermissionMode { get; init; }
    public string? ClaudeCodeVersion { get; init; }
    public int SkippedLines { get; init; }
    public IReadOnlyList<AgentTraceTimelineEntry> Timeline { get; init; } = [];
    public IReadOnlyList<AgentTraceSubagentTimeline> SubagentTimelines { get; init; } = [];

    public static AgentTraceDigest? Parse(IEnumerable<AgentRunTraceFile> files)
    {
        AgentRunTraceFile? envelopeFile = files
            .Where(static file => file.Path == ENVELOPE_FILE_PATH)
            .OrderByDescending(static file => file.CollectedAt)
            .FirstOrDefault();
        if (envelopeFile is null)
        {
            return null;
        }

        string? model = null;
        int attempt = 0;
        int? turns = null;
        long inputTokens = 0;
        long cachedInputTokens = 0;
        long outputTokens = 0;
        decimal? totalCostUsd = null;
        string? stopReason = null;
        long? wallDurationMs = null;
        long? apiDurationMs = null;
        long toolTimeMs = 0;
        var modelUsages = new List<AgentTraceModelUsage>();
        var permissionDenials = new List<AgentTracePermissionDenial>();
        IReadOnlyList<string> availableTools = [];
        IReadOnlyList<string> loadedSkills = [];
        IReadOnlyList<string>? runSkills = null;
        string? permissionMode = null;
        string? claudeCodeVersion = null;
        int skippedLines = 0;
        var timeline = new List<AgentTraceTimelineEntry>();

        foreach (JsonObject? record in ReadJsonLines(envelopeFile.Content))
        {
            if (record is null)
            {
                skippedLines++;
                continue;
            }

            switch (StringOf(record, "type"))
            {
                case AgentTraceRecordTypes.ENVELOPE:
                    model = StringOf(record, "modelPin") ?? model;
                    attempt = ValueOf<int>(record, "attempt") ?? attempt;
                    if (record["skills"] is JsonArray)
                    {
                        runSkills = StringsOf(record, "skills");
                    }

                    break;

                case AgentTraceRecordTypes.USAGE:
                    if (record["usage"] is JsonObject usage)
                    {
                        long cacheRead = ValueOf<long>(usage, "cache_read_input_tokens") ?? 0;
                        inputTokens += (ValueOf<long>(usage, "input_tokens") ?? 0)
                            + (ValueOf<long>(usage, "cache_creation_input_tokens") ?? 0)
                            + cacheRead;
                        cachedInputTokens += cacheRead;
                        outputTokens += ValueOf<long>(usage, "output_tokens") ?? 0;
                    }

                    break;

                case AgentTraceRecordTypes.MESSAGE:
                    if (record["message"] is not JsonObject message)
                    {
                        break;
                    }

                    string? messageType = StringOf(message, "type");
                    if (messageType == RESULT_MESSAGE_TYPE)
                    {
                        turns = ValueOf<int>(message, "num_turns") ?? turns;
                        totalCostUsd = ValueOf<decimal>(message, "total_cost_usd") ?? totalCostUsd;
                        stopReason = StringOf(message, "subtype") ?? stopReason;
                        wallDurationMs = ValueOf<long>(message, "duration_ms") ?? wallDurationMs;
                        apiDurationMs = ValueOf<long>(message, "duration_api_ms") ?? apiDurationMs;
                        ReadModelUsages(modelUsages, message);
                        ReadPermissionDenials(permissionDenials, message);
                    }
                    else if (messageType == ASSISTANT_MESSAGE_TYPE)
                    {
                        AddAssistantContent(timeline, TimestampOf(record), message);
                    }
                    else if (messageType == SYSTEM_MESSAGE_TYPE && StringOf(message, "subtype") == INIT_SUBTYPE)
                    {
                        availableTools = StringsOf(message, "tools");
                        loadedSkills = StringsOf(message, "skills");
                        permissionMode = StringOf(message, "permissionMode") ?? permissionMode;
                        claudeCodeVersion = StringOf(message, "claude_code_version") ?? claudeCodeVersion;
                    }

                    break;

                case AgentTraceRecordTypes.TOOL_CALL:
                    string? toolName = StringOf(record, "toolName");
                    toolTimeMs += ValueOf<long>(record, "durationMs") ?? 0;
                    timeline.Add(new AgentTraceTimelineEntry(
                        TimestampOf(record),
                        AgentTraceTimelineKind.ToolCall,
                        toolName ?? "tool",
                        ToolCallDetail(record, toolName)));
                    break;

                case AgentTraceRecordTypes.STAGE:
                    timeline.Add(new AgentTraceTimelineEntry(
                        TimestampOf(record),
                        AgentTraceTimelineKind.Stage,
                        StringOf(record, "event") ?? "stage",
                        StageDetail(record)));
                    break;

                default:
                    break;
            }
        }

        List<AgentTraceSubagentTimeline> subagentTimelines = ParseSubagentTimelines(files);

        return new AgentTraceDigest
        {
            Model = model,
            Attempt = attempt,
            Turns = turns,
            InputTokens = inputTokens,
            CachedInputTokens = cachedInputTokens,
            OutputTokens = outputTokens,
            TotalCostUsd = totalCostUsd,
            StopReason = stopReason,
            WallDurationMs = wallDurationMs,
            ApiDurationMs = apiDurationMs,
            ToolTimeMs = toolTimeMs,
            ModelUsages = modelUsages,
            PermissionDenials = permissionDenials,
            AvailableTools = availableTools,
            // The init message lists every skill the CLI found, its built-in ones included; the SDK
            // hides all but the run's own from the model. Traces before envelope v6 carry no list.
            LoadedSkills = runSkills ?? loadedSkills,
            PermissionMode = permissionMode,
            ClaudeCodeVersion = claudeCodeVersion,
            SkippedLines = skippedLines,
            Timeline = [.. timeline
                .OrderBy(static entry => entry.At ?? DateTimeOffset.MaxValue)],
            SubagentTimelines = subagentTimelines,
        };
    }

    /// <summary>
    /// The tolerant line reader shared by the envelope and sub-agent parses: one record per
    /// non-blank line, yielding null for a line that will not parse. The caller decides whether a
    /// null counts — the envelope tallies <see cref="SkippedLines"/>, the sub-agent parse skips it
    /// — because the raw files stay the evidence of record, so a bad line is never fatal.
    /// </summary>
    private static IEnumerable<JsonObject?> ReadJsonLines(byte[] content)
    {
        foreach (string line in Encoding.UTF8.GetString(content).Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonObject? record;
            try
            {
                record = JsonNode.Parse(line) as JsonObject;
            }
            catch (JsonException)
            {
                record = null;
            }

            yield return record;
        }
    }

    /// <summary>
    /// One assistant message's own words. The main trace shows no tool-use rows here — the pack's
    /// tool_call records carry those — so it passes <c>includeToolUse: false</c>.
    /// </summary>
    private static void AddAssistantContent(
        List<AgentTraceTimelineEntry> timeline,
        DateTimeOffset? at,
        JsonObject message)
    {
        if (message["message"] is JsonObject apiMessage && apiMessage["content"] is JsonArray content)
        {
            AddContentBlocks(timeline, at, content, includeToolUse: false);
        }
    }

    /// <summary>
    /// The content blocks of one assistant turn, in stream order: each thinking block and each
    /// prose text block becomes a timeline row. In the main trace the tool-use blocks are skipped —
    /// the pack's tool_call records already carry them with duration and outcome — but a sub-agent
    /// transcript carries no such records, so <paramref name="includeToolUse"/> renders its
    /// tool_use blocks as the only account of what the sub-agent ran.
    /// </summary>
    private static void AddContentBlocks(
        List<AgentTraceTimelineEntry> timeline,
        DateTimeOffset? at,
        JsonArray content,
        bool includeToolUse)
    {
        foreach (JsonNode? node in content)
        {
            if (node is not JsonObject block)
            {
                continue;
            }

            switch (StringOf(block, "type"))
            {
                case THINKING_BLOCK_TYPE:
                    AddTextEntry(
                        timeline, at, AgentTraceTimelineKind.Thinking, THINKING_LABEL, StringOf(block, THINKING_BLOCK_TYPE));
                    break;

                case TEXT_BLOCK_TYPE:
                    AddTextEntry(
                        timeline, at, AgentTraceTimelineKind.AssistantText, ASSISTANT_LABEL, StringOf(block, TEXT_BLOCK_TYPE));
                    break;

                case TOOL_USE_BLOCK_TYPE when includeToolUse:
                    string? toolName = StringOf(block, "name");
                    timeline.Add(new AgentTraceTimelineEntry(
                        at,
                        AgentTraceTimelineKind.ToolCall,
                        toolName ?? "tool",
                        ToolInputSummary(toolName, block["input"] as JsonObject)));
                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>
    /// The pack keeps each spawned sub-agent's transcript as its own <c>trace/sub/agent-*.jsonl</c>
    /// file, in the raw session shape — one nesting level and a <c>timestamp</c>, not the runtime
    /// envelope the main trace uses — so a delegating run no longer shows a gap where the work went.
    /// The newest file per agent wins, mirroring the envelope's own newest-collected rule. A file
    /// with no renderable content contributes no group, so a run without real sub-agent activity
    /// reads exactly as it does today.
    /// </summary>
    private static List<AgentTraceSubagentTimeline> ParseSubagentTimelines(IEnumerable<AgentRunTraceFile> files)
    {
        var timelines = new List<AgentTraceSubagentTimeline>();

        IEnumerable<IGrouping<string, AgentRunTraceFile>> byAgent = files
            .Select(static file => (Agent: SubagentIdentity(file.Path), File: file))
            .Where(static pair => pair.Agent is not null)
            .GroupBy(static pair => pair.Agent!, static pair => pair.File, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal);

        foreach (IGrouping<string, AgentRunTraceFile> group in byAgent)
        {
            AgentRunTraceFile file = group.MaxBy(static candidate => candidate.CollectedAt)!;
            var timeline = new List<AgentTraceTimelineEntry>();

            foreach (JsonObject? record in ReadJsonLines(file.Content))
            {
                if (record is null
                    || StringOf(record, "type") != ASSISTANT_MESSAGE_TYPE
                    || record["message"] is not JsonObject apiMessage
                    || apiMessage["content"] is not JsonArray content)
                {
                    continue;
                }

                AddContentBlocks(timeline, TimestampOf(record), content, includeToolUse: true);
            }

            if (timeline.Count > 0)
            {
                timelines.Add(new AgentTraceSubagentTimeline(
                    group.Key,
                    [.. timeline.OrderBy(static entry => entry.At ?? DateTimeOffset.MaxValue)]));
            }
        }

        return timelines;
    }

    private static string? SubagentIdentity(string path)
    {
        if (!path.StartsWith(SUBAGENT_PATH_PREFIX, StringComparison.Ordinal)
            || !path.EndsWith(SUBAGENT_PATH_SUFFIX, StringComparison.Ordinal))
        {
            return null;
        }

        int idLength = path.Length - SUBAGENT_PATH_PREFIX.Length - SUBAGENT_PATH_SUFFIX.Length;

        return idLength > 0 ? path.Substring(SUBAGENT_PATH_PREFIX.Length, idLength) : null;
    }

    private static void AddTextEntry(
        List<AgentTraceTimelineEntry> timeline,
        DateTimeOffset? at,
        AgentTraceTimelineKind kind,
        string label,
        string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        string trimmed = text.Trim();
        if (trimmed.Length > TEXT_DETAIL_MAX_CHARS)
        {
            trimmed = trimmed[..(TEXT_DETAIL_MAX_CHARS - TRUNCATION_SUFFIX.Length)] + TRUNCATION_SUFFIX;
        }

        timeline.Add(new AgentTraceTimelineEntry(at, kind, label, trimmed));
    }

    private static string? ToolCallDetail(JsonObject record, string? toolName)
    {
        var parts = new List<string>();

        long? durationMs = ValueOf<long>(record, "durationMs");
        if (durationMs is not null)
        {
            parts.Add($"{durationMs} ms");
        }

        long? outputBytes = ValueOf<long>(record, "outputBytes");
        if (outputBytes is not null)
        {
            parts.Add($"{outputBytes} B");
        }

        string? errorClass = StringOf(record, "errorClass");
        if (!string.IsNullOrWhiteSpace(errorClass))
        {
            parts.Add(errorClass);
        }

        string? input = ToolInputSummary(toolName, StringOf(record, "inputTruncated"));
        if (!string.IsNullOrWhiteSpace(input))
        {
            parts.Add(input);
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>
    /// The recorded input is JSON truncated at the runtime's cap, so a long input arrives cut
    /// mid-token; an unparseable input is shown raw rather than hidden, because a truncated
    /// command is still the row's most useful content.
    /// </summary>
    private static string? ToolInputSummary(string? toolName, string? inputTruncated)
    {
        if (string.IsNullOrWhiteSpace(inputTruncated))
        {
            return null;
        }

        try
        {
            return ToolInputSummary(toolName, JsonNode.Parse(inputTruncated) as JsonObject);
        }
        catch (JsonException)
        {
            return inputTruncated;
        }
    }

    private static string? ToolInputSummary(string? toolName, JsonObject? input)
    {
        if (input is null || input.Count == 0)
        {
            return null;
        }

        return toolName switch
        {
            AgentTraceToolNames.BASH => NonBlank(StringOf(input, "description")) ?? StringOf(input, "command") ?? input.ToJsonString(),
            AgentTraceToolNames.READ => ReadInputSummary(input),
            _ => input.ToJsonString(),
        };
    }

    private static string ReadInputSummary(JsonObject input)
    {
        string? filePath = StringOf(input, "file_path");
        if (filePath is null)
        {
            return input.ToJsonString();
        }

        var parts = new List<string> { filePath };
        foreach (string key in _readRangeKeys)
        {
            if (input[key] is JsonNode node and not JsonObject)
            {
                parts.Add($"{key} {node.ToJsonString().Trim('"')}");
            }
        }

        return string.Join(" · ", parts);
    }

    private static void ReadModelUsages(List<AgentTraceModelUsage> modelUsages, JsonObject message)
    {
        if (message["modelUsage"] is not JsonObject models)
        {
            return;
        }

        modelUsages.Clear();
        foreach ((string model, JsonNode? node) in models)
        {
            if (node is not JsonObject usage)
            {
                continue;
            }

            modelUsages.Add(new AgentTraceModelUsage(
                model,
                ValueOf<long>(usage, "inputTokens") ?? 0,
                ValueOf<long>(usage, "outputTokens") ?? 0,
                ValueOf<long>(usage, "cacheReadInputTokens") ?? 0,
                ValueOf<decimal>(usage, "costUSD")));
        }
    }

    private static void ReadPermissionDenials(List<AgentTracePermissionDenial> denials, JsonObject message)
    {
        if (message["permission_denials"] is not JsonArray array)
        {
            return;
        }

        denials.Clear();
        foreach (JsonNode? node in array)
        {
            if (node is not JsonObject denial)
            {
                continue;
            }

            string? tool = StringOf(denial, "tool_name");
            if (tool is null)
            {
                continue;
            }

            string? input = denial["tool_input"] is JsonObject toolInput
                ? ToolInputSummary(tool, toolInput)
                : null;
            denials.Add(new AgentTracePermissionDenial(tool, input));
        }
    }

    private static List<string> StringsOf(JsonObject record, string property)
    {
        if (record[property] is not JsonArray array)
        {
            return [];
        }

        var values = new List<string>();
        foreach (JsonNode? node in array)
        {
            if (node is JsonValue value && value.TryGetValue(out string? parsed))
            {
                values.Add(parsed);
            }
        }

        return values;
    }

    private static string? StageDetail(JsonObject record)
    {
        return record["detail"] is JsonObject detail && detail.Count > 0
            ? detail.ToJsonString()
            : null;
    }

    private static DateTimeOffset? TimestampOf(JsonObject record)
    {
        string? at = StringOf(record, "at") ?? StringOf(record, "timestamp");

        return DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)
            ? parsed
            : null;
    }

    private static string? NonBlank(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? StringOf(JsonObject record, string property)
    {
        return record[property] is JsonValue value && value.TryGetValue(out string? parsed)
            ? parsed
            : null;
    }

    private static T? ValueOf<T>(JsonObject record, string property)
        where T : struct
    {
        return record[property] is JsonValue value && value.TryGetValue(out T parsed)
            ? parsed
            : null;
    }
}
