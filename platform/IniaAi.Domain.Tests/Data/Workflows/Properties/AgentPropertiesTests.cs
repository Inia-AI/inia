using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;
using static IniaAi.Domain.Tests.Data.Workflows.Properties.BlockPropertiesTestCodec;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties;

public class AgentPropertiesTests
{
    private const string EXTRACTION_SKILL = """
        # Extraction skill
        Extract line items for {ClientName} from the pack.
        Emit JSON exactly as: {"client":"...","total":0}
        Use {{PortfolioSummary}} as upstream context.
        CI example stays literal: ${{ github.ref }}
        """;

    private const string SUMMARY_SKILL = "Summarize {Quarter} for {ClientName}.";

    private static AgentWorkerSkill Skill(string name, string content)
    {
        return new AgentWorkerSkill { Name = name, Description = $"Handles {name}.", Content = content };
    }

    private static string SkillContent(AgentProperties props, string name)
    {
        return props.WorkerSkills.Single(skill => skill.Name == name).Content;
    }

    private static string ExecutedSkillContent(AgentProperties props, string name)
    {
        return props.ExecutionState!.WorkerSkills.Single(skill => skill.Name == name).Content;
    }

    private static AgentProperties CreateTwoSkillProperties()
    {
        return new AgentProperties
        {
            WorkerSkills =
            [
                Skill("extraction", EXTRACTION_SKILL),
                Skill("summary", SUMMARY_SKILL),
            ],
            Model = "test-model-a",
        };
    }

    [Fact]
    public void SetExecutionState_PreservesTheRunLedgerAcrossReResolution()
    {
        AgentProperties props = CreateTwoSkillProperties();
        props.SetExecutionState(false, []);
        props.ExecutionState!.RunPasses.Add(new AgentRunPass
        {
            Round = 1,
            Pass = 1,
            ExecutionId = "wf_block_w_r1_1",
            Status = "Succeeded",
            EstimatedCostUsd = 1.25m,
        });

        props.SetExecutionState(false, []);

        AgentRunPass survivor = Assert.Single(props.ExecutionState!.RunPasses);
        Assert.Equal("wf_block_w_r1_1", survivor.ExecutionId);
        Assert.Equal(1, survivor.Round);
        Assert.Equal(1.25m, survivor.EstimatedCostUsd);
    }

    [Fact]
    public void SetExecutionState_CarriesTheLedgerIntoItsOwnList_NotTheReplacedStatesList()
    {
        AgentProperties props = CreateTwoSkillProperties();
        props.SetExecutionState(false, []);
        AgentPropertiesExecutionState previous = props.ExecutionState!;
        previous.RunPasses.Add(new AgentRunPass { Round = 1, Pass = 1, ExecutionId = "wf_block_w_r1_1" });

        props.SetExecutionState(false, []);
        props.ExecutionState!.RunPasses.Add(new AgentRunPass { Round = 2, Pass = 1, ExecutionId = "wf_block_w_r2_1" });

        AgentRunPass untouched = Assert.Single(previous.RunPasses);
        Assert.Equal("wf_block_w_r1_1", untouched.ExecutionId);
        Assert.Equal(2, props.ExecutionState.RunPasses.Count);
    }

    [Fact]
    public void RunPassRows_WrittenBeforeTheRoundAxisExisted_ReadAsRoundOne()
    {
        var row = JsonConvert.DeserializeObject<AgentRunPass>(
            """{"Pass":2,"ExecutionId":"wf_block_w_2","Unacknowledged":false,"EstimatedCostUsd":0.5}""");

        Assert.NotNull(row);
        Assert.Equal(1, row!.Round);
        Assert.Equal(2, row.Pass);
    }

