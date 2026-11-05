using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json.Linq;

namespace IniaAi.Domain.Tests.Serialization;

public class WorkflowFormatVersionTests
{
    [Fact]
    public void ToExportJson_StampsCurrentFormatVersion()
    {
        string exported = BuildWorkflow().ToExportJson();

        var root = JObject.Parse(exported);
        Assert.Equal(Workflow.CURRENT_FORMAT_VERSION, (int?)root[nameof(Workflow.FormatVersion)]);
    }

    [Fact]
    public void ToExportJson_RoundTripsFormatVersion()
    {
        var restored = Workflow.FromJson(BuildWorkflow().ToExportJson());

        Assert.NotNull(restored);
        Assert.Equal(Workflow.CURRENT_FORMAT_VERSION, restored.FormatVersion);
    }

    [Fact]
    public void ToExportJson_DoesNotMutateWorkflow()
    {
        Workflow workflow = BuildWorkflow();

        _ = workflow.ToExportJson();

        Assert.Null(workflow.FormatVersion);
    }

    [Fact]
    public void ToJson_WithoutStamp_OmitsFormatVersion()
    {
        Assert.DoesNotContain(
            nameof(Workflow.FormatVersion),
            BuildWorkflow().ToJson(pretty: false));
    }

    [Fact]
    public void FromJson_LegacyJsonWithoutFormatVersion_LeavesNull()
    {
        var restored = Workflow.FromJson(BuildWorkflow().ToJson(pretty: false));

        Assert.NotNull(restored);
        Assert.Null(restored.FormatVersion);
    }

    [Fact]
    public void DeclaresUnsupportedFormatVersion_NewerVersion_True()
    {
        string json = ExportedJsonWithFormatVersion(Workflow.CURRENT_FORMAT_VERSION + 1);

        bool unsupported = Workflow.DeclaresUnsupportedFormatVersion(json, out int declared);

        Assert.True(unsupported);
        Assert.Equal(Workflow.CURRENT_FORMAT_VERSION + 1, declared);
    }

    [Fact]
    public void DeclaresUnsupportedFormatVersion_CurrentVersion_False()
    {
        string json = ExportedJsonWithFormatVersion(Workflow.CURRENT_FORMAT_VERSION);

        Assert.False(Workflow.DeclaresUnsupportedFormatVersion(json, out _));
    }

    [Fact]
    public void DeclaresUnsupportedFormatVersion_MissingFormatVersion_False()
    {
        string json = BuildWorkflow().ToJson(pretty: false);

        Assert.False(Workflow.DeclaresUnsupportedFormatVersion(json, out _));
    }

    [Fact]
    public void DeclaresUnsupportedFormatVersion_MalformedJson_False()
    {
        Assert.False(Workflow.DeclaresUnsupportedFormatVersion("{not json", out _));
    }

    [Fact]
    public void DeclaresUnsupportedFormatVersion_BeyondInt32_TrueWithoutThrowing()
    {
        string json = ExportedJsonWithRawFormatVersion("3000000000");

        bool unsupported = Workflow.DeclaresUnsupportedFormatVersion(json, out int declared);

        Assert.True(unsupported);
        Assert.Equal(int.MaxValue, declared);
    }

    [Fact]
    public void DeclaresUnsupportedFormatVersion_BeyondInt64_TrueWithoutThrowing()
    {
        string json = ExportedJsonWithRawFormatVersion("99999999999999999999999999");

        bool unsupported = Workflow.DeclaresUnsupportedFormatVersion(json, out int declared);

        Assert.True(unsupported);
        Assert.Equal(int.MaxValue, declared);
    }

    [Fact]
    public void DeclaresUnsupportedFormatVersion_HugeNegative_False()
    {
        string json = ExportedJsonWithRawFormatVersion("-3000000000");

        Assert.False(Workflow.DeclaresUnsupportedFormatVersion(json, out _));
    }

    [Fact]
    public void DeclaresUnsupportedFormatVersion_NewerVersionWrappedInNoise_True()
    {
        string json = "model output preamble\n"
            + ExportedJsonWithFormatVersion(Workflow.CURRENT_FORMAT_VERSION + 1)
            + "\ntrailing noise";

        bool unsupported = Workflow.DeclaresUnsupportedFormatVersion(json, out int declared);

        Assert.True(unsupported);
        Assert.Equal(Workflow.CURRENT_FORMAT_VERSION + 1, declared);
    }

    [Fact]
    public void FromJson_NoiseWrappedStampedJson_RoundTripsFormatVersion()
    {
        string json = "model output preamble\n"
            + ExportedJsonWithFormatVersion(Workflow.CURRENT_FORMAT_VERSION + 1)
            + "\ntrailing noise";

        var restored = Workflow.FromJson(json);

        Assert.NotNull(restored);
        Assert.Equal(Workflow.CURRENT_FORMAT_VERSION + 1, restored.FormatVersion);
    }

    [Fact]
    public void FromJson_FileWithTheRemovedCitationExclusionField_Loads()
    {
        var root = JObject.Parse(BuildWorkflow().ToJson(pretty: false));
        var savedBlock = (JObject)Assert.Single(root.SelectTokens("$.Sections[*].Blocks[*]"));
        savedBlock["ShouldExcludeBlockOutputFromCitationSources"] = true;

        var restored = Workflow.FromJson(root.ToString());

        Assert.NotNull(restored);
        Assert.Equal("block1", Assert.Single(restored.GetAllBlocks()).ReplacementTag);
    }

    [Fact]
    public void UnsupportedFormatVersionMessage_NamesBothVersions()
    {
        string message = Workflow.UnsupportedFormatVersionMessage(
            Workflow.CURRENT_FORMAT_VERSION + 1);

        Assert.Contains($"{Workflow.CURRENT_FORMAT_VERSION + 1}", message);
        Assert.Contains($"{Workflow.CURRENT_FORMAT_VERSION}", message);
    }

    private static string ExportedJsonWithFormatVersion(int formatVersion)
    {
        return ExportedJsonWithRawFormatVersion(formatVersion.ToString());
    }

    private static string ExportedJsonWithRawFormatVersion(string rawToken)
    {
        var root = JObject.Parse(BuildWorkflow().ToJson(pretty: false));
        root[nameof(Workflow.FormatVersion)] = JToken.Parse(rawToken);

        return root.ToString();
    }

    private static Workflow BuildWorkflow()
    {
        var workflow = new Workflow("Report") { WorkflowTypeId = "t" };
        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "block1")
        {
            Name = "Counterparty",
            Properties = new AiQueryProperties { UserPromptTemplate = "prompt" },
        };

        return workflow;
    }
}
