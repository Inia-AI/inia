using IniaAi.Domain.Entities.AgentRuntime;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// One model request's tokens, or, when <see cref="Summed"/> is set, the total of several requests
/// the trace reports only as a sum.
/// </summary>
/// <param name="Summed">True for the requests the model SDK counted beyond the per-request
/// records: context compaction, the CLI's own small-model calls, and calls of a segment whose
/// stream records were lost. Their split per request is unknown, so they are priced at the
/// standard rates: a sum over a long-prompt threshold says nothing about any one request.</param>
public sealed record AgentRunCallTokens(
    long InputTokens,
    long OutputTokens,
    long CacheWrite5mTokens,
    long CacheWrite1hTokens,
    long CacheReadInputTokens,
    bool Summed = false)
{
    public long CacheCreationInputTokens => CacheWrite5mTokens + CacheWrite1hTokens;

    /// <summary>The request's prompt length as the long-prompt price counts it: every input token, cached or not.</summary>
    public long PromptTokens => InputTokens + CacheCreationInputTokens + CacheReadInputTokens;
}

/// <summary>
/// One model's share of an agent run's token spend, read from collected trace packs, kept per
/// request so a price that depends on prompt length can be applied request by request.
/// </summary>
public sealed record AgentRunModelTokens(string Model, IReadOnlyList<AgentRunCallTokens> Calls)
{
    public long InputTokens => Calls.Sum(static call => call.InputTokens);

    public long OutputTokens => Calls.Sum(static call => call.OutputTokens);

    public long CacheCreationInputTokens => Calls.Sum(static call => call.CacheCreationInputTokens);

    public long CacheWrite5mTokens => Calls.Sum(static call => call.CacheWrite5mTokens);

    public long CacheWrite1hTokens => Calls.Sum(static call => call.CacheWrite1hTokens);

    public long CacheReadInputTokens => Calls.Sum(static call => call.CacheReadInputTokens);
}

/// <summary>
/// Where a usage record's token counts came from, stamped onto the record's metadata so a
/// reader can tell "the run used no tokens" apart from "nobody could say".
/// </summary>
public static class AgentTokenProvenance
{
    /// <summary>The trace pack's usage records supplied the split.</summary>
    public const string FROM_TRACE = "trace";

    /// <summary>No trace pack was collected, or it carried no usage records.</summary>
    public const string UNKNOWN = "unknown";

    /// <summary>The run's cost is its tokens priced from the model pricing table.</summary>
    public const string COST_FROM_TOKENS = "tokens";

    /// <summary>The run's cost, or the part of it no pricing row covered, is the runtime's own measure.</summary>
    public const string COST_FROM_RUNTIME = "runtime";

    private const string TOKEN_SOURCE_FIELD = "tokenSource";
    private const string ROUND_FIELD = "round";
    private const string COST_SOURCE_FIELD = "costSource";
    private const string RUNTIME_COST_FIELD = "runtimeCostUsd";

    /// <summary>
    /// The metadata blob a usage record carries: its token provenance, and the round of the
    /// verification loop the spend belongs to. The round rides the free-form metadata on purpose
    /// — it makes a group's bill splittable by round, and by member against verifier, with no
    /// schema change and no second write. A block outside a loop records round one.
    /// </summary>
    public static string MetadataFor(string provenance, int round)
    {
        return JsonSerializer.Serialize(Fields(provenance, round));
    }

    /// <summary>
    /// <see cref="MetadataFor(string, int)"/> for a run's spend record, which also says where its
    /// cost came from and keeps the runtime's own figure beside it for comparison.
    /// </summary>
    public static string MetadataFor(string provenance, int round, string costSource, decimal runtimeCostUsd)
    {
        Dictionary<string, string> fields = Fields(provenance, round);
        fields[COST_SOURCE_FIELD] = costSource;
        fields[RUNTIME_COST_FIELD] = runtimeCostUsd.ToString(CultureInfo.InvariantCulture);

        return JsonSerializer.Serialize(fields);
    }

    private static Dictionary<string, string> Fields(string provenance, int round)
    {
        return new Dictionary<string, string>
        {
            [TOKEN_SOURCE_FIELD] = provenance,
            [ROUND_FIELD] = round.ToString(CultureInfo.InvariantCulture),
        };
    }
}