    [Fact]
    public void RunPassRows_WrittenBeforeTheRedoModeFieldsExisted_StillDeserialize()
    {
        var row = JsonConvert.DeserializeObject<AgentRunPass>(
            """{"Round":1,"Pass":1,"ExecutionId":"wf_block_w_r1_1","Status":"Succeeded","EstimatedCostUsd":0.5}""");

        Assert.NotNull(row);
        Assert.Null(row!.ConfiguredRedoMode);
        Assert.Null(row.ActualRedoMode);
    }

    [Fact]
    public void RunPassRows_WithNoRedoModes_OmitTheFieldsFromTheJson()
    {
        string json = JsonConvert.SerializeObject(new AgentRunPass
        {
            Round = 1,
            Pass = 1,
            ExecutionId = "wf_block_w_r1_1",
        });

        Assert.DoesNotContain(nameof(AgentRunPass.ConfiguredRedoMode), json);
        Assert.DoesNotContain(nameof(AgentRunPass.ActualRedoMode), json);
    }

    [Fact]
    public void RunPassRows_WithRedoModes_RoundTripBothStamps()
    {
        string json = JsonConvert.SerializeObject(new AgentRunPass
        {
            Round = 2,
            Pass = 1,
            ExecutionId = "wf_block_w_r2_1",
            ConfiguredRedoMode = "Revise",
            ActualRedoMode = "Scratch",
        });

        var row = JsonConvert.DeserializeObject<AgentRunPass>(json);

        Assert.NotNull(row);
        Assert.Equal("Revise", row!.ConfiguredRedoMode);
        Assert.Equal("Scratch", row.ActualRedoMode);
    }

    [Fact]
    public void Constructor_SetsAgentTypeAndEmptyDefaults()
    {
        var props = new AgentProperties();

        Assert.Equal(BlockTypes.Agent, props.Type);
        Assert.Empty(props.WorkerSkills);
        Assert.Null(props.Model);
        Assert.Null(props.ExecutionState);
        Assert.Null(props.DeadlineMinutes);
        Assert.Null(props.MaxBudgetUsd);
        Assert.Null(props.MaxTurns);
        Assert.Null(props.MaxRetainedOutputMib);
    }

    [Fact]
    public void AgentEnumMember_KeepsItsWireValue()
    {
        Assert.Equal(28, (int)BlockTypes.Agent);
        Assert.Equal(BlockTypes.Agent, Enum.Parse<BlockTypes>("Agent"));
    }

    [Fact]
    public void CreateDefaultProperties_Agent_ReturnsAgentProperties()
    {
        BlockProperties props = Block.CreateDefaultProperties(BlockTypes.Agent);

        var agentProps = Assert.IsType<AgentProperties>(props);
        Assert.Equal(BlockTypes.Agent, agentProps.Type);
    }

    [Fact]
    public void GetAllPlaceholders_DiscoversDoubleBraceEngineTagsOnly()
    {
        AgentProperties props = CreateTwoSkillProperties();

        List<string> placeholders = props.GetAllPlaceholders();

        Assert.Equal(["PortfolioSummary"], placeholders);
    }

    [Fact]
    public void GetAllPlaceholders_StrategyNamespaceTags_AreNotDiscovered()
    {
        var props = new AgentProperties
        {
            WorkerSkills =
            [
                Skill("loop", "Act on {{UpstreamTag}}; then read {{Validator.decision}} and {{Verifier.verdict}}."),
            ],
        };

        List<string> placeholders = props.GetAllPlaceholders();

        Assert.Equal(["UpstreamTag"], placeholders);
    }

    [Fact]
    public void GetAllPlaceholders_CarriesPromptAndSystemPromptEngineTags()
    {
        var props = new AgentProperties
        {
            PromptTemplate = "Focus on {{RiskSummary}}; single-brace {ClientName} stays out.",
            SystemPromptTemplate = "You review {{PolicyDigest}}.",
        };

        List<string> placeholders = props.GetAllPlaceholders();

        Assert.Contains("RiskSummary", placeholders);
        Assert.Contains("PolicyDigest", placeholders);
        Assert.DoesNotContain("ClientName", placeholders);
    }

