using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;

namespace IniaAi.Core.Tests.Workflows;

/// <summary>
/// Focused unit tests for <see cref="WorkflowHandler.InjectSignatureFieldValuesForBlock"/>,
/// the pure injection step that resolves workflow-level
/// <see cref="WorkflowSignatureField"/> definitions into a block's inputs prior to
/// execution. Definitions live on the workflow; overrides come pre-loaded as a
/// name-keyed map (one row per org + workflow type + field name); a block receives a
/// value only for fields whose <c>{name}</c> token its templates actually reference.
/// </summary>
public class WorkflowHandlerSignatureInjectionTests
{
    [Fact]
    public void Inject_OverridePresent_OverrideWins()
    {
        Workflow workflow = WorkflowWithField("tone", defaultValue: "formal");
        var props = new MergeProperties { TextTemplate = "Use {tone} please." };
        var overrides = new Dictionary<string, string> { ["tone"] = "cheerful" };
        var inputs = new Dictionary<string, string?>();

        int matched = WorkflowHandler.InjectSignatureFieldValuesForBlock(
            props, workflow.SignatureFields, overrides, inputs);

        Assert.Equal(1, matched);
        Assert.Single(inputs);
        Assert.Equal("cheerful", inputs["tone"]);
    }

    [Fact]
    public void Inject_NoOverride_DefaultValueUsed()
    {
        Workflow workflow = WorkflowWithField("tone", defaultValue: "formal");
        var props = new MergeProperties { TextTemplate = "Use {tone} please." };
        var inputs = new Dictionary<string, string?>();

        int matched = WorkflowHandler.InjectSignatureFieldValuesForBlock(
            props, workflow.SignatureFields, new Dictionary<string, string>(), inputs);

        Assert.Equal(0, matched);
        Assert.Single(inputs);
        Assert.Equal("formal", inputs["tone"]);
    }

    [Fact]
    public void Inject_NullOverrides_AllDefaultsUsed()
    {
        // Null means "not loaded" (e.g. no tenant identity yet) — staff defaults still inject.
        var workflow = new Workflow("w");
        _ = new WorkflowSignatureField(workflow, "tone") { DefaultValue = "formal" };
        _ = new WorkflowSignatureField(workflow, "language") { DefaultValue = "English" };
        var props = new MergeProperties { TextTemplate = "{tone} in {language}" };
        var inputs = new Dictionary<string, string?>();

        int matched = WorkflowHandler.InjectSignatureFieldValuesForBlock(
            props, workflow.SignatureFields, activeOverrides: null, inputs);

        Assert.Equal(0, matched);
        Assert.Equal(2, inputs.Count);
        Assert.Equal("formal", inputs["tone"]);
        Assert.Equal("English", inputs["language"]);
    }

    [Fact]
    public void Inject_OnlyReferencedFieldsAreInjected()
    {
        // The workflow declares two fields but this block's template only references
        // {tone} — {language} must not leak into the block's inputs.
        var workflow = new Workflow("w");
        _ = new WorkflowSignatureField(workflow, "tone") { DefaultValue = "formal" };
        _ = new WorkflowSignatureField(workflow, "language") { DefaultValue = "English" };
        var props = new MergeProperties { TextTemplate = "Use {tone} please." };
        var inputs = new Dictionary<string, string?>();

        _ = WorkflowHandler.InjectSignatureFieldValuesForBlock(
            props, workflow.SignatureFields, activeOverrides: null, inputs);

        Assert.Single(inputs);
        Assert.True(inputs.ContainsKey("tone"));
        Assert.False(inputs.ContainsKey("language"));
    }

    [Fact]
    public void Inject_OverrideForUnreferencedField_NotInjectedNotCounted()
    {
        Workflow workflow = WorkflowWithField("language", defaultValue: "English");
        var props = new MergeProperties { TextTemplate = "No tokens here." };
        var overrides = new Dictionary<string, string> { ["language"] = "French" };
        var inputs = new Dictionary<string, string?>();

        int matched = WorkflowHandler.InjectSignatureFieldValuesForBlock(
            props, workflow.SignatureFields, overrides, inputs);

        Assert.Equal(0, matched);
        Assert.Empty(inputs);
    }

