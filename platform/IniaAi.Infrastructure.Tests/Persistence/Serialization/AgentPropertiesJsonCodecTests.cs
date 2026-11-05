using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Persistence.Serialization.Blocks;
using System.Text.Json;

namespace IniaAi.Infrastructure.Tests.Persistence.Serialization;

public class AgentPropertiesJsonCodecTests
{
    private const string SKILL_WITH_HAZARDS = """
        Extract for {ClientName}; emit {"total": 0}; upstream {{PortfolioSummary}}.
        """;

    private static AgentProperties CreateProperties()
    {
        var props = new AgentProperties
        {
            WorkerSkills =
            [
                new AgentWorkerSkill
                {
                    Name = "extraction",
                    Description = "Extracts figures. Use when the task asks for totals.",
                    Content = SKILL_WITH_HAZARDS,
                },
            ],
            PromptTemplate = "Work on {{PortfolioSummary}} only",
            SystemPromptTemplate = "You are exact about {ClientName}",
            OutputSchema = """{"type":"object","required":["MarkerCode"]}""",
            Model = "test-model-a",
        };
        props.SetExecutionState(true, []);
        props.ExecutionState!.WorkerSkills.Single(skill => skill.Name == "extraction").Content = "rendered content";
        props.ExecutionState.Prompt = "Work on S1 only";
        return props;
    }

    [Fact]
    public void UserDataOptions_ReadALegacyRunPassRowWithoutARound_AsRoundOne()
    {
        AgentProperties props = CreateProperties();
        props.ExecutionState!.RunPasses.Add(new AgentRunPass { Pass = 2, ExecutionId = "wf_block_w_2" });
        string json = JsonSerializer.Serialize<BlockProperties>(props, BlockPropertiesJsonDefaults.Options)
            .Replace("\"round\":1,", string.Empty);
        Assert.DoesNotContain("round", json, StringComparison.OrdinalIgnoreCase);

        var deserialized = Assert.IsType<AgentProperties>(JsonSerializer.Deserialize<BlockProperties>(
            json, BlockPropertiesJsonDefaults.Options));

        AgentRunPass row = Assert.Single(deserialized.ExecutionState!.RunPasses);
        Assert.Equal(1, row.Round);
        Assert.Equal(2, row.Pass);
    }

    [Fact]
    public void UserDataOptions_RoundTripsSkillsModelAndExecutionState()
    {
        AgentProperties props = CreateProperties();

        string json = JsonSerializer.Serialize<BlockProperties>(props, BlockPropertiesJsonDefaults.Options);
        BlockProperties? deserialized = JsonSerializer.Deserialize<BlockProperties>(
            json, BlockPropertiesJsonDefaults.Options);

        var agentProps = Assert.IsType<AgentProperties>(deserialized);
        Assert.Equal(BlockTypes.Agent, agentProps.Type);
        Assert.Equal("test-model-a", agentProps.Model);
        Assert.Equal(SKILL_WITH_HAZARDS, agentProps.WorkerSkills.Single(skill => skill.Name == "extraction").Content);
        Assert.Equal("Work on {{PortfolioSummary}} only", agentProps.PromptTemplate);
        Assert.Equal("You are exact about {ClientName}", agentProps.SystemPromptTemplate);
        Assert.Equal("""{"type":"object","required":["MarkerCode"]}""", agentProps.OutputSchema);
        Assert.NotNull(agentProps.ExecutionState);
        Assert.Equal("rendered content", agentProps.ExecutionState.WorkerSkills.Single(skill => skill.Name == "extraction").Content);
        Assert.Equal("Work on S1 only", agentProps.ExecutionState.Prompt);
    }