/// <summary>
/// Reads the per-model, per-request token split out of collected agent-run trace packs.
/// <para>
/// The runtime's monitor payload carries a cost figure and nothing else, so the trace pack is
/// the only place the split exists. Two sources inside it:
/// <list type="bullet">
/// <item>a <c>usage</c> record per settled API message, main loop and sub-agents,
/// carrying that message's model and final token counts;</item>
/// <item>the model SDK's per-model totals: the runtime's <c>model_usage</c> record per settled
/// segment, or, from a runtime that predates it, the <c>modelUsage</c> of the run's last
/// result message.</item>
/// </list>
/// The totals also count requests that never reach the message stream (context compaction, the
/// CLI's own small-model calls). Whatever the totals hold beyond the per-message records is added
/// as one <see cref="AgentRunCallTokens.Summed"/> request per model. A result message's
/// <c>modelUsage</c> carries every earlier segment of its session, restored ones included, so it
/// is read only for a run that did not start from a restored session.
/// </para>
/// <para>
/// Tolerant by design, as <see cref="AgentTraceRecordTypes"/> promises — a malformed line or an
/// unrecognised record type is skipped, never fatal, because the raw files stay the evidence of
/// record. An empty result means the tokens are <em>unknown</em>, never that they were zero.
/// </para>
/// </summary>
public static class AgentRunTraceTokens
{
    /// <summary>The pack file the usage records live in; sub-agent transcripts carry none.</summary>
    public const string TRACE_FILE_PATH = "trace/trace.ndjson";

    /// <summary>Stands in for a usage record that named no model, so its tokens still count.</summary>
    public const string UNKNOWN_MODEL = "unknown";

    private const string TYPE_FIELD = "type";
    private const string MODEL_FIELD = "model";
    private const string USAGE_FIELD = "usage";
    private const string MESSAGE_FIELD = "message";
    private const string EVENT_FIELD = "event";
    private const string DETAIL_FIELD = "detail";
    private const string RESUMED_FIELD = "resumed";
    private const string RESULT_MESSAGE_TYPE = "result";
    private const string MODEL_USAGE_FIELD = "modelUsage";
    private const string INPUT_TOKENS_FIELD = "input_tokens";
    private const string OUTPUT_TOKENS_FIELD = "output_tokens";
    private const string CACHE_CREATION_TOKENS_FIELD = "cache_creation_input_tokens";
    private const string CACHE_READ_TOKENS_FIELD = "cache_read_input_tokens";
    private const string CACHE_CREATION_SPLIT_FIELD = "cache_creation";
    private const string CACHE_WRITE_1H_FIELD = "ephemeral_1h_input_tokens";
    private const string TOTAL_INPUT_FIELD = "inputTokens";
    private const string TOTAL_OUTPUT_FIELD = "outputTokens";
    private const string TOTAL_CACHE_CREATION_FIELD = "cacheCreationInputTokens";
    private const string TOTAL_CACHE_READ_FIELD = "cacheReadInputTokens";
    private const string MODEL_PIN_FIELD = "modelPin";
    private const string MODEL_NAME_START = "claude";
    private const string VERSION_SUFFIX_MARKER = "-v";

    /// <summary>
    /// Sums every pack's records into one entry per model. Packs are deduplicated on their
    /// natural key (run, attempt, path) keeping the newest collection, so a re-collected pack is
    /// counted once while the separate packs of separate passes still add up.
    /// </summary>
    public static IReadOnlyList<AgentRunModelTokens> Read(IEnumerable<AgentRunTraceFile>? files)
    {
        if (files is null)
        {
            return [];
        }

        var calls = new Dictionary<string, List<AgentRunCallTokens>>(StringComparer.Ordinal);
        IEnumerable<AgentRunTraceFile> packs = files
            .Where(static file => file.Path == TRACE_FILE_PATH)
            .GroupBy(static file => (file.RunExecutionId, file.Attempt, file.Path))
            .Select(static group => group.MaxBy(static file => file.CollectedAt)!);

        foreach (AgentRunTraceFile pack in packs)
        {
            Accumulate(calls, pack);
        }

        return [.. calls.Select(static entry => new AgentRunModelTokens(entry.Key, entry.Value))];
    }

    /// <summary>
    /// What the runtime reported for the runs whose trace pack was not collected: their tokens are
    /// unknown, so their own figure is the only measure of what they spent.
    /// </summary>
    public static decimal UncoveredCostUsd(IReadOnlyList<AgentRunAttempt> attempts, IEnumerable<AgentRunTraceFile>? files)
    {
        var covered = new HashSet<string>(
            (files ?? []).Where(static file => file.Path == TRACE_FILE_PATH).Select(static file => file.RunExecutionId),
            StringComparer.Ordinal);

        return attempts
            .Where(attempt => !covered.Contains(attempt.ExecutionId))
            .Sum(static attempt => attempt.EstimatedCostUsd);
    }