    [Fact]
    public void Inject_TokenMatchIsCaseInsensitive()
    {
        // The template carries {TONE}; the definition is named "tone". The reference
        // check is OrdinalIgnoreCase, and the input lands under the definition's name.
        Workflow workflow = WorkflowWithField("tone", defaultValue: "formal");
        var props = new MergeProperties { TextTemplate = "Use {TONE} please." };
        var inputs = new Dictionary<string, string?>();

        _ = WorkflowHandler.InjectSignatureFieldValuesForBlock(
            props, workflow.SignatureFields, activeOverrides: null, inputs);

        Assert.Single(inputs);
        Assert.Equal("formal", inputs["tone"]);
    }

    [Fact]
    public void Inject_MixedOverridesAndDefaults_ReturnsOverrideMatchCount()
    {
        var workflow = new Workflow("w");
        _ = new WorkflowSignatureField(workflow, "cogs") { DefaultValue = "default_cogs" };
        _ = new WorkflowSignatureField(workflow, "tone") { DefaultValue = "formal" };
        _ = new WorkflowSignatureField(workflow, "language") { DefaultValue = "English" };
        var props = new MergeProperties { TextTemplate = "{cogs} {tone} {language}" };
        var overrides = new Dictionary<string, string>
        {
            ["cogs"] = "override_cogs",
            ["language"] = "French",
        };
        var inputs = new Dictionary<string, string?>();

        int matched = WorkflowHandler.InjectSignatureFieldValuesForBlock(
            props, workflow.SignatureFields, overrides, inputs);

        Assert.Equal(2, matched);
        Assert.Equal(3, inputs.Count);
        Assert.Equal("override_cogs", inputs["cogs"]);
        Assert.Equal("formal", inputs["tone"]);
        Assert.Equal("French", inputs["language"]);
    }

    [Fact]
    public void Inject_PreexistingInputWithSameName_IsOverwrittenByResolvedValue()
    {
        Workflow workflow = WorkflowWithField("tone", defaultValue: "formal");
        var props = new MergeProperties { TextTemplate = "Use {tone} please." };
        var overrides = new Dictionary<string, string> { ["tone"] = "override_wins" };
        var inputs = new Dictionary<string, string?> { ["tone"] = "preexisting" };

        _ = WorkflowHandler.InjectSignatureFieldValuesForBlock(
            props, workflow.SignatureFields, overrides, inputs);

        Assert.Equal("override_wins", inputs["tone"]);
    }

    [Fact]
    public void Inject_EmptyDefinitions_NoOp()
    {
        var props = new MergeProperties { TextTemplate = "Use {tone} please." };
        var overrides = new Dictionary<string, string> { ["tone"] = "irrelevant" };
        var inputs = new Dictionary<string, string?>();

        int matched = WorkflowHandler.InjectSignatureFieldValuesForBlock(
            props, [], overrides, inputs);

        Assert.Equal(0, matched);
        Assert.Empty(inputs);
    }

    [Fact]
    public void Inject_WhitespaceDefinitionName_Skipped()
    {
        var workflow = new Workflow("w");
        _ = new WorkflowSignatureField(workflow, "   ") { DefaultValue = "junk" };
        var props = new MergeProperties { TextTemplate = "Use {   } please." };
        var inputs = new Dictionary<string, string?>();

        int matched = WorkflowHandler.InjectSignatureFieldValuesForBlock(
            props, workflow.SignatureFields, activeOverrides: null, inputs);

        Assert.Equal(0, matched);
        Assert.Empty(inputs);
    }