    [Fact]
    public void SetExecutionState_CopiesPromptSystemPromptAndOutputSchemaVerbatim()
    {
        AgentProperties props = CreateTwoSkillProperties();
        props.PromptTemplate = "Do the work on {{PortfolioSummary}}";
        props.SystemPromptTemplate = "Be strict about {ClientName}";
        props.OutputSchema = """{"type":"object"}""";

        props.SetExecutionState(true, new Dictionary<string, string?> { ["PortfolioSummary"] = "S1" });

        Assert.NotNull(props.ExecutionState);
        Assert.Equal("Do the work on {{PortfolioSummary}}", props.ExecutionState.Prompt);
        Assert.Equal("Be strict about {ClientName}", props.ExecutionState.SystemPrompt);
        Assert.Equal("""{"type":"object"}""", props.ExecutionState.OutputSchema);
    }

    [Fact]
    public void NewtonsoftRoundTrip_PreservesPromptSystemPromptAndOutputSchema()
    {
        AgentProperties props = CreateTwoSkillProperties();
        props.PromptTemplate = "P {{A}}";
        props.SystemPromptTemplate = "S {{B}}";
        props.OutputSchema = """{"type":"object","required":["MarkerCode"]}""";

        BlockProperties? deserialized = NewtonsoftRoundTrip(props);

        var agentProps = Assert.IsType<AgentProperties>(deserialized);
        Assert.Equal("P {{A}}", agentProps.PromptTemplate);
        Assert.Equal("S {{B}}", agentProps.SystemPromptTemplate);
        Assert.Equal("""{"type":"object","required":["MarkerCode"]}""", agentProps.OutputSchema);
    }

    [Fact]
    public void GetAllTemplateStrings_SkillContentStaysInvisible()
    {
        AgentProperties props = CreateTwoSkillProperties();

        List<string> templates = props.GetAllTemplateStrings();

        Assert.DoesNotContain(EXTRACTION_SKILL, templates);
        Assert.DoesNotContain(SUMMARY_SKILL, templates);
    }

    [Fact]
    public void BlockGetAllPlaceholders_CarriesSkillEngineTags()
    {
        var workflow = new Workflow("agent-tags");
        var section = new Section("body", workflow);
        var block = new Block(BlockTypes.Agent, section, "extraction_agent");
        ((AgentProperties)block.Properties).WorkerSkills.Add(Skill("extraction", EXTRACTION_SKILL));

        List<string> placeholders = block.GetAllPlaceholders();

        Assert.Contains("PortfolioSummary", placeholders);
        Assert.DoesNotContain("ClientName", placeholders);
    }

    [Fact]
    public void HasPlaceholder_SkillContentToken_ReturnsFalse()
    {
        AgentProperties props = CreateTwoSkillProperties();

        Assert.False(props.HasPlaceholder("{ClientName}"));
        Assert.False(props.HasPlaceholder("{Quarter}"));
    }

    [Fact]
    public void ReplaceAll_LeavesWorkerSkillsVerbatim()
    {
        AgentProperties props = CreateTwoSkillProperties();
        Dictionary<string, string?> replacements = new()
        {
            ["ClientName"] = "ACME Corp",
            ["Quarter"] = "Q2 2026",
        };

        props.ReplaceAll(replacements);

        Assert.Equal(EXTRACTION_SKILL, SkillContent(props, "extraction"));
        Assert.Equal(SUMMARY_SKILL, SkillContent(props, "summary"));
    }

    [Fact]
    public void SetExecutionState_AsPlaceholder_CarriesSkillsVerbatim()
    {
        AgentProperties props = CreateTwoSkillProperties();
        Dictionary<string, string?> inputs = new()
        {
            ["ClientName"] = "ACME Corp",
            ["PortfolioSummary"] = "No data available",
        };

        props.SetExecutionState(true, inputs);

        Assert.NotNull(props.ExecutionState);
        Assert.Equal(EXTRACTION_SKILL, ExecutedSkillContent(props, "extraction"));
        Assert.Equal(SUMMARY_SKILL, ExecutedSkillContent(props, "summary"));
        Assert.Equal("test-model-a", props.Model);
    }

