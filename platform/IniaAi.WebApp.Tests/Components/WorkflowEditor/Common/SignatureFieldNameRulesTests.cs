using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApp.Components.WorkflowEditor.Common;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Common;

public class SignatureFieldNameRulesTests
{
    private static Workflow NewWorkflow()
    {
        return new Workflow("wf") { Title = "wf" };
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingName_ReturnsError(string? name)
    {
        Workflow workflow = NewWorkflow();

        string? error = SignatureFieldNameRules.Validate(workflow, name, existingField: null);

        Assert.Equal("Field name is required", error);
    }

    [Fact]
    public void Validate_DuplicateNameCaseInsensitive_ReturnsError()
    {
        Workflow workflow = NewWorkflow();
        _ = new WorkflowSignatureField(workflow, "Signed_By");

        string? error = SignatureFieldNameRules.Validate(workflow, "signed_by", existingField: null);

        Assert.NotNull(error);
        Assert.Contains("already exists", error);
    }

    [Fact]
    public void Validate_EditedFieldKeepsOwnName_ReturnsNull()
    {
        Workflow workflow = NewWorkflow();
        var field = new WorkflowSignatureField(workflow, "Signed_By");

        string? error = SignatureFieldNameRules.Validate(workflow, "SIGNED_BY", field);

        Assert.Null(error);
    }

    [Fact]
    public void Validate_NameCollidesWithBlockReplacementTag_ReturnsError()
    {
        Workflow workflow = NewWorkflow();
        var section = new Section("section", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "block_tag");

        string? error = SignatureFieldNameRules.Validate(workflow, "BLOCK_TAG", existingField: null);

        Assert.NotNull(error);
        Assert.Contains("ReplacementTag", error);
    }

    [Fact]
    public void Validate_NameCollidesWithWorkflowVariableTag_ReturnsError()
    {
        Workflow workflow = NewWorkflow();
        _ = new WorkflowVariable(workflow, "var_tag");

        string? error = SignatureFieldNameRules.Validate(workflow, "VAR_TAG", existingField: null);

        Assert.NotNull(error);
        Assert.Contains("variable tag", error);
    }

    [Fact]
    public void Validate_ValidName_ReturnsNull()
    {
        Workflow workflow = NewWorkflow();
        var section = new Section("section", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "block_tag");
        _ = new WorkflowSignatureField(workflow, "existing_field");
        _ = new WorkflowVariable(workflow, "var_tag");

        string? error = SignatureFieldNameRules.Validate(workflow, "fresh_name", existingField: null);

        Assert.Null(error);
    }
}
