using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Tests.Data.Workflows;

public class RegressionSchemaLegacyMigratorTests
{
    private const string LEGACY_SCHEMA =
        """{"aum":{"Type":"Numeric","Tolerance":5,"Source":"xml","Locator":"aum_tag"},"manager":{"Type":"text","Tolerance":0}}""";

    [Fact]
    public void FromJson_LegacyFullShapeSchema_HoistsDefinitionsToWorkflowLevel()
    {
        Workflow workflow = BuildWorkflow(LEGACY_SCHEMA);

        Workflow restored = Workflow.FromJson(workflow.ToJson(pretty: false))!;

        Assert.Equal(2, restored.RegressionFields.Count);

        WorkflowRegressionField aum = restored.RegressionFields.Single(f => f.Name == "aum");
        Assert.Equal(RegressionFieldTypes.NUMERIC, aum.Type);
        Assert.Equal(5m, aum.Tolerance);

        WorkflowRegressionField manager = restored.RegressionFields.Single(f => f.Name == "manager");
        Assert.Equal(RegressionFieldTypes.TEXT, manager.Type);
        Assert.Equal(0m, manager.Tolerance);
    }

    [Fact]
    public void FromJson_LegacyFullShapeSchema_RewritesBlockJsonToBindingsOnly()
    {
        Workflow workflow = BuildWorkflow(LEGACY_SCHEMA);

        Workflow restored = Workflow.FromJson(workflow.ToJson(pretty: false))!;
        string? bindingJson = restored.GetAllBlocks().First().RegressionSchema;

        Assert.False(RegressionSchemaBindings.AnyLegacyKeys(bindingJson));
        Dictionary<string, RegressionFieldBinding> bindings = RegressionSchemaBindings.Parse(bindingJson);
        Assert.Equal(2, bindings.Count);
        Assert.Equal("xml", bindings["aum"].Source);
        Assert.Equal("aum_tag", bindings["aum"].Locator);
        Assert.Null(bindings["manager"].Source);
        Assert.Null(bindings["manager"].Locator);
    }

    [Fact]
    public void FromJson_DuplicateFieldAcrossBlocks_FirstBlockWinsDefinitionAndBinding()
    {
        var workflow = new Workflow("Legacy") { Title = "Legacy" };
        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "first")
        {
            Name = "First",
            Properties = new AiQueryProperties { UserPromptTemplate = "prompt" },
            RegressionSchema = """{"aum":{"Type":"numeric","Tolerance":5,"Source":"xml"}}""",
        };
        _ = new Block(BlockTypes.AiQuery, section, "second")
        {
            Name = "Second",
            Properties = new AiQueryProperties { UserPromptTemplate = "prompt" },
            RegressionSchema = """{"aum":{"Type":"text","Tolerance":9},"other":{"Type":"text","Tolerance":0}}""",
        };

        Workflow restored = Workflow.FromJson(workflow.ToJson(pretty: false))!;

        Assert.Equal(2, restored.RegressionFields.Count);
        WorkflowRegressionField aum = restored.RegressionFields.Single(f => f.Name == "aum");
        Assert.Equal(RegressionFieldTypes.NUMERIC, aum.Type);
        Assert.Equal(5m, aum.Tolerance);

        Block first = restored.GetAllBlocks().Single(b => b.ReplacementTag == "first");
        Assert.True(RegressionSchemaBindings.Parse(first.RegressionSchema).ContainsKey("aum"));

        Block second = restored.GetAllBlocks().Single(b => b.ReplacementTag == "second");
        Dictionary<string, RegressionFieldBinding> secondBindings = RegressionSchemaBindings.Parse(second.RegressionSchema);
        Assert.False(secondBindings.ContainsKey("aum"));
        Assert.True(secondBindings.ContainsKey("other"));
    }

    [Fact]
    public void FromJson_HoistIsIdempotent()
    {
        Workflow workflow = BuildWorkflow(LEGACY_SCHEMA);

        string once = Workflow.FromJson(workflow.ToJson(pretty: false))!.ToJson(pretty: false);
        string twice = Workflow.FromJson(once)!.ToJson(pretty: false);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void FromJson_BindingOnlyJson_LeftUntouched()
    {
        const string bindingOnly = """{"aum":{"Source":"xml","Locator":"aum_tag"}}""";
        Workflow workflow = BuildWorkflow(bindingOnly);
        _ = new WorkflowRegressionField(workflow, "aum");

        Workflow restored = Workflow.FromJson(workflow.ToJson(pretty: false))!;

        Assert.Equal(bindingOnly, restored.GetAllBlocks().First().RegressionSchema);
        WorkflowRegressionField definition = Assert.Single(restored.RegressionFields);
        Assert.Equal("aum", definition.Name);
    }

    [Fact]
    public void FromJson_MalformedSchemaJson_LeftUntouched()
    {
        const string malformed = "{ this is not json";
        Workflow workflow = BuildWorkflow(malformed);

        Workflow restored = Workflow.FromJson(workflow.ToJson(pretty: false))!;

        Assert.Equal(malformed, restored.GetAllBlocks().First().RegressionSchema);
        Assert.Empty(restored.RegressionFields);
    }

    private static Workflow BuildWorkflow(string schemaJson)
    {
        var workflow = new Workflow("Legacy") { Title = "Legacy" };
        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "block1")
        {
            Name = "Block One",
            Properties = new AiQueryProperties { UserPromptTemplate = "prompt" },
            RegressionSchema = schemaJson,
        };

        return workflow;
    }
}