    /// <summary>
    /// The model the runtime ran, from the first pack's envelope (<c>modelPin</c>), or null. A
    /// consumer that asked for no model of its own learns from it which provider served the run.
    /// </summary>
    public static string? ModelPinOf(IEnumerable<AgentRunTraceFile>? files)
    {
        foreach (AgentRunTraceFile pack in (files ?? []).Where(static file => file.Path == TRACE_FILE_PATH))
        {
            foreach (JsonObject record in RecordsOf(pack))
            {
                if (StringOf(record, TYPE_FIELD) == AgentTraceRecordTypes.ENVELOPE
                    && StringOf(record, MODEL_PIN_FIELD) is { Length: > 0 } pin)
                {
                    return pin;
                }
            }
        }

        return null;
    }

    private sealed record StreamCall(int Index, string Model, AgentRunCallTokens Tokens);

    private sealed record Totals(int Index, Dictionary<string, AgentRunCallTokens> ByModel);

    private static void Accumulate(Dictionary<string, List<AgentRunCallTokens>> calls, AgentRunTraceFile pack)
    {
        List<StreamCall> streamCalls = [];
        List<Totals> segmentTotals = [];
        Totals? lastResultTotals = null;
        bool resumed = false;
        int index = 0;

        foreach (JsonObject record in RecordsOf(pack))
        {
            index++;
            string? type = StringOf(record, TYPE_FIELD);
            if (type == AgentTraceRecordTypes.USAGE && record[USAGE_FIELD] is JsonObject usage)
            {
                string model = StringOf(record, MODEL_FIELD) is { Length: > 0 } named ? named : UNKNOWN_MODEL;
                streamCalls.Add(new StreamCall(index, model, CallOf(usage)));
            }
            else if (type == AgentTraceRecordTypes.MODEL_USAGE && record[MODEL_USAGE_FIELD] is JsonObject segment)
            {
                segmentTotals.Add(new Totals(index, TotalsOf(segment)));
            }
            else if (type == AgentTraceRecordTypes.STAGE
                && StringOf(record, EVENT_FIELD) == AgentTraceStageEvents.RUN_STARTED
                && record[DETAIL_FIELD] is JsonObject detail
                && BoolOf(detail, RESUMED_FIELD))
            {
                resumed = true;
            }
            else if (type == AgentTraceRecordTypes.MESSAGE
                && record[MESSAGE_FIELD] is JsonObject message
                && StringOf(message, TYPE_FIELD) == RESULT_MESSAGE_TYPE
                && message[MODEL_USAGE_FIELD] is JsonObject resultTotals)
            {
                lastResultTotals = new Totals(index, TotalsOf(resultTotals));
            }
        }

        foreach (StreamCall call in streamCalls)
        {
            Add(calls, call.Model, call.Tokens);
        }

        Totals? settled = segmentTotals.Count > 0
            ? new Totals(segmentTotals[^1].Index, Sum(segmentTotals))
            : resumed ? null : lastResultTotals;
        if (settled is null)
        {
            return;
        }

        foreach ((string totalsModel, AgentRunCallTokens total) in settled.ByModel)
        {
            string key = KeyOf(totalsModel);
            List<StreamCall> counted = [.. streamCalls.Where(call => call.Index < settled.Index && SameModel(KeyOf(call.Model), key))];
            AgentRunCallTokens? beyond = Beyond(total, counted.Select(static call => call.Tokens));
            if (beyond is not null)
            {
                Add(calls, counted.Count > 0 ? counted[0].Model : totalsModel, beyond);
            }
        }
    }

    private static IEnumerable<JsonObject> RecordsOf(AgentRunTraceFile pack)
    {
        foreach (string line in Encoding.UTF8.GetString(pack.Content).Split('\n'))
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
                continue;
            }