    [Fact]
    public void CoreOptions_StripsExecutionStateAndRoundTripsTemplate()
    {
        AgentProperties props = CreateProperties();

        string json = JsonSerializer.Serialize<BlockProperties>(props, BlockPropertiesJsonDefaults.CoreOptions);
        BlockProperties? deserialized = JsonSerializer.Deserialize<BlockProperties>(
            json, BlockPropertiesJsonDefaults.CoreOptions);

        Assert.DoesNotContain("executionState", json, StringComparison.OrdinalIgnoreCase);
        var agentProps = Assert.IsType<AgentProperties>(deserialized);
        Assert.Null(agentProps.ExecutionState);
        Assert.Equal(SKILL_WITH_HAZARDS, agentProps.WorkerSkills.Single(skill => skill.Name == "extraction").Content);
        Assert.Equal("test-model-a", agentProps.Model);
    }

    [Fact]
    public void LegacyNameToContentSkills_StillDeserialize()
    {
        const string legacyJson = """
            {"type":"agent","workerSkills":{"SKILL.md":"Extract the things.","summary":"Summarize."},"model":"test-model-a"}
            """;

        BlockProperties? deserialized = JsonSerializer.Deserialize<BlockProperties>(
            legacyJson, BlockPropertiesJsonDefaults.Options);

        var agentProps = Assert.IsType<AgentProperties>(deserialized);
        Assert.Equal(2, agentProps.WorkerSkills.Count);
        Assert.Equal("Extract the things.", agentProps.WorkerSkills.Single(skill => skill.Name == "SKILL.md").Content);
        Assert.Equal("Summarize.", agentProps.WorkerSkills.Single(skill => skill.Name == "summary").Content);
        Assert.All(agentProps.WorkerSkills, skill => Assert.Equal(string.Empty, skill.Description));
    }

    [Fact]
    public void LegacyExecutionStateSkills_StillDeserialize()
    {
        const string legacyJson = """
            {"type":"agent","workerSkills":{"SKILL.md":"template"},"model":"test-model-a",
             "executionState":{"workerSkills":{"SKILL.md":"rendered"},"prompt":"go"}}
            """;

        BlockProperties? deserialized = JsonSerializer.Deserialize<BlockProperties>(
            legacyJson, BlockPropertiesJsonDefaults.Options);

        var agentProps = Assert.IsType<AgentProperties>(deserialized);
        Assert.NotNull(agentProps.ExecutionState);
        Assert.Equal("rendered", Assert.Single(agentProps.ExecutionState.WorkerSkills).Content);
    }

    [Fact]
    public void FusesRoundTripThroughBothOptions()
    {
        AgentProperties props = CreateProperties();
        props.DeadlineMinutes = 7;
        props.MaxTurns = 12;
        props.MaxBudgetUsd = 0.4m;
        props.MaxRetainedOutputMib = 4;

        string json = JsonSerializer.Serialize<BlockProperties>(props, BlockPropertiesJsonDefaults.Options);
        var roundTripped = Assert.IsType<AgentProperties>(
            JsonSerializer.Deserialize<BlockProperties>(json, BlockPropertiesJsonDefaults.Options));

        Assert.Equal(7, roundTripped.DeadlineMinutes);
        Assert.Equal(12, roundTripped.MaxTurns);
        Assert.Equal(0.4m, roundTripped.MaxBudgetUsd);
        Assert.Equal(4, roundTripped.MaxRetainedOutputMib);

        string coreJson = JsonSerializer.Serialize<BlockProperties>(props, BlockPropertiesJsonDefaults.CoreOptions);
        var coreRoundTripped = Assert.IsType<AgentProperties>(
            JsonSerializer.Deserialize<BlockProperties>(coreJson, BlockPropertiesJsonDefaults.CoreOptions));

        Assert.Equal(7, coreRoundTripped.DeadlineMinutes);
        Assert.Equal(12, coreRoundTripped.MaxTurns);
    }

    [Fact]
    public void BlocksStoredBeforeFusesExisted_ComeBackDeclaringNothing()
    {
        const string legacyJson = """
            {"type":"agent","workerSkills":[{"name":"extraction","description":"d","content":"c"}],"model":"test-model-a"}
            """;

        var agentProps = Assert.IsType<AgentProperties>(
            JsonSerializer.Deserialize<BlockProperties>(legacyJson, BlockPropertiesJsonDefaults.Options));

        Assert.Null(agentProps.DeadlineMinutes);
        Assert.Null(agentProps.MaxBudgetUsd);
        Assert.Null(agentProps.MaxTurns);
        Assert.Null(agentProps.MaxRetainedOutputMib);
    }