    [Fact]
    public void SetExecutionState_RawReplacement_CarriesSkillsVerbatim()
    {
        AgentProperties props = CreateTwoSkillProperties();
        Dictionary<string, string?> inputs = new()
        {
            ["{ClientName}"] = "ACME Corp",
            ["Extract line items"] = "REPLACED",
        };

        props.SetExecutionState(false, inputs);

        Assert.NotNull(props.ExecutionState);
        Assert.Equal(EXTRACTION_SKILL, ExecutedSkillContent(props, "extraction"));
    }

    [Fact]
    public void UpdateExecutionState_KeepsExistingRenderedSkills()
    {
        AgentProperties props = CreateTwoSkillProperties();
        props.SetExecutionState(true, []);
        props.ExecutionState!.WorkerSkills.Single(skill => skill.Name == "extraction").Content = "rendered content";

        props.UpdateExecutionState(true, new Dictionary<string, string?> { ["ClientName"] = "ACME" });

        Assert.Equal("rendered content", ExecutedSkillContent(props, "extraction"));
    }

    [Fact]
    public void ClearExecutionState_RemovesState()
    {
        AgentProperties props = CreateTwoSkillProperties();
        props.SetExecutionState(true, []);

        props.ClearExecutionState();

        Assert.Null(props.ExecutionState);
    }

    [Fact]
    public void Clone_DeepCopiesWorkerSkills()
    {
        AgentProperties props = CreateTwoSkillProperties();

        var clone = (AgentProperties)props.Clone();
        props.WorkerSkills.Single(skill => skill.Name == "extraction").Content = "mutated after clone";
        props.WorkerSkills.Add(Skill("added", "new entry"));

        Assert.Equal(BlockTypes.Agent, clone.Type);
        Assert.Equal("test-model-a", clone.Model);
        Assert.Equal(2, clone.WorkerSkills.Count);
        Assert.Equal(EXTRACTION_SKILL, SkillContent(clone, "extraction"));
    }

    [Fact]
    public void Clone_CarriesTheFuses()
    {
        AgentProperties props = CreateTwoSkillProperties();
        props.DeadlineMinutes = 5;
        props.MaxBudgetUsd = 0.25m;
        props.MaxRetainedOutputMib = 2;

        var clone = (AgentProperties)props.Clone();

        Assert.Equal(5, clone.DeadlineMinutes);
        Assert.Equal(0.25m, clone.MaxBudgetUsd);
        Assert.Equal(2, clone.MaxRetainedOutputMib);
        Assert.Null(clone.MaxTurns);
    }

    [Fact]
    public void Fuses_AreNeverTemplated()
    {
        AgentProperties props = CreateTwoSkillProperties();
        props.DeadlineMinutes = 5;

        props.SetExecutionState(false, new Dictionary<string, string?> { ["ClientName"] = "Acme" });

        Assert.Equal(5, props.DeadlineMinutes);
        Assert.DoesNotContain(
            nameof(AgentProperties.DeadlineMinutes),
            props.GetAllPlaceholders(),
            StringComparer.Ordinal);
    }

    private static BlockProperties? NewtonsoftRoundTrip(AgentProperties props)
    {
        return DeserializeViaConverter(JsonConvert.SerializeObject(props));
    }

    [Fact]
    public void NewtonsoftRoundTrip_PreservesSkillsAndModel()
    {
        AgentProperties props = CreateTwoSkillProperties();

        BlockProperties? deserialized = NewtonsoftRoundTrip(props);

        var agentProps = Assert.IsType<AgentProperties>(deserialized);
        Assert.Equal(BlockTypes.Agent, agentProps.Type);
        Assert.Equal("test-model-a", agentProps.Model);
        Assert.Equal(EXTRACTION_SKILL, SkillContent(agentProps, "extraction"));
        Assert.Equal(SUMMARY_SKILL, SkillContent(agentProps, "summary"));
    }