            if (record is not null)
            {
                yield return record;
            }
        }
    }

    private static void Add(Dictionary<string, List<AgentRunCallTokens>> calls, string model, AgentRunCallTokens call)
    {
        if (!calls.TryGetValue(model, out List<AgentRunCallTokens>? modelCalls))
        {
            modelCalls = [];
            calls[model] = modelCalls;
        }

        modelCalls.Add(call);
    }

    private static AgentRunCallTokens CallOf(JsonObject usage)
    {
        long cacheWrites = TokensOf(usage, CACHE_CREATION_TOKENS_FIELD);
        long cacheWrites1h = usage[CACHE_CREATION_SPLIT_FIELD] is JsonObject split
            ? Math.Min(cacheWrites, TokensOf(split, CACHE_WRITE_1H_FIELD))
            : 0;

        return new AgentRunCallTokens(
            TokensOf(usage, INPUT_TOKENS_FIELD),
            TokensOf(usage, OUTPUT_TOKENS_FIELD),
            cacheWrites - cacheWrites1h,
            cacheWrites1h,
            TokensOf(usage, CACHE_READ_TOKENS_FIELD));
    }

    private static Dictionary<string, AgentRunCallTokens> TotalsOf(JsonObject modelUsage)
    {
        var totals = new Dictionary<string, AgentRunCallTokens>(StringComparer.Ordinal);
        foreach ((string model, JsonNode? node) in modelUsage)
        {
            if (node is not JsonObject entry || string.IsNullOrWhiteSpace(model))
            {
                continue;
            }

            totals[model] = new AgentRunCallTokens(
                TokensOf(entry, TOTAL_INPUT_FIELD),
                TokensOf(entry, TOTAL_OUTPUT_FIELD),
                TokensOf(entry, TOTAL_CACHE_CREATION_FIELD),
                0,
                TokensOf(entry, TOTAL_CACHE_READ_FIELD),
                Summed: true);
        }

        return totals;
    }

    private static Dictionary<string, AgentRunCallTokens> Sum(IEnumerable<Totals> segments)
    {
        var sum = new Dictionary<string, AgentRunCallTokens>(StringComparer.Ordinal);
        foreach ((string model, AgentRunCallTokens tokens) in segments.SelectMany(static segment => segment.ByModel))
        {
            sum[model] = sum.TryGetValue(model, out AgentRunCallTokens? running)
                ? running with
                {
                    InputTokens = running.InputTokens + tokens.InputTokens,
                    OutputTokens = running.OutputTokens + tokens.OutputTokens,
                    CacheWrite5mTokens = running.CacheWrite5mTokens + tokens.CacheWrite5mTokens,
                    CacheReadInputTokens = running.CacheReadInputTokens + tokens.CacheReadInputTokens,
                }
                : tokens;
        }

        return sum;
    }

    /// <summary>
    /// What a model's totals hold beyond its per-request records, or null when nothing. Each
    /// count is compared on its own, so a total that undercounts one kind adds nothing for it.
    /// The totals do not split cache writes by lifetime, so the extra writes are 5-minute writes.
    /// </summary>
    private static AgentRunCallTokens? Beyond(AgentRunCallTokens total, IEnumerable<AgentRunCallTokens> counted)
    {
        List<AgentRunCallTokens> calls = [.. counted];
        long input = Math.Max(0, total.InputTokens - calls.Sum(static call => call.InputTokens));
        long output = Math.Max(0, total.OutputTokens - calls.Sum(static call => call.OutputTokens));
        long cacheWrites = Math.Max(0, total.CacheCreationInputTokens - calls.Sum(static call => call.CacheCreationInputTokens));
        long cacheReads = Math.Max(0, total.CacheReadInputTokens - calls.Sum(static call => call.CacheReadInputTokens));

        return input + output + cacheWrites + cacheReads == 0
            ? null
            : new AgentRunCallTokens(input, output, cacheWrites, 0, cacheReads, Summed: true);
    }

    /// <summary>
    /// The per-request records name a model as the API answered it (<c>claude-opus-5-5</c>); the
    /// SDK's totals name it as it was requested (<c>us.anthropic.claude-opus-5-5</c>). Both read
    /// as the model name from <c>claude</c> on, without a Bedrock version suffix.
    /// </summary>
    private static string KeyOf(string model)
    {
        int start = model.IndexOf(MODEL_NAME_START, StringComparison.OrdinalIgnoreCase);
        string key = start > 0 ? model[start..] : model;
        int suffix = key.LastIndexOf(VERSION_SUFFIX_MARKER, StringComparison.OrdinalIgnoreCase);
        if (suffix > 0 && suffix + VERSION_SUFFIX_MARKER.Length < key.Length && char.IsDigit(key[suffix + VERSION_SUFFIX_MARKER.Length]))
        {
            key = key[..suffix];
        }

        return key.ToLowerInvariant();
    }

    /// <summary>An alias names the same model as its dated id (<c>claude-haiku-4-5</c>, <c>claude-haiku-4-5-20251001</c>).</summary>
    private static bool SameModel(string left, string right)
    {
        return left == right
            || left.StartsWith(right + "-", StringComparison.Ordinal)
            || right.StartsWith(left + "-", StringComparison.Ordinal);
    }

    private static long TokensOf(JsonObject usage, string field)
    {
        try
        {
            long value = usage[field]?.GetValue<long>() ?? 0;

            return value > 0 ? value : 0;
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException)
        {
            return 0;
        }
    }

    private static bool BoolOf(JsonObject record, string field)
    {
        try
        {
            return record[field]?.GetValue<bool>() ?? false;
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException)
        {
            return false;
        }
    }

    private static string? StringOf(JsonObject record, string field)
    {
        try
        {
            return record[field]?.GetValue<string>();
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException)
        {
            return null;
        }
    }
}