    [Fact]
    public void BlocksStoredWithTheRetiredFuseSwitches_LoadCleanlyAndKeepTheirValues()
    {
        const string switchEraJson = """
            {"type":"agent","workerSkills":[{"name":"extraction","description":"d","content":"c"}],
             "model":"test-model-a","runDeadlineEnabled":false,"maxBudgetEnabled":true,
             "maxTurnsEnabled":false,"maxRetainedOutputEnabled":true,
             "deadlineMinutes":9,"maxTurns":12}
            """;

        var agentProps = Assert.IsType<AgentProperties>(
            JsonSerializer.Deserialize<BlockProperties>(switchEraJson, BlockPropertiesJsonDefaults.Options));

        Assert.Equal(9, agentProps.DeadlineMinutes);
        Assert.Equal(12, agentProps.MaxTurns);
        Assert.Null(agentProps.MaxBudgetUsd);
        Assert.Null(agentProps.MaxRetainedOutputMib);
    }

    [Fact]
    public void SkillAssets_RoundTripThroughBothOptions()
    {
        AgentProperties props = CreateProperties();
        props.WorkerSkills[0].Assets =
        [
            new AgentWorkerSkillAsset { Name = "verify.py", Content = "print(\"{{NotATag}}\")" },
        ];

        string json = JsonSerializer.Serialize<BlockProperties>(props, BlockPropertiesJsonDefaults.Options);
        var roundTripped = Assert.IsType<AgentProperties>(
            JsonSerializer.Deserialize<BlockProperties>(json, BlockPropertiesJsonDefaults.Options));
        AgentWorkerSkillAsset asset = Assert.Single(roundTripped.WorkerSkills[0].Assets!);
        Assert.Equal("verify.py", asset.Name);
        Assert.Equal("print(\"{{NotATag}}\")", asset.Content);

        string coreJson = JsonSerializer.Serialize<BlockProperties>(props, BlockPropertiesJsonDefaults.CoreOptions);
        var coreRoundTripped = Assert.IsType<AgentProperties>(
            JsonSerializer.Deserialize<BlockProperties>(coreJson, BlockPropertiesJsonDefaults.CoreOptions));
        AgentWorkerSkillAsset coreAsset = Assert.Single(coreRoundTripped.WorkerSkills[0].Assets!);
        Assert.Equal("verify.py", coreAsset.Name);
    }