    [Fact]
    public void Deserialize_LegacyNameToContentSkills_LoadsThroughNewtonsoft()
    {
        BlockProperties? deserialized = DeserializeViaConverter(
            """{"Type":28,"WorkerSkills":{"SKILL.md":"Extract.","summary":"Summarize."},"Model":"test-model-a"}""");

        var agentProps = Assert.IsType<AgentProperties>(deserialized);
        Assert.Equal(2, agentProps.WorkerSkills.Count);
        Assert.Equal("Extract.", SkillContent(agentProps, "SKILL.md"));
        Assert.All(agentProps.WorkerSkills, skill => Assert.Equal(string.Empty, skill.Description));
    }

    [Fact]
    public void GetAllPlaceholders_DiscoversEngineTagsInSkillDescriptions()
    {
        var props = new AgentProperties
        {
            WorkerSkills = [Skill("extraction", "Plain content.")],
        };
        props.WorkerSkills[0].Description = "Use when {{PortfolioSummary}} is present; {inert} stays out.";

        List<string> placeholders = props.GetAllPlaceholders();

        Assert.Contains("PortfolioSummary", placeholders);
        Assert.DoesNotContain("inert", placeholders);
    }

    [Fact]
    public void Clone_DoesNotShareSkillInstances()
    {
        AgentProperties props = CreateTwoSkillProperties();

        var clone = (AgentProperties)props.Clone();
        clone.WorkerSkills.Single(skill => skill.Name == "extraction").Content = "edited on the clone";

        Assert.Equal(EXTRACTION_SKILL, SkillContent(props, "extraction"));
    }

    [Fact]
    public void Deserialize_RetiredFuseSwitches_AreIgnoredAndTheValuesSurvive()
    {
        BlockProperties? deserialized = DeserializeViaConverter(
            """
            {"Type":28,"Model":"test-model-a","RunDeadlineEnabled":false,"MaxTurnsEnabled":false,
             "MaxBudgetEnabled":true,"MaxRetainedOutputEnabled":true,"DeadlineMinutes":9,"MaxTurns":12}
            """);

        var agentProps = Assert.IsType<AgentProperties>(deserialized);
        Assert.Equal(9, agentProps.DeadlineMinutes);
        Assert.Equal(12, agentProps.MaxTurns);
        Assert.Null(agentProps.MaxBudgetUsd);
        Assert.Null(agentProps.MaxRetainedOutputMib);
    }

    [Fact]
    public void Deserialize_ExplicitNullWorkerSkills_KeepsEmptyDictionary()
    {
        BlockProperties? deserialized = DeserializeViaConverter(
            """{"Type":28,"WorkerSkills":null,"Model":"test-model-a"}""");

        var agentProps = Assert.IsType<AgentProperties>(deserialized);
        Assert.NotNull(agentProps.WorkerSkills);
        Assert.Empty(agentProps.WorkerSkills);
        Assert.Empty(agentProps.GetAllPlaceholders());
    }

    [Fact]
    public void NewtonsoftRoundTrip_PreservesExecutionState()
    {
        AgentProperties props = CreateTwoSkillProperties();
        props.SetExecutionState(true, []);
        props.ExecutionState!.WorkerSkills.Single(skill => skill.Name == "extraction").Content = "rendered for run 42";

        BlockProperties? deserialized = NewtonsoftRoundTrip(props);

        var agentProps = Assert.IsType<AgentProperties>(deserialized);
        Assert.NotNull(agentProps.ExecutionState);
        Assert.Equal("rendered for run 42", ExecutedSkillContent(agentProps, "extraction"));
    }
}
