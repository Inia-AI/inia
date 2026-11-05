using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;

namespace IniaAi.Infrastructure.Tests.Services;

public partial class AgentActivityProjectorTests
{
    private const string REFERENCE_TRACE = "reference-income-statement-trace.ndjson";
    private const string CASH_FLOW_TRACE = "reference-cash-flow-trace.ndjson";
    private const string CLINICAL_TRACE = "synthetic-clinical-trace.ndjson";

    private static readonly string[] _internalMachinerySmells =
    [
        "/workspaces",
        "toolu_",
        "us.anthropic",
        "estimatedCostUsd",
        "sessionId",
        "pdftotext -layout",
        "sed -n",
        "grep -n",
        "cat >",
        "04ebd2e9-e301-467e-81cb-06dd079e5bcb",
    ];

    private static readonly string[] _financeVocabulary =
    [
        "income statement",
        "balance sheet",
        "cash flow",
        "reconcil",
        "financial",
        "accounting",
        "fiscal",
        "revenue",
        "audit",
        "ledger",
        "impairment",
        "goodwill",
        "amazon",
    ];

    [GeneratedRegex("[0-9a-f]{64}")]
    private static partial Regex Sha256HexRegex();

    private static byte[] Fixture(string name)
    {
        return File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "TestData", "AgentActivity", name));
    }

    private static byte[] Pack(params string[] lines)
    {
        return Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");
    }

    private static readonly AgentActivityProjectionOptions _narrated =
        new() { BeatSource = AgentActivityBeatSource.Narrated };
    private static readonly AgentActivityProjectionOptions _classified =
        new() { BeatSource = AgentActivityBeatSource.Classified };

    [Fact]
    public void Narration_IsPlainText_ALinkKeepsItsTextAndNeverItsTarget()
    {
        byte[] pack = Pack(
            STARTED,
            NarrationLine("n1", "2026-08-13T13:39:00.000Z", "**No block computes this.** The closest is [Sample block](block:0a1b2c3d4e5f), with `code`."));

        AgentActivityBeat beat = Assert.Single(AgentActivityProjector.Project(pack, _narrated).Beats);

        Assert.Equal("No block computes this. The closest is Sample block, with code.", beat.Label);
    }

    [Fact]
    public void AReadOfAStagedFile_NamesTheFileOnItsStep()
    {
        const string readCall =
            """{"uuid":"t9","at":"2026-08-13T13:39:20.000Z","type":"tool_call","toolUseId":"toolu_09","toolName":"Read","inputTruncated":"{\"file_path\":\"/workspaces/run-x/docs/dependency-map.json\"}","outputBytes":900,"durationMs":5,"errorClass":null}""";

        AgentActivityBeat beat = Assert.Single(AgentActivityProjector.Project(Pack(STARTED, readCall), _classified).Beats);

        Assert.Equal(AgentActivityMapping.READ_CONVERTED_TEXT_LABEL, beat.Label);
        Assert.Equal(["dependency-map.json"], beat.DocumentNames);
    }

    private static string NarrationLine(string uuid, string at, string text)
    {
        return "{\"uuid\":\"" + uuid + "\",\"at\":\"" + at + "\",\"type\":\"message\",\"seq\":1,"
            + "\"message\":{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\","
            + "\"content\":[{\"type\":\"text\",\"text\":\"" + text + "\"}]}}}";
    }

    private const string STARTED =
        """{"uuid":"s1","at":"2026-08-13T13:38:58.532Z","type":"stage","event":"run.started","executionId":"e_b_w_1","attempt":1,"detail":{"resumed":false}}""";
    private const string COMPLETED =
        """{"uuid":"s2","at":"2026-08-13T13:43:46.100Z","type":"stage","event":"run.completed","executionId":"e_b_w_1","attempt":1,"detail":{"estimatedCostUsd":0.89,"sessionId":"abc"}}""";
    private const string FAILED =
        """{"uuid":"s3","at":"2026-08-13T13:40:00.000Z","type":"stage","event":"run.failed","executionId":"e_b_w_1","attempt":1,"detail":{"subtype":"capacity"}}""";
    private const string CONVERT_CALL =
        """{"uuid":"t1","at":"2026-08-13T13:39:14.000Z","type":"tool_call","toolUseId":"toolu_01","toolName":"Bash","inputTruncated":"{\"command\":\"pdftotext -layout docs/Sample-Filing.pdf sample.txt && wc -l sample.txt\"}","outputBytes":42,"durationMs":1680,"errorClass":null}""";
    private const string PAGE_VIEW_CALL =
        """{"uuid":"t2","at":"2026-08-13T13:41:38.000Z","type":"tool_call","toolUseId":"toolu_02","toolName":"Read","inputTruncated":"{\"file_path\":\"/workspaces/run-x/docs/Sample-Filing.pdf\",\"pages\":\"49\"}","outputBytes":9000,"durationMs":57,"errorClass":null}""";
    private const string CODE_RUN_CALL =
        """{"uuid":"t3","at":"2026-08-13T13:42:47.000Z","type":"tool_call","toolUseId":"toolu_03","toolName":"Bash","inputTruncated":"{\"command\":\"python3 - <<'EOF'\\ntotal = 1 + 1\\nprint(total)\\nEOF\"}","outputBytes":42,"durationMs":44,"errorClass":null}""";
    private const string DESCRIBED_CALL =
        """{"uuid":"t6","at":"2026-08-13T13:39:30.000Z","type":"tool_call","toolUseId":"toolu_06","toolName":"Bash","inputTruncated":"{\"command\":\"awk '/^NOTE/{print NR}' sample.txt | head -40\",\"description\":\"Index the appendix headings\"}","outputBytes":300,"durationMs":30,"errorClass":null}""";
    private const string UNCLASSIFIABLE_CALL =
        """{"uuid":"t7","at":"2026-08-13T13:39:40.000Z","type":"tool_call","toolUseId":"toolu_07","toolName":"Bash","inputTruncated":"{\"command\":\"awk 'BEGIN{RS=\\\"x\\\"} {print NR}' sample.txt | head -5\"}","outputBytes":50,"durationMs":30,"errorClass":null}""";
    private const string ORIENTATION_CALL =
        """{"uuid":"t10","at":"2026-08-13T13:39:32.000Z","type":"tool_call","toolUseId":"toolu_10","toolName":"Bash","inputTruncated":"{\"command\":\"ls -la out/\",\"description\":\"Check out directory contents\"}","outputBytes":80,"durationMs":12,"errorClass":null}""";
    private const string SECOND_DESCRIBED_CALL =
        """{"uuid":"t11","at":"2026-08-13T13:39:44.000Z","type":"tool_call","toolUseId":"toolu_11","toolName":"Bash","inputTruncated":"{\"command\":\"grep -n 'Note 18' sample.txt\",\"description\":\"Locate balance sheet statement in 2025 report\"}","outputBytes":300,"durationMs":30,"errorClass":null}""";
    private const string ERRORED_DESCRIBED_CALL =
        """{"uuid":"t12","at":"2026-08-13T13:39:46.000Z","type":"tool_call","toolUseId":"toolu_12","toolName":"Bash","inputTruncated":"{\"command\":\"grep -n 'Total equity' missing.txt\",\"description\":\"Locate total equity in the 2024 report\"}","outputBytes":0,"durationMs":8,"errorClass":"tool_error"}""";
    private const string ERRORED_READ_CALL =
        """{"uuid":"t4","at":"2026-08-13T13:39:16.000Z","type":"tool_call","toolUseId":"toolu_04","toolName":"Read","inputTruncated":"{\"file_path\":\"/workspaces/run-x/sample.txt\",\"offset\":2260,\"limit\":90}","outputBytes":120,"durationMs":5,"errorClass":"tool_error"}""";

    [Fact]
    public void Project_EmptyPack_DegradesToAnEmptyDigest()
    {
        AgentActivityDigest digest = AgentActivityProjector.Project([]);

        Assert.Equal(AgentActivityDigest.CURRENT_SCHEMA_VERSION, digest.SchemaVersion);
        Assert.Equal(AgentActivityOutcome.Unknown, digest.Outcome);
        Assert.Empty(digest.Beats);
        Assert.NotEmpty(digest.Phases);
    }

    [Fact]
    public void Project_RunStart_IsExposedAsTheAbsoluteAnchorBeatsAreMeasuredFrom()
    {
        AgentActivityDigest digest = AgentActivityProjector.Project(Pack(STARTED, CONVERT_CALL));

        Assert.Equal(DateTimeOffset.Parse("2026-08-13T13:38:58.532Z"), digest.StartedAtUtc);
    }

    [Fact]
    public void Project_PackWithoutStageRecords_AnchorsToTheFirstTimestampedFact()
    {
        AgentActivityDigest digest = AgentActivityProjector.Project(Pack(CONVERT_CALL));

        Assert.Equal(DateTimeOffset.Parse("2026-08-13T13:39:14.000Z"), digest.StartedAtUtc);
    }

    [Fact]
    public void Project_EmptyPack_CarriesNoAnchor()
    {
        AgentActivityDigest digest = AgentActivityProjector.Project([]);

        Assert.Null(digest.StartedAtUtc);
    }

    [Fact]
    public void Project_StartedWithoutTerminal_ReadsAsRunning()
    {
        AgentActivityDigest digest = AgentActivityProjector.Project(Pack(STARTED, CONVERT_CALL));

        Assert.Equal(AgentActivityOutcome.Running, digest.Outcome);
        Assert.Null(digest.Receipt.DurationSeconds);
    }

    [Fact]
    public void Project_CompletedRun_ReadsAsSucceededWithDuration()
    {
        AgentActivityDigest digest = AgentActivityProjector.Project(Pack(STARTED, CONVERT_CALL, COMPLETED));

        Assert.Equal(AgentActivityOutcome.Succeeded, digest.Outcome);
        Assert.Equal(288, digest.Receipt.DurationSeconds);
    }

    [Fact]
    public void Project_FailedRun_ReadsAsFailedAndKeepsThePartialRecord()
    {
        AgentActivityDigest digest = AgentActivityProjector.Project(Pack(STARTED, CONVERT_CALL, FAILED));

        Assert.Equal(AgentActivityOutcome.Failed, digest.Outcome);
        Assert.NotEmpty(digest.Beats);
    }

    [Fact]
    public void Project_ConversionCall_BecomesABeatTimedFromRunStart()
    {
        AgentActivityDigest digest = AgentActivityProjector.Project(Pack(STARTED, CONVERT_CALL));

        AgentActivityBeat beat = Assert.Single(digest.Beats);
        Assert.Equal(15, beat.AtSeconds);
        Assert.Equal(AgentActivityBeatKind.Normal, beat.Kind);
        Assert.False(string.IsNullOrWhiteSpace(beat.Label));
        Assert.Contains("Sample-Filing.pdf", $"{beat.Label} {beat.Detail}");
        Assert.DoesNotContain("docs/", $"{beat.Label} {beat.Detail}");
        Assert.DoesNotContain("pdftotext", $"{beat.Label} {beat.Detail}");
    }

    [Fact]
    public void Project_PageView_IsAHeroBeatCarryingThePageNumber()
    {
        AgentActivityDigest digest = AgentActivityProjector.Project(Pack(STARTED, PAGE_VIEW_CALL));

        AgentActivityBeat beat = Assert.Single(digest.Beats);
        Assert.Equal(AgentActivityBeatKind.Hero, beat.Kind);
        Assert.Contains("49", $"{beat.Label} {beat.Detail}");
        Assert.DoesNotContain("/workspaces", $"{beat.Label} {beat.Detail}");
        Assert.Equal(1, digest.Receipt.PagesViewed);
    }

    [Fact]
    public void Project_CodeRun_IsAHeroBeatAndCountsTowardTheReceipt()
    {
        AgentActivityDigest digest = AgentActivityProjector.Project(Pack(STARTED, CODE_RUN_CALL));

        AgentActivityBeat beat = Assert.Single(digest.Beats);
        Assert.Equal(AgentActivityBeatKind.Hero, beat.Kind);
        Assert.DoesNotContain("python3", $"{beat.Label} {beat.Detail}");
        Assert.Equal(1, digest.Receipt.CodeRuns);
    }

    [Fact]
    public void Project_ModelAuthoredDescription_BecomesTheLabelVerbatim()
    {
        AgentActivityDigest digest = AgentActivityProjector.Project(Pack(STARTED, DESCRIBED_CALL));

        AgentActivityBeat beat = Assert.Single(digest.Beats);
        Assert.Equal("Index the appendix headings", beat.Label);
        Assert.DoesNotContain("awk", $"{beat.Label} {beat.Detail}");
    }

    [Fact]
    public void Project_UnclassifiableCommandWithoutDescription_ContributesNoBeatAndNoRawShell()
    {
        AgentActivityDigest digest = AgentActivityProjector.Project(Pack(STARTED, UNCLASSIFIABLE_CALL));

        Assert.Empty(digest.Beats);
    }

    [Fact]
    public void Project_RecoveredError_ShowsACalmRetryBeatNeverARawError()
    {
        AgentActivityDigest digest = AgentActivityProjector.Project(
            Pack(STARTED, ERRORED_READ_CALL, PAGE_VIEW_CALL));

        Assert.Equal(2, digest.Beats.Count);
        string retryText = $"{digest.Beats[0].Label} {digest.Beats[0].Detail}";
        Assert.DoesNotContain("error", retryText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tool_error", retryText);
        Assert.DoesNotContain("fail", retryText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AgentActivityBeatKind.Hero, digest.Beats[1].Kind);
    }

    [Fact]
    public void Project_ConsecutiveErrors_CollapseIntoOneRetryBeat()
    {
        string secondError = ERRORED_READ_CALL
            .Replace("\"uuid\":\"t4\"", "\"uuid\":\"t5\"")
            .Replace("13:39:16.000Z", "13:39:18.000Z");

        AgentActivityDigest digest = AgentActivityProjector.Project(
            Pack(STARTED, ERRORED_READ_CALL, secondError));

        _ = Assert.Single(digest.Beats);
    }

    [Fact]
    public void Project_TruncatedFinalLine_KeepsEverythingBeforeIt()
    {
        byte[] whole = Pack(STARTED, CONVERT_CALL, PAGE_VIEW_CALL);
        byte[] truncated = whole[..(whole.Length - 40)];

        AgentActivityDigest digest = AgentActivityProjector.Project(truncated);

        _ = Assert.Single(digest.Beats);
        Assert.Equal(AgentActivityOutcome.Running, digest.Outcome);
    }

    [Fact]
    public void Project_GarbageLines_AreSkippedNotFatal()
    {
        AgentActivityDigest digest = AgentActivityProjector.Project(
            Pack(STARTED, "not json at all", "{\"type\":\"tool_call\"", CONVERT_CALL));

        _ = Assert.Single(digest.Beats);
    }

    [Fact]
    public void Project_BeatsAreOrderedAndPhaseIndicesResolve()
    {
        AgentActivityDigest digest = AgentActivityProjector.Project(Fixture(REFERENCE_TRACE));

        Assert.Equal(digest.Beats, [.. digest.Beats.OrderBy(static beat => beat.AtSeconds)]);
        Assert.All(digest.Beats, beat => Assert.InRange(beat.PhaseIndex, 0, digest.Phases.Count - 1));
    }

    [Fact]
    public void Project_ReferencePack_GivesEveryToolCallABeatBesideTheAgentsOwnLines()
    {
        AgentActivityDigest digest = AgentActivityProjector.Project(Fixture(REFERENCE_TRACE));

        Assert.Equal(AgentActivityOutcome.Succeeded, digest.Outcome);
        Assert.InRange(digest.Beats.Count, 20, 60);
        Assert.Contains(digest.Beats, static beat => beat.Kind == AgentActivityBeatKind.Hero);
        Assert.Contains(digest.Beats, static beat =>
            beat.Kind == AgentActivityBeatKind.Check
            && beat.Label == "Reconciliation ties exactly for both periods.");
        Assert.Contains(digest.Beats, static beat =>
            beat.Label == AgentActivityMapping.SEARCHED_DOCUMENTS_LABEL);
        Assert.Contains(digest.Beats, static beat =>
            beat.Label == AgentActivityMapping.READ_CONVERTED_TEXT_LABEL);
        Assert.DoesNotContain(digest.Beats, static beat =>
            beat.Label == AgentActivityMapping.LOADED_SKILL_LABEL);
        Assert.All(digest.Beats, static beat => Assert.False(string.IsNullOrWhiteSpace(beat.Label)));
        Assert.Contains("Amazon-2024-Annual-Report-NEW.pdf", digest.Receipt.DocumentNames);
        Assert.Contains("income-statement-line-items", digest.Receipt.SkillNames);
        Assert.True(digest.Receipt.PagesViewed >= 1);
        Assert.True(digest.Receipt.CodeRuns >= 1);
    }

    [Fact]
    public void Project_ReferencePack_ClassifiedModeStaysSelectableForComparison()
    {
        AgentActivityDigest digest = AgentActivityProjector.Project(Fixture(REFERENCE_TRACE), _classified);

        Assert.Equal(AgentActivityOutcome.Succeeded, digest.Outcome);
        Assert.InRange(digest.Beats.Count, 12, 40);
        Assert.Contains(digest.Beats, static beat =>
            beat.Label == AgentActivityMapping.SEARCHED_DOCUMENTS_LABEL);
    }

    [Fact]
    public void Project_ClassifiedReferencePack_NeverRendersScratchFileNamesOrSearchTerms()
    {
        AgentActivityDigest digest = AgentActivityProjector.Project(Fixture(REFERENCE_TRACE), _classified);

        Assert.All(
            digest.Beats.Where(static beat =>
                beat.Label == AgentActivityMapping.SEARCHED_DOCUMENTS_LABEL
                || beat.Label == AgentActivityMapping.RECORDED_WORKING_FILE_LABEL),
            static beat => Assert.Null(beat.Detail));

        string serialized = JsonSerializer.Serialize(digest);
        Assert.DoesNotContain(".json", serialized);
        Assert.DoesNotContain("out/", serialized);
    }

    [Fact]
    public void Project_ConsecutiveCodeRuns_CollapseIntoOneHeroBeat()
    {
        string secondRun = CODE_RUN_CALL
            .Replace("\"uuid\":\"t3\"", "\"uuid\":\"t8\"")
            .Replace("13:42:47.000Z", "13:42:52.000Z");
        string thirdRun = CODE_RUN_CALL
            .Replace("\"uuid\":\"t3\"", "\"uuid\":\"t9\"")
            .Replace("13:42:47.000Z", "13:42:58.000Z");

        AgentActivityDigest digest = AgentActivityProjector.Project(
            Pack(STARTED, CODE_RUN_CALL, secondRun, thirdRun), _narrated);

        _ = Assert.Single(digest.Beats);
        Assert.Equal(3, digest.Receipt.CodeRuns);
    }

    [Fact]
    public void Project_NarrationThinPack_AutoFallsBackToClassified()
    {
        AgentActivityDigest digest = AgentActivityProjector.Project(Pack(STARTED, CONVERT_CALL));

        AgentActivityBeat beat = Assert.Single(digest.Beats);
        Assert.Equal(AgentActivityMapping.CONVERTED_DOCUMENTS_LABEL, beat.Label);
    }

    [Fact]
    public void Project_TidiedNarration_ReplacesTheRawLineAndCanDropIt()
    {
        string keep = NarrationLine("n1", "2026-08-13T13:39:20.000Z",
            "Found the statement around line 5778. Let me view the full statement.");
        string drop = NarrationLine("n2", "2026-08-13T13:39:25.000Z", "Now the next file:");
        var options = new AgentActivityProjectionOptions
        {
            BeatSource = AgentActivityBeatSource.Narrated,
            TidiedNarration = new Dictionary<string, string?>
            {
                ["n1:0"] = "Found the statement and read it in full",
                ["n2:0"] = null,
            },
        };

        AgentActivityDigest digest = AgentActivityProjector.Project(Pack(STARTED, keep, drop), options);

        AgentActivityBeat beat = Assert.Single(digest.Beats);
        Assert.Equal("Found the statement and read it in full", beat.Label);
        Assert.DoesNotContain("5778", $"{beat.Label} {beat.Detail}");
    }

    [Fact]
    public void Project_CheckBeats_StayVerbatimEvenWhenATidyExists()
    {
        string check = NarrationLine("n9", "2026-08-13T13:42:49.000Z",
            "Reconciliation ties exactly for both periods.");
        var options = new AgentActivityProjectionOptions
        {
            BeatSource = AgentActivityBeatSource.Narrated,
            TidiedNarration = new Dictionary<string, string?> { ["n9:0"] = "Everything checks out" },
        };
        AgentActivityDigest digest = AgentActivityProjector.Project(
            Pack(STARTED, CODE_RUN_CALL, check), options);

        AgentActivityBeat checkBeat = Assert.Single(
            digest.Beats, static beat => beat.Kind == AgentActivityBeatKind.Check);
        Assert.Equal("Reconciliation ties exactly for both periods.", checkBeat.Label);
    }

    [Fact]
    public void ExtractNarration_CarriesToolDescriptionsBesideTheAgentsOwnNotes()
    {
        IReadOnlyList<AgentNarrationLine> lines = AgentActivityProjector.ExtractNarration(
            Pack(
                STARTED,
                DESCRIBED_CALL,
                ORIENTATION_CALL,
                NarrationLine("n1", "2026-08-13T13:39:35.000Z", "Found it."),
                SECOND_DESCRIBED_CALL));

        Assert.Collection(
            lines,
            static line =>
            {
                Assert.Equal(AgentActivityLineKind.ToolAction, line.Kind);
                Assert.Equal("Index the appendix headings", line.Text);
            },
            static line =>
            {
                Assert.Equal(AgentActivityLineKind.ToolAction, line.Kind);
                Assert.Equal("Check out directory contents", line.Text);
            },
            static line =>
            {
                Assert.Equal(AgentActivityLineKind.Narration, line.Kind);
                Assert.Equal("Found it.", line.Text);
            },
            static line =>
            {
                Assert.Equal(AgentActivityLineKind.ToolAction, line.Kind);
                Assert.Equal("Locate balance sheet statement in 2025 report", line.Text);
            });
        Assert.All(lines, static line => Assert.False(string.IsNullOrWhiteSpace(line.Key)));
        Assert.Equal(lines.Count, lines.Select(static line => line.Key).Distinct().Count());
    }

    [Fact]
    public void ExtractNarration_WithholdsAnErroredCallsDescription()
    {
        IReadOnlyList<AgentNarrationLine> lines = AgentActivityProjector.ExtractNarration(
            Pack(STARTED, DESCRIBED_CALL, ERRORED_DESCRIBED_CALL));

        AgentNarrationLine line = Assert.Single(lines);
        Assert.Equal("Index the appendix headings", line.Text);
    }

    [Fact]
    public void Project_AnErroredCallsDescription_NeverReachesTheFeedEvenWhenTidied()
    {
        AgentActivityDigest digest = AgentActivityProjector.Project(
            Pack(STARTED, ERRORED_DESCRIBED_CALL),
            new AgentActivityProjectionOptions
            {
                TidiedNarration = new Dictionary<string, string?>
                {
                    ["t12:0"] = "Finding total equity in the 2024 report",
                },
            });

        Assert.DoesNotContain(
            digest.Beats,
            static beat => beat.Label.Contains("total equity", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(0.3)]
    [InlineData(0.5)]
    [InlineData(0.8)]
    public void ExtractNarration_LineKeys_SurviveTheTraceGrowingUnderThem(double fraction)
    {
        byte[] whole = Pack(
            STARTED,
            DESCRIBED_CALL,
            ORIENTATION_CALL,
            NarrationLine("n1", "2026-08-13T13:39:35.000Z", "Found it."),
            SECOND_DESCRIBED_CALL,
            COMPLETED);
        byte[] prefix = whole[..(int)(whole.Length * fraction)];

        string[] partialKeys =
            [.. AgentActivityProjector.ExtractNarration(prefix).Select(static line => line.Key)];
        string[] wholeKeys =
            [.. AgentActivityProjector.ExtractNarration(whole).Select(static line => line.Key)];

        Assert.Equal(wholeKeys.Take(partialKeys.Length), partialKeys);
    }

    [Fact]
    public void Project_TidiedToolDescription_ReplacesTheLabelAndADropLeavesNoHole()
    {
        byte[] pack = Pack(STARTED, DESCRIBED_CALL, ORIENTATION_CALL, PAGE_VIEW_CALL);
        IReadOnlyList<AgentNarrationLine> lines = AgentActivityProjector.ExtractNarration(pack);
        var options = new AgentActivityProjectionOptions
        {
            TidiedNarration = new Dictionary<string, string?>
            {
                [KeyOf(lines, "Index the appendix headings")] = "Reading the notes index",
                [KeyOf(lines, "Check out directory contents")] = null,
            },
        };

        AgentActivityDigest digest = AgentActivityProjector.Project(pack, options);

        Assert.Equal(2, digest.Beats.Count);
        Assert.Equal("Reading the notes index", digest.Beats[0].Label);
        Assert.Equal([31, 159], digest.Beats.Select(static beat => beat.AtSeconds));
        Assert.Equal(AgentActivityBeatKind.Hero, digest.Beats[1].Kind);
        Assert.All(digest.Beats, static beat => Assert.False(string.IsNullOrWhiteSpace(beat.Label)));
    }

    [Fact]
    public void Project_ToolDescriptionsWithoutATidy_StayRawSoAnOutageNeverEmptiesTheFeed()
    {
        AgentActivityDigest digest = AgentActivityProjector.Project(
            Pack(STARTED, DESCRIBED_CALL, ORIENTATION_CALL),
            new AgentActivityProjectionOptions { TidiedNarration = new Dictionary<string, string?>() });

        Assert.Equal(
            ["Index the appendix headings", "Check out directory contents"],
            digest.Beats.Select(static beat => beat.Label));
    }

    private static string KeyOf(IReadOnlyList<AgentNarrationLine> lines, string text)
    {
        return lines.Single(line => line.Text == text).Key;
    }

    [Fact]
    public void ExtractNarration_ReturnsOnlyTheAgentsOwnLines()
    {
        IReadOnlyList<AgentNarrationLine> lines =
            AgentActivityProjector.ExtractNarration(Fixture(REFERENCE_TRACE));

        Assert.Equal(6, lines.Count);
        Assert.All(lines, static line => Assert.False(string.IsNullOrWhiteSpace(line.Key)));
        Assert.Contains(lines, static line =>
            line.Text == "Reconciliation ties exactly for both periods.");
        Assert.DoesNotContain(lines, static line => line.Text.StartsWith("Base directory"));
    }

    [Fact]
    public void Project_ReferencePack_SerializesWithoutInternalMachinery()
    {
        AgentActivityDigest digest = AgentActivityProjector.Project(Fixture(REFERENCE_TRACE));

        string serialized = JsonSerializer.Serialize(digest);
        foreach (string smell in _internalMachinerySmells)
        {
            Assert.DoesNotContain(smell, serialized, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotMatch(Sha256HexRegex(), serialized);
    }

    [Fact]
    public void Project_CashFlowPack_RendersItsRealRecoveredErrorsCalmly()
    {
        AgentActivityDigest digest = AgentActivityProjector.Project(Fixture(CASH_FLOW_TRACE));

        Assert.Equal(AgentActivityOutcome.Succeeded, digest.Outcome);
        string serialized = JsonSerializer.Serialize(digest);
        Assert.DoesNotContain("tool_error", serialized);
        Assert.DoesNotContain("error", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Project_NonFinancePack_CarriesItsOwnDomainAndNoneOfOurs()
    {
        AgentActivityDigest digest = AgentActivityProjector.Project(Fixture(CLINICAL_TRACE));

        Assert.Equal(AgentActivityOutcome.Succeeded, digest.Outcome);
        Assert.InRange(digest.Beats.Count, 20, 60);

        string serialized = JsonSerializer.Serialize(digest);
        foreach (string financeWord in _financeVocabulary)
        {
            Assert.DoesNotContain(financeWord, serialized, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("Atlas-2024-Trial-Protocol-NEW.pdf", digest.Receipt.DocumentNames);
    }

    [Fact]
    public void Project_MappingVocabulary_ContainsNoFinanceWords()
    {
        foreach (string mappingText in AgentActivityMapping.AllMappingText())
        {
            foreach (string financeWord in _financeVocabulary)
            {
                Assert.DoesNotContain(financeWord, mappingText, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Theory]
    [InlineData(0.25, AgentActivityBeatSource.Narrated)]
    [InlineData(0.5, AgentActivityBeatSource.Narrated)]
    [InlineData(0.75, AgentActivityBeatSource.Narrated)]
    [InlineData(0.25, AgentActivityBeatSource.Classified)]
    [InlineData(0.5, AgentActivityBeatSource.Classified)]
    [InlineData(0.75, AgentActivityBeatSource.Classified)]
    public void Project_MidRunPrefix_IsAlwaysAPrefixOfTheFullFeed(
        double fraction, AgentActivityBeatSource beatSource)
    {
        var options = new AgentActivityProjectionOptions { BeatSource = beatSource };
        byte[] whole = Fixture(REFERENCE_TRACE);
        byte[] prefix = whole[..(int)(whole.Length * fraction)];

        AgentActivityDigest partial = AgentActivityProjector.Project(prefix, options);
        AgentActivityDigest full = AgentActivityProjector.Project(whole, options);

        Assert.True(partial.Beats.Count <= full.Beats.Count);
        for (int index = 0; index < partial.Beats.Count; index++)
        {
            Assert.Equal(full.Beats[index], partial.Beats[index]);
        }
    }
}