    [Fact]
    public void SkillsWithoutAssets_SerializeWithoutAnAssetsKey()
    {
        AgentProperties props = CreateProperties();

        string json = JsonSerializer.Serialize<BlockProperties>(props, BlockPropertiesJsonDefaults.Options);

        Assert.DoesNotContain("assets", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SkillsStoredBeforeAssetsExisted_ReadBackWithNullAssets()
    {
        const string legacyJson = """
            {"type":"agent","workerSkills":[{"name":"extraction","description":"d","content":"c"}],"model":"test-model-a"}
            """;

        var agentProps = Assert.IsType<AgentProperties>(
            JsonSerializer.Deserialize<BlockProperties>(legacyJson, BlockPropertiesJsonDefaults.Options));

        Assert.Null(Assert.Single(agentProps.WorkerSkills).Assets);
        string reserialized = JsonSerializer.Serialize<BlockProperties>(
            agentProps, BlockPropertiesJsonDefaults.Options);
        Assert.DoesNotContain("assets", reserialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnUnflaggedAsset_SerializesWithoutAFillTagsKey_InBothOptions()
    {
        AgentProperties props = CreateProperties();
        props.WorkerSkills[0].Assets = [new AgentWorkerSkillAsset { Name = "verify.py", Content = "print(1)" }];

        string json = JsonSerializer.Serialize<BlockProperties>(props, BlockPropertiesJsonDefaults.Options);
        string coreJson = JsonSerializer.Serialize<BlockProperties>(props, BlockPropertiesJsonDefaults.CoreOptions);

        Assert.DoesNotContain("fillTags", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fillTags", coreJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AssetsStoredBeforeTheFlagExisted_ReserializeByteIdentically()
    {
        AgentProperties props = CreateProperties();
        props.WorkerSkills[0].Assets = [new AgentWorkerSkillAsset { Name = "verify.py", Content = "print(1)" }];
        string stored = JsonSerializer.Serialize<BlockProperties>(props, BlockPropertiesJsonDefaults.Options);

        var readBack = Assert.IsType<AgentProperties>(
            JsonSerializer.Deserialize<BlockProperties>(stored, BlockPropertiesJsonDefaults.Options));

        Assert.False(Assert.Single(readBack.WorkerSkills[0].Assets!).FillTags);
        Assert.Equal(stored, JsonSerializer.Serialize<BlockProperties>(readBack, BlockPropertiesJsonDefaults.Options));
    }

    [Fact]
    public void AFlaggedAsset_RoundTripsThroughBothOptions()
    {
        AgentProperties props = CreateProperties();
        props.WorkerSkills[0].Assets =
            [new AgentWorkerSkillAsset { Name = "filled.py", Content = "x = '{{fund}}'", FillTags = true }];

        foreach (JsonSerializerOptions options in new[] { BlockPropertiesJsonDefaults.Options, BlockPropertiesJsonDefaults.CoreOptions })
        {
            string json = JsonSerializer.Serialize<BlockProperties>(props, options);
            var roundTripped = Assert.IsType<AgentProperties>(JsonSerializer.Deserialize<BlockProperties>(json, options));

            Assert.True(Assert.Single(roundTripped.WorkerSkills[0].Assets!).FillTags);
        }
    }

    [Fact]
    public void APascalCaseFillTagsFlag_WrittenByNewtonsoft_ReadsBackThroughSystemTextJson()
    {
        const string pascalJson = """
            {"type":"agent","workerSkills":[{"Name":"extraction","Description":"d","Content":"c",
             "Assets":[{"Name":"filled.py","Content":"x","FillTags":true}]}],"model":"test-model-a"}
            """;

        var agentProps = Assert.IsType<AgentProperties>(
            JsonSerializer.Deserialize<BlockProperties>(pascalJson, BlockPropertiesJsonDefaults.Options));

        Assert.True(Assert.Single(Assert.Single(agentProps.WorkerSkills).Assets!).FillTags);
    }

    [Fact]
    public void PascalCaseAssets_WrittenByNewtonsoft_ReadBackThroughSystemTextJson()
    {
        const string pascalJson = """
            {"type":"agent","workerSkills":[{"Name":"extraction","Description":"d","Content":"c",
             "Assets":[{"Name":"verify.py","Content":"print(1)"}]}],"model":"test-model-a"}
            """;

        var agentProps = Assert.IsType<AgentProperties>(
            JsonSerializer.Deserialize<BlockProperties>(pascalJson, BlockPropertiesJsonDefaults.Options));

        AgentWorkerSkillAsset asset = Assert.Single(Assert.Single(agentProps.WorkerSkills).Assets!);
        Assert.Equal("verify.py", asset.Name);
        Assert.Equal("print(1)", asset.Content);
    }

    [Fact]
    public void DescriptionSurvivesTheRoundTrip()
    {
        AgentProperties props = CreateProperties();

        string json = JsonSerializer.Serialize<BlockProperties>(props, BlockPropertiesJsonDefaults.Options);
        BlockProperties? deserialized = JsonSerializer.Deserialize<BlockProperties>(
            json, BlockPropertiesJsonDefaults.Options);

        var agentProps = Assert.IsType<AgentProperties>(deserialized);
        Assert.Equal(
            "Extracts figures. Use when the task asks for totals.",
            Assert.Single(agentProps.WorkerSkills).Description);
    }

    [Fact]
    public void UserDataOptions_RoundTripsStartTimeRunBookkeeping()
    {
        AgentProperties props = CreateProperties();
        props.ExecutionState!.RunPasses.Add(new AgentRunPass
        {
            Pass = 1,
            ExecutionId = "wfexec_blk_w_1",
            Unacknowledged = true,
        });

        string json = JsonSerializer.Serialize<BlockProperties>(props, BlockPropertiesJsonDefaults.Options);
        var roundTripped = Assert.IsType<AgentProperties>(
            JsonSerializer.Deserialize<BlockProperties>(json, BlockPropertiesJsonDefaults.Options));

        AgentRunPass pass = Assert.Single(roundTripped.ExecutionState!.RunPasses);
        Assert.Equal(1, pass.Pass);
        Assert.Equal("wfexec_blk_w_1", pass.ExecutionId);
        Assert.True(pass.Unacknowledged);
        Assert.Null(pass.Status);
    }

    /// <summary>
    /// Pins the persisted key casing the jsonb query path depends on:
    /// <c>"Properties"->'executionState'->'runPasses'</c> with camelCase keys. The persistence
    /// options apply a camelCase naming policy and ignore the type's Newtonsoft PascalCase
    /// attributes, so a PascalCase jsonb path silently returns zero rows rather than erroring.
    /// </summary>
    [Fact]
    public void PersistedRunPassRedoModes_UseTheCamelCaseKeysTheJsonbPathQueries()
    {
        AgentProperties props = CreateProperties();
        props.ExecutionState!.RunPasses.Add(new AgentRunPass
        {
            Round = 2,
            Pass = 1,
            ExecutionId = "wfexec_blk_w_r2_1",
            ConfiguredRedoMode = "Revise",
            ActualRedoMode = "Scratch",
        });

        string json = JsonSerializer.Serialize<BlockProperties>(props, BlockPropertiesJsonDefaults.Options);

        Assert.Contains("\"runPasses\"", json);
        Assert.Contains("\"configuredRedoMode\":\"Revise\"", json);
        Assert.Contains("\"actualRedoMode\":\"Scratch\"", json);
        Assert.DoesNotContain("\"ConfiguredRedoMode\"", json);

        var roundTripped = Assert.IsType<AgentProperties>(
            JsonSerializer.Deserialize<BlockProperties>(json, BlockPropertiesJsonDefaults.Options));
        AgentRunPass pass = Assert.Single(roundTripped.ExecutionState!.RunPasses);
        Assert.Equal("Revise", pass.ConfiguredRedoMode);
        Assert.Equal("Scratch", pass.ActualRedoMode);
    }

    [Fact]
    public void PersistedRunPassRowsWithoutRedoModes_OmitTheKeys()
    {
        AgentProperties props = CreateProperties();
        props.ExecutionState!.RunPasses.Add(new AgentRunPass
        {
            Round = 1,
            Pass = 1,
            ExecutionId = "wfexec_blk_w_r1_1",
        });

        string json = JsonSerializer.Serialize<BlockProperties>(props, BlockPropertiesJsonDefaults.Options);

        Assert.DoesNotContain("configuredRedoMode", json);
        Assert.DoesNotContain("actualRedoMode", json);
    }

    [Fact]
    public void RunPassRowsWrittenBeforeTheMarkerExisted_ReadAsAcknowledged()
    {
        const string preMarkerJson = """
            {"type":"agent","workerSkills":[{"name":"extraction","description":"d","content":"c"}],
             "model":"test-model-a",
             "executionState":{"runPasses":[{"Pass":1,"ExecutionId":"wfexec_blk_w_1","Status":"Succeeded","EstimatedCostUsd":0.1}]}}
            """;

        var agentProps = Assert.IsType<AgentProperties>(
            JsonSerializer.Deserialize<BlockProperties>(preMarkerJson, BlockPropertiesJsonDefaults.Options));

        AgentRunPass pass = Assert.Single(agentProps.ExecutionState!.RunPasses);
        Assert.False(pass.Unacknowledged);
    }
}