    [Fact]
    public void Inject_AiQueryProperties_RecordsReferencedNamesAsCandidates()
    {
        // The injector must stamp CandidateSignatureFieldNames with exactly the
        // referenced definition names so replay capture can tell signature values
        // apart from ordinary inputs.
        var workflow = new Workflow("w");
        _ = new WorkflowSignatureField(workflow, "tone") { DefaultValue = "formal" };
        _ = new WorkflowSignatureField(workflow, "language") { DefaultValue = "English" };
        _ = new WorkflowSignatureField(workflow, "unreferenced") { DefaultValue = "x" };
        var props = new AiQueryProperties
        {
            UserPromptTemplate = "Speak {language}.",
            SystemPromptTemplate = "Always use {tone}.",
        };
        var inputs = new Dictionary<string, string?>();

        _ = WorkflowHandler.InjectSignatureFieldValuesForBlock(
            props, workflow.SignatureFields, activeOverrides: null, inputs);

        Assert.NotNull(props.CandidateSignatureFieldNames);
        Assert.Equal(2, props.CandidateSignatureFieldNames!.Count);
        Assert.Contains("tone", props.CandidateSignatureFieldNames);
        Assert.Contains("language", props.CandidateSignatureFieldNames);
        Assert.DoesNotContain("unreferenced", props.CandidateSignatureFieldNames);
    }

    [Fact]
    public void Inject_AiQueryPropertiesWithNoReferencedFields_ClearsStaleCandidates()
    {
        // A previous execution pass may have stamped candidates; when no definition
        // token appears in the current templates the injector must reset to null so
        // capture can't attribute stale names.
        Workflow workflow = WorkflowWithField("tone", defaultValue: "formal");
        var props = new AiQueryProperties
        {
            UserPromptTemplate = "No tokens here.",
            CandidateSignatureFieldNames = ["stale"],
        };
        var inputs = new Dictionary<string, string?>();

        _ = WorkflowHandler.InjectSignatureFieldValuesForBlock(
            props, workflow.SignatureFields, activeOverrides: null, inputs);

        Assert.Null(props.CandidateSignatureFieldNames);
    }

    [Fact]
    public void Inject_ExpansionVariantBlock_SameNameResolvesWithoutTranslation()
    {
        // Expansion clones (e.g. a parameterized _F2 variant) carry the same {tone}
        // token as their template block. Overrides are keyed by field name only, so
        // both the original and the variant resolve identically — no per-block id
        // translation exists anymore.
        Workflow workflow = WorkflowWithField("tone", defaultValue: "formal");
        var templateProps = new MergeProperties { TextTemplate = "Use {tone} please." };
        var variantProps = (MergeProperties)templateProps.Clone();
        var overrides = new Dictionary<string, string> { ["tone"] = "cheerful" };

        var templateInputs = new Dictionary<string, string?>();
        var variantInputs = new Dictionary<string, string?>();

        int templateMatched = WorkflowHandler.InjectSignatureFieldValuesForBlock(
            templateProps, workflow.SignatureFields, overrides, templateInputs);
        int variantMatched = WorkflowHandler.InjectSignatureFieldValuesForBlock(
            variantProps, workflow.SignatureFields, overrides, variantInputs);

        Assert.Equal(1, templateMatched);
        Assert.Equal(1, variantMatched);
        Assert.Equal("cheerful", templateInputs["tone"]);
        Assert.Equal("cheerful", variantInputs["tone"]);
    }

    [Fact]
    public void EndToEnd_MergeBlock_TextTemplateSubstitutesSignatureOverride()
    {
        // Full chain: inject the resolved signature value into inputs, then run the
        // template-substitution step that MergeProperties.SetExecutionState performs.
        // WorkflowHandler always passes asPlaceHolder:true so {tone} is matched literally.
        Workflow workflow = WorkflowWithField("tone", defaultValue: "formal");
        var props = new MergeProperties { TextTemplate = "Use {tone} please." };
        var overrides = new Dictionary<string, string> { ["tone"] = "cheerful" };
        var inputs = new Dictionary<string, string?>();

        _ = WorkflowHandler.InjectSignatureFieldValuesForBlock(
            props, workflow.SignatureFields, overrides, inputs);
        props.SetExecutionState(asPlaceHolder: true, inputs);

        Assert.NotNull(props.ExecutionState);
        Assert.Equal("Use cheerful please.", props.ExecutionState!.Text);
    }

    private static Workflow WorkflowWithField(string name, string defaultValue)
    {
        var workflow = new Workflow("w");
        _ = new WorkflowSignatureField(workflow, name)
        {
            DisplayName = name,
            Description = "desc",
            DefaultValue = defaultValue,
        };

        return workflow;
    }
}
