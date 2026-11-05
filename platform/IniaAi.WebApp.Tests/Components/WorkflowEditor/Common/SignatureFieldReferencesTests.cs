using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApp.Components.WorkflowEditor.Common;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Common;

public class SignatureFieldReferencesTests
{
    private static Workflow NewWorkflow()
    {
        return new Workflow("wf") { Title = "wf" };
    }

    private static Block NewAiQueryBlock(Section section, string tag, string? userPromptTemplate)
    {
        var block = new Block(BlockTypes.AiQuery, section, tag);
        ((AiQueryProperties)block.Properties).UserPromptTemplate = userPromptTemplate;
        return block;
    }

    private static Block NewMergeBlock(Section section, string tag, string? textTemplate)
    {
        var block = new Block(BlockTypes.Merge, section, tag);
        ((MergeProperties)block.Properties).TextTemplate = textTemplate;
        return block;
    }

    [Fact]
    public void GetReferencedFieldNames_TokenCaseDiffersFromDefinition_StillBinds()
    {
        Workflow workflow = NewWorkflow();
        var section = new Section("section", workflow);
        Block block = NewAiQueryBlock(section, "ai", "Signed by {field_a}");
        _ = new WorkflowSignatureField(workflow, "Field_A");

        List<string> names = SignatureFieldReferences.GetReferencedFieldNames(block, workflow);

        string name = Assert.Single(names);
        Assert.Equal("Field_A", name);
    }

    [Fact]
    public void GetReferencedFieldNames_DoubleBracedToken_StillCountsAsReference()
    {
        // Referencing is Contains-based (the surface runtime substitution operates on),
        // so {{field}} contains {field} and binds. Only GetUnmatchedTokens excludes doubles.
        Workflow workflow = NewWorkflow();
        var section = new Section("section", workflow);
        Block block = NewAiQueryBlock(section, "ai", "Cached: {{field_a}}");
        _ = new WorkflowSignatureField(workflow, "field_a");

        List<string> names = SignatureFieldReferences.GetReferencedFieldNames(block, workflow);
        List<Block> blocks = SignatureFieldReferences.GetReferencingBlocks(workflow, "field_a");

        string name = Assert.Single(names);
        Assert.Equal("field_a", name);
        Block referencing = Assert.Single(blocks);
        Assert.Same(block, referencing);
    }

    [Fact]
    public void GetReferencedFieldNames_NonBearingBlock_ReturnsEmpty()
    {
        Workflow workflow = NewWorkflow();
        var section = new Section("section", workflow);
        var block = new Block(BlockTypes.CalculateNumber, section, "calc");
        _ = new WorkflowSignatureField(workflow, "field_a");

        Assert.Empty(SignatureFieldReferences.GetReferencedFieldNames(block, workflow));
    }

    [Fact]
    public void GetReferencedFieldNames_OrdersByFirstAppearance_ThenUnmatchedInOrderingByName()
    {
        Workflow workflow = NewWorkflow();
        var section = new Section("section", workflow);
        var block = new Block(BlockTypes.AiQuery, section, "ai");
        var properties = (AiQueryProperties)block.Properties;
        properties.TextToCache = "cached {alpha}";
        properties.SystemPromptTemplate = "sys {omega} then {gamma}";
        properties.UserPromptTemplate = "user {gamma} again";
        // Referenced via a template outside the ordering set, so these sort last by name.
        properties.ModelIdOverride = "{zeta_late} {beta_late}";

        _ = new WorkflowSignatureField(workflow, "zeta_late");
        _ = new WorkflowSignatureField(workflow, "gamma");
        _ = new WorkflowSignatureField(workflow, "unused");
        _ = new WorkflowSignatureField(workflow, "omega");
        _ = new WorkflowSignatureField(workflow, "beta_late");
        _ = new WorkflowSignatureField(workflow, "alpha");

        List<string> names = SignatureFieldReferences.GetReferencedFieldNames(block, workflow);

        Assert.Equal(new[] { "alpha", "omega", "gamma", "beta_late", "zeta_late" }, names);
        Assert.DoesNotContain("unused", names);
    }

    [Fact]
    public void GetUnmatchedTokens_ReturnsOnlyStrayTokens()
    {
        Workflow workflow = NewWorkflow();
        var section = new Section("section", workflow);
        Block bearer = NewAiQueryBlock(
            section,
            "bearer_tag",
            "{signed_by} {var_tag} {other_block} {bearer_tag} {unknown_token} {{doubled}} {dotted.ref}");
        _ = NewMergeBlock(section, "other_block", null);
        _ = new WorkflowSignatureField(workflow, "signed_by");
        _ = new WorkflowVariable(workflow, "var_tag");

        List<string> unmatched = SignatureFieldReferences.GetUnmatchedTokens(bearer, workflow);

        string token = Assert.Single(unmatched);
        Assert.Equal("unknown_token", token);
    }

    [Fact]
    public void GetUnmatchedTokens_NonBearingBlock_ReturnsEmpty()
    {
        Workflow workflow = NewWorkflow();
        var section = new Section("section", workflow);
        var block = new Block(BlockTypes.CalculateNumber, section, "calc");

        Assert.Empty(SignatureFieldReferences.GetUnmatchedTokens(block, workflow));
    }

    [Fact]
    public void RenameToken_RewritesEveryReferencingBlockOnly()
    {
        Workflow workflow = NewWorkflow();
        var section = new Section("section", workflow);
        Block aiBlock = NewAiQueryBlock(section, "ai", "Sign: {old_name} please");
        Block mergeBlock = NewMergeBlock(section, "merge", "By {OLD_NAME} done");
        Block untouched = NewMergeBlock(section, "plain", "nothing to rename");
        _ = new WorkflowSignatureField(workflow, "old_name");

        int touched = SignatureFieldReferences.RenameToken(workflow, "old_name", "new_name");

        Assert.Equal(2, touched);
        Assert.Equal("Sign: {new_name} please", ((AiQueryProperties)aiBlock.Properties).UserPromptTemplate);
        Assert.Equal("By {new_name} done", ((MergeProperties)mergeBlock.Properties).TextTemplate);
        Assert.Equal("nothing to rename", ((MergeProperties)untouched.Properties).TextTemplate);
    }

    [Fact]
    public void SharedField_BothBlocksReturned_FieldAppearsOncePerBlock()
    {
        Workflow workflow = NewWorkflow();
        var section = new Section("section", workflow);
        Block aiBlock = NewAiQueryBlock(section, "ai", "Signed by {shared}");
        Block mergeBlock = NewMergeBlock(section, "merge", "Countersigned by {shared}");
        _ = new WorkflowSignatureField(workflow, "shared");

        List<Block> blocks = SignatureFieldReferences.GetReferencingBlocks(workflow, "shared");

        Assert.Equal(2, blocks.Count);
        Assert.Same(aiBlock, blocks[0]);
        Assert.Same(mergeBlock, blocks[1]);
        Assert.Equal(new[] { "shared" }, SignatureFieldReferences.GetReferencedFieldNames(aiBlock, workflow));
        Assert.Equal(new[] { "shared" }, SignatureFieldReferences.GetReferencedFieldNames(mergeBlock, workflow));
    }

    [Fact]
    public void GetReferencingBlocks_UnknownField_ReturnsEmpty()
    {
        Workflow workflow = NewWorkflow();
        var section = new Section("section", workflow);
        _ = NewAiQueryBlock(section, "ai", "Signed by {shared}");
        _ = new WorkflowSignatureField(workflow, "shared");

        Assert.Empty(SignatureFieldReferences.GetReferencingBlocks(workflow, "not_defined"));
    }
}
