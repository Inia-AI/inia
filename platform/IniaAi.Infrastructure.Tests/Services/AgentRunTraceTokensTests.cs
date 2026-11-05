using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Domain.Entities.AgentRuntime;
using System.Text;
using System.Text.Json;

namespace IniaAi.Infrastructure.Tests.Services;

public class AgentRunTraceTokensTests
{
    private const string SONNET = "claude-sonnet-4-6";
    private const string HAIKU = "claude-haiku-4-5";
    private const string TOKEN_SOURCE_FIELD = "tokenSource";
    private const string ROUND_FIELD = "round";

    private static AgentRunTraceFile TraceFile(
        string path,
        string runExecutionId = "run-1",
        int attempt = 1,
        DateTime? collectedAt = null,
        params string[] lines)
    {
        return new AgentRunTraceFile
        {
            Path = path,
            RunExecutionId = runExecutionId,
            Attempt = attempt,
            CollectedAt = collectedAt ?? new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc),
            Content = Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n"),
        };
    }

    private static string UsageLine(string model, int input, int output, int cacheCreation = 0, int cacheRead = 0)
    {
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["uuid"] = "u",
            ["at"] = "2026-08-19T12:00:00Z",
            ["type"] = AgentTraceRecordTypes.USAGE,
            ["messageId"] = "m",
            ["model"] = model,
            ["usage"] = new Dictionary<string, object?>
            {
                ["input_tokens"] = input,
                ["output_tokens"] = output,
                ["cache_creation_input_tokens"] = cacheCreation,
                ["cache_read_input_tokens"] = cacheRead,
                ["output_tokens_details"] = null,
            },
        });
    }

    private const string OPUS = "claude-opus-5-5";
    private const string OPUS_REQUESTED = "us.anthropic.claude-opus-5-5";
    private const string SIDE_MODEL_REQUESTED = "us.anthropic.claude-haiku-4-5-20251001-v1:0";

    private static string ResultLine(Dictionary<string, (int Input, int Output, int CacheWrite, int CacheRead)> modelUsage)
    {
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["type"] = AgentTraceRecordTypes.MESSAGE,
            ["message"] = new Dictionary<string, object?>
            {
                ["type"] = "result",
                ["subtype"] = "success",
                ["total_cost_usd"] = 1.0,
                ["modelUsage"] = ModelUsage(modelUsage),
            },
        });
    }

    private static string SegmentTotalsLine(Dictionary<string, (int Input, int Output, int CacheWrite, int CacheRead)> modelUsage)
    {
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["type"] = AgentTraceRecordTypes.MODEL_USAGE,
            ["modelUsage"] = ModelUsage(modelUsage),
        });
    }

    private static Dictionary<string, object> ModelUsage(
        Dictionary<string, (int Input, int Output, int CacheWrite, int CacheRead)> modelUsage)
    {
        return modelUsage.ToDictionary(
            static entry => entry.Key,
            static entry => (object)new Dictionary<string, object>
            {
                ["inputTokens"] = entry.Value.Input,
                ["outputTokens"] = entry.Value.Output,
                ["cacheCreationInputTokens"] = entry.Value.CacheWrite,
                ["cacheReadInputTokens"] = entry.Value.CacheRead,
                ["costUSD"] = 0.5,
            });
    }

    private static string StartedLine(bool resumed)
    {
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["type"] = AgentTraceRecordTypes.STAGE,
            ["event"] = AgentTraceStageEvents.RUN_STARTED,
            ["detail"] = new Dictionary<string, object> { ["resumed"] = resumed },
        });
    }

    private static JsonElement Metadata(string provenance, int round)
    {
        using JsonDocument metadata = JsonDocument.Parse(AgentTokenProvenance.MetadataFor(provenance, round));

        return metadata.RootElement.Clone();
    }

    private static List<string> FieldsOf(JsonElement metadata)
    {
        return [.. metadata.EnumerateObject().Select(static field => field.Name).Order()];
    }

    [Fact]
    public void Read_WithoutFiles_ReportsNoModels()
    {
        Assert.Empty(AgentRunTraceTokens.Read(null));
        Assert.Empty(AgentRunTraceTokens.Read([]));
    }

    [Fact]
    public void Read_IgnoresFilesThatAreNotTheTracePack()
    {
        AgentRunTraceFile subagent = TraceFile(
            "trace/sub/agent-1.jsonl", lines: UsageLine(SONNET, 100, 20));

        Assert.Empty(AgentRunTraceTokens.Read([subagent]));
    }

    [Fact]
    public void Read_SumsEveryUsageRecordForOneModel()
    {
        AgentRunTraceFile trace = TraceFile(
            AgentRunTraceTokens.TRACE_FILE_PATH,
            lines:
            [
                """{"uuid":"1","at":"2026-08-19T12:00:00Z","type":"envelope","modelPin":"claude-sonnet-4-6","attempt":1}""",
                UsageLine(SONNET, 100, 40, cacheCreation: 500, cacheRead: 2000),
                UsageLine(SONNET, 25, 10),
            ]);

        AgentRunModelTokens tokens = Assert.Single(AgentRunTraceTokens.Read([trace]));

        Assert.Equal(SONNET, tokens.Model);
        Assert.Equal(125, tokens.InputTokens);
        Assert.Equal(50, tokens.OutputTokens);
        Assert.Equal(500, tokens.CacheCreationInputTokens);
        Assert.Equal(2000, tokens.CacheReadInputTokens);
    }

    [Fact]
    public void Read_KeepsEachModelsShareApart()
    {
        AgentRunTraceFile trace = TraceFile(
            AgentRunTraceTokens.TRACE_FILE_PATH,
            lines: [UsageLine(SONNET, 100, 40), UsageLine(HAIKU, 7, 3), UsageLine(SONNET, 10, 5)]);

        List<AgentRunModelTokens> tokens = [.. AgentRunTraceTokens.Read([trace])];

        Assert.Equal(2, tokens.Count);
        AgentRunModelTokens sonnet = Assert.Single(tokens, entry => entry.Model == SONNET);
        Assert.Equal(110, sonnet.InputTokens);
        Assert.Equal(45, sonnet.OutputTokens);
        AgentRunModelTokens haiku = Assert.Single(tokens, entry => entry.Model == HAIKU);
        Assert.Equal(7, haiku.InputTokens);
        Assert.Equal(3, haiku.OutputTokens);
    }

    [Fact]
    public void Read_SumsAcrossThePacksOfSeveralPasses()
    {
        AgentRunTraceFile first = TraceFile(
            AgentRunTraceTokens.TRACE_FILE_PATH, "run-1", lines: UsageLine(SONNET, 100, 40));
        AgentRunTraceFile second = TraceFile(
            AgentRunTraceTokens.TRACE_FILE_PATH, "run-2", lines: UsageLine(SONNET, 20, 5));

        AgentRunModelTokens tokens = Assert.Single(AgentRunTraceTokens.Read([first, second]));

        Assert.Equal(120, tokens.InputTokens);
        Assert.Equal(45, tokens.OutputTokens);
    }

    [Fact]
    public void Read_CountsOnePackOnceWhenItWasCollectedTwice()
    {
        AgentRunTraceFile earlier = TraceFile(
            AgentRunTraceTokens.TRACE_FILE_PATH,
            collectedAt: new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc),
            lines: UsageLine(SONNET, 100, 40));
        AgentRunTraceFile later = TraceFile(
            AgentRunTraceTokens.TRACE_FILE_PATH,
            collectedAt: new DateTime(2026, 8, 19, 12, 5, 0, DateTimeKind.Utc),
            lines: [UsageLine(SONNET, 100, 40), UsageLine(SONNET, 30, 10)]);

        AgentRunModelTokens tokens = Assert.Single(AgentRunTraceTokens.Read([earlier, later]));

        Assert.Equal(130, tokens.InputTokens);
        Assert.Equal(50, tokens.OutputTokens);
    }

    [Fact]
    public void Read_SkipsMalformedLinesInsteadOfThrowing()
    {
        AgentRunTraceFile trace = TraceFile(
            AgentRunTraceTokens.TRACE_FILE_PATH,
            lines:
            [
                "not json at all",
                """{"type":"usage","model":"claude-sonnet-4-6"}""",
                """{"type":"tool_call","name":"Bash"}""",
                UsageLine(SONNET, 100, 40),
                string.Empty,
            ]);

        AgentRunModelTokens tokens = Assert.Single(AgentRunTraceTokens.Read([trace]));

        Assert.Equal(100, tokens.InputTokens);
        Assert.Equal(40, tokens.OutputTokens);
    }

    [Fact]
    public void Read_NamesAnUnattributedUsageRecordRatherThanDroppingIt()
    {
        AgentRunTraceFile trace = TraceFile(
            AgentRunTraceTokens.TRACE_FILE_PATH,
            lines: """{"type":"usage","messageId":"m","usage":{"input_tokens":11,"output_tokens":2}}""");

        AgentRunModelTokens tokens = Assert.Single(AgentRunTraceTokens.Read([trace]));

        Assert.Equal(AgentRunTraceTokens.UNKNOWN_MODEL, tokens.Model);
        Assert.Equal(11, tokens.InputTokens);
    }

    [Fact]
    public void Read_TreatsAPackWithoutUsageRecordsAsUnknownRatherThanZero()
    {
        AgentRunTraceFile trace = TraceFile(
            AgentRunTraceTokens.TRACE_FILE_PATH,
            lines: """{"uuid":"1","at":"2026-08-19T12:00:00Z","type":"envelope","modelPin":"claude-sonnet-4-6"}""");

        Assert.Empty(AgentRunTraceTokens.Read([trace]));
    }

    [Fact]
    public void Read_KeepsEachRequestApart_SoAPricePerRequestCanApply()
    {
        AgentRunTraceFile trace = TraceFile(
            AgentRunTraceTokens.TRACE_FILE_PATH,
            lines: [UsageLine(SONNET, 100, 40, cacheRead: 2000), UsageLine(SONNET, 25, 10)]);

        AgentRunModelTokens tokens = Assert.Single(AgentRunTraceTokens.Read([trace]));

        Assert.Equal(2, tokens.Calls.Count);
        Assert.Equal(2100, tokens.Calls[0].PromptTokens);
        Assert.Equal(25, tokens.Calls[1].PromptTokens);
        Assert.All(tokens.Calls, static call => Assert.False(call.Summed));
    }

    [Fact]
    public void Read_SplitsOneHourCacheWritesFromFiveMinuteOnes()
    {
        string line = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["type"] = AgentTraceRecordTypes.USAGE,
            ["model"] = SONNET,
            ["usage"] = new Dictionary<string, object?>
            {
                ["input_tokens"] = 2,
                ["output_tokens"] = 5,
                ["cache_creation_input_tokens"] = 900,
                ["cache_read_input_tokens"] = 0,
                ["cache_creation"] = new Dictionary<string, int>
                {
                    ["ephemeral_5m_input_tokens"] = 300,
                    ["ephemeral_1h_input_tokens"] = 600,
                },
            },
        });

        AgentRunCallTokens call = Assert.Single(Assert.Single(AgentRunTraceTokens.Read([TraceFile(AgentRunTraceTokens.TRACE_FILE_PATH, lines: line)])).Calls);

        Assert.Equal(300, call.CacheWrite5mTokens);
        Assert.Equal(600, call.CacheWrite1hTokens);
        Assert.Equal(900, call.CacheCreationInputTokens);
    }

    /// <summary>
    /// The model SDK's per-model totals also count requests the message stream never
    /// shows (context compaction and the CLI's own small-model calls). Measured on a customer run, the
    /// stream held 86 / 54,561 / 207,484 / 2,886,303 Opus 5.5 tokens and the totals 2,185 / 67,023 /
    /// 207,938 / 3,040,988, plus one Haiku 4.5 call of 1,052 / 24.
    /// </summary>
    [Fact]
    public void Read_AddsWhatTheSdkTotalsCountBeyondTheStream_ForAFreshRun()
    {
        AgentRunTraceFile trace = TraceFile(
            AgentRunTraceTokens.TRACE_FILE_PATH,
            lines:
            [
                StartedLine(resumed: false),
                UsageLine(OPUS, 50, 30000, cacheCreation: 107484, cacheRead: 1886303),
                UsageLine(OPUS, 36, 24561, cacheCreation: 100000, cacheRead: 1000000),
                ResultLine(new()
                {
                    [SIDE_MODEL_REQUESTED] = (1052, 24, 0, 0),
                    [OPUS_REQUESTED] = (2185, 67023, 207938, 3040988),
                }),
            ]);

        List<AgentRunModelTokens> tokens = [.. AgentRunTraceTokens.Read([trace])];

        AgentRunModelTokens opus = Assert.Single(tokens, static entry => entry.Model == OPUS);
        Assert.Equal(3, opus.Calls.Count);
        Assert.Equal(2185, opus.InputTokens);
        Assert.Equal(67023, opus.OutputTokens);
        Assert.Equal(207938, opus.CacheCreationInputTokens);
        Assert.Equal(3040988, opus.CacheReadInputTokens);
        AgentRunCallTokens beyond = Assert.Single(opus.Calls, static call => call.Summed);
        Assert.Equal(2099, beyond.InputTokens);
        Assert.Equal(12462, beyond.OutputTokens);

        AgentRunModelTokens side = Assert.Single(tokens, static entry => entry.Model == SIDE_MODEL_REQUESTED);
        AgentRunCallTokens sideCall = Assert.Single(side.Calls);
        Assert.True(sideCall.Summed);
        Assert.Equal(1052, sideCall.InputTokens);
        Assert.Equal(24, sideCall.OutputTokens);
    }

    [Fact]
    public void Read_MatchesAnAliasInTheTotalsToTheDatedModelInTheStream()
    {
        AgentRunTraceFile trace = TraceFile(
            AgentRunTraceTokens.TRACE_FILE_PATH,
            lines:
            [
                UsageLine("claude-haiku-4-5-20251001", 100, 10),
                ResultLine(new() { ["claude-haiku-4-5"] = (100, 10, 0, 0) }),
            ]);

        AgentRunModelTokens tokens = Assert.Single(AgentRunTraceTokens.Read([trace]));

        Assert.Equal(100, tokens.InputTokens);
        Assert.DoesNotContain(tokens.Calls, static call => call.Summed);
    }

    [Fact]
    public void ModelPinOf_ReadsTheEnvelopesModel()
    {
        AgentRunTraceFile trace = TraceFile(
            AgentRunTraceTokens.TRACE_FILE_PATH,
            lines: """{"type":"envelope","modelPin":"us.anthropic.claude-haiku-5-5","attempt":1}""");

        Assert.Equal("us.anthropic.claude-haiku-5-5", AgentRunTraceTokens.ModelPinOf([trace]));
        Assert.Null(AgentRunTraceTokens.ModelPinOf(null));
    }

    [Fact]
    public void Read_AddsNothing_WhenTheSdkTotalsMatchTheStream()
    {
        AgentRunTraceFile trace = TraceFile(
            AgentRunTraceTokens.TRACE_FILE_PATH,
            lines:
            [
                UsageLine(OPUS, 32, 11707, cacheCreation: 38715, cacheRead: 434582),
                ResultLine(new() { [OPUS_REQUESTED] = (32, 11707, 38715, 434582) }),
            ]);

        AgentRunModelTokens opus = Assert.Single(AgentRunTraceTokens.Read([trace]));

        AgentRunCallTokens call = Assert.Single(opus.Calls);
        Assert.False(call.Summed);
    }

    /// <summary>
    /// A resumed session's result carries every earlier turn's tokens too (the CLI restores its
    /// cost ledger), so reading it would bill those turns again.
    /// </summary>
    [Fact]
    public void Read_IgnoresTheResultTotals_WhenTheRunResumedASession()
    {
        AgentRunTraceFile trace = TraceFile(
            AgentRunTraceTokens.TRACE_FILE_PATH,
            lines:
            [
                StartedLine(resumed: true),
                UsageLine(SONNET, 12, 2221, cacheCreation: 14697, cacheRead: 238582),
                ResultLine(new() { ["us.anthropic." + SONNET] = (22, 3748, 42679, 340605) }),
            ]);

        AgentRunModelTokens tokens = Assert.Single(AgentRunTraceTokens.Read([trace]));

        Assert.Equal(12, tokens.InputTokens);
        Assert.Equal(2221, tokens.OutputTokens);
        Assert.Single(tokens.Calls);
    }

    [Fact]
    public void Read_SumsTheRuntimesSegmentTotals_EvenForAResumedRun()
    {
        AgentRunTraceFile trace = TraceFile(
            AgentRunTraceTokens.TRACE_FILE_PATH,
            lines:
            [
                StartedLine(resumed: true),
                UsageLine(SONNET, 10, 100),
                SegmentTotalsLine(new() { ["us.anthropic." + SONNET] = (15, 130, 0, 0) }),
                UsageLine(SONNET, 5, 50),
                SegmentTotalsLine(new() { ["us.anthropic." + SONNET] = (5, 60, 0, 0) }),
                ResultLine(new() { ["us.anthropic." + SONNET] = (999, 999, 0, 0) }),
            ]);

        AgentRunModelTokens tokens = Assert.Single(AgentRunTraceTokens.Read([trace]));

        Assert.Equal(20, tokens.InputTokens);
        Assert.Equal(190, tokens.OutputTokens);
        AgentRunCallTokens beyond = Assert.Single(tokens.Calls, static call => call.Summed);
        Assert.Equal(5, beyond.InputTokens);
        Assert.Equal(40, beyond.OutputTokens);
    }

    [Fact]
    public void Read_CountsStreamRequestsAfterTheLastSettledSegment_OnTopOfItsTotals()
    {
        AgentRunTraceFile trace = TraceFile(
            AgentRunTraceTokens.TRACE_FILE_PATH,
            lines:
            [
                UsageLine(SONNET, 10, 100),
                ResultLine(new() { ["us.anthropic." + SONNET] = (10, 100, 0, 0) }),
                UsageLine(SONNET, 7, 70),
            ]);

        AgentRunModelTokens tokens = Assert.Single(AgentRunTraceTokens.Read([trace]));

        Assert.Equal(17, tokens.InputTokens);
        Assert.Equal(170, tokens.OutputTokens);
        Assert.DoesNotContain(tokens.Calls, static call => call.Summed);
    }

    [Fact]
    public void MetadataFor_CarriesTheTokenSourceAndTheRoundTogether()
    {
        JsonElement metadata = Metadata(AgentTokenProvenance.FROM_TRACE, 2);
        List<string> expectedFields = [ROUND_FIELD, TOKEN_SOURCE_FIELD];

        Assert.Equal(AgentTokenProvenance.FROM_TRACE, metadata.GetProperty(TOKEN_SOURCE_FIELD).GetString());
        Assert.Equal("2", metadata.GetProperty(ROUND_FIELD).GetString());
        Assert.Equal(expectedFields, FieldsOf(metadata));
    }

    [Fact]
    public void MetadataFor_RecordsTheFirstRoundForABlockOutsideAVerificationLoop()
    {
        JsonElement metadata = Metadata(AgentTokenProvenance.FROM_TRACE, AgentRunIds.FIRST_ROUND);

        Assert.Equal("1", metadata.GetProperty(ROUND_FIELD).GetString());
    }

    [Fact]
    public void MetadataFor_KeepsTheUnknownTokenSourceOnALaterRound()
    {
        JsonElement metadata = Metadata(AgentTokenProvenance.UNKNOWN, 3);

        Assert.Equal(AgentTokenProvenance.UNKNOWN, metadata.GetProperty(TOKEN_SOURCE_FIELD).GetString());
        Assert.Equal("3", metadata.GetProperty(ROUND_FIELD).GetString());
    }

    /// <summary>
    /// The round is the axis a verifying group's bill is split on, and it rides the free-form
    /// metadata so no schema had to move — so a repair round's record must differ from the first
    /// pass's in the round field and in nothing else.
    /// </summary>
    [Fact]
    public void MetadataFor_TellsARepairRoundFromTheFirstPassByTheRoundFieldAlone()
    {
        JsonElement firstPass = Metadata(AgentTokenProvenance.FROM_TRACE, AgentRunIds.FIRST_ROUND);
        JsonElement repair = Metadata(AgentTokenProvenance.FROM_TRACE, AgentRunIds.FIRST_ROUND + 1);

        Assert.Equal(FieldsOf(firstPass), FieldsOf(repair));
        Assert.Equal(
            firstPass.GetProperty(TOKEN_SOURCE_FIELD).GetString(),
            repair.GetProperty(TOKEN_SOURCE_FIELD).GetString());
        Assert.NotEqual(
            firstPass.GetProperty(ROUND_FIELD).GetString(),
            repair.GetProperty(ROUND_FIELD).GetString());
        Assert.Equal("2", repair.GetProperty(ROUND_FIELD).GetString());
    }
}
