using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApp.Services;

namespace IniaAi.WebApp.Tests.Services;

public class ReplacementTagUiValidationServiceTests
{
    [Fact]
    public void ValidateAllTextFieldsForBlock_IgnoresUserPromptTemplatePreview()
    {
        // UserPromptTemplatePreview is AI-generated / execution-output text, not an authored
        // template. A stale or fallback preview containing braces must not raise validation warnings.
        var workflow = new Workflow("wf");
        var section = new Section("s", workflow);
        var block = new Block(BlockTypes.AiQuery, section)
        {
            ReplacementTag = "b",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "All good.",
                UserPromptTemplatePreview = "Stale preview using {nonexistent_preview_tag}.",
            },
        };

        List<string> result = ReplacementTagUiValidationService.ValidateAllTextFieldsForBlock(block, workflow);

        Assert.DoesNotContain("{nonexistent_preview_tag}", result);
    }

    [Fact]
    public void ValidateAllTextFieldsForBlock_WithInvalidTagInTextInputValue_ReturnsInvalidTags()
    {
        // Arrange
        Workflow workflow = CreateWorkflowWithBlocks();
        Block block = workflow.GetAllBlocks().First();

        // Add a TextInput with an invalid replacement tag in its Value
        _ = new TextInput(block, "input_1")
        {
            Value = "Use {nonexistent_block} for analysis"
        };

        // Act
        List<string> result = ReplacementTagUiValidationService.ValidateAllTextFieldsForBlock(block, workflow);

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains("{nonexistent_block}", result);
    }

    [Fact]
    public void ValidateAllTextFieldsForBlock_WithValidTagInTextInputValue_ReturnsEmpty()
    {
        // Arrange
        Workflow workflow = CreateWorkflowWithBlocks();
        Block block = workflow.GetAllBlocks().First();

        // Add a TextInput with a valid replacement tag in its Value
        _ = new TextInput(block, "input_1")
        {
            Value = "Use {block_1} for analysis"
        };

        // Act
        List<string> result = ReplacementTagUiValidationService.ValidateAllTextFieldsForBlock(block, workflow);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ValidateAllTextFieldsForBlock_WithMultipleTextInputs_ValidatesAll()
    {
        // Arrange
        Workflow workflow = CreateWorkflowWithBlocks();
        Block block = workflow.GetAllBlocks().First();

        // Add multiple TextInputs
        _ = new TextInput(block, "input_1")
        {
            Value = "Use {invalid_tag_1} here"
        };

        _ = new TextInput(block, "input_2")
        {
            Value = "And {invalid_tag_2} here"
        };

        // Act
        List<string> result = ReplacementTagUiValidationService.ValidateAllTextFieldsForBlock(block, workflow);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("{invalid_tag_1}", result);
        Assert.Contains("{invalid_tag_2}", result);
    }

    [Fact]
    public void ValidateAllTextFieldsForBlock_WithEmptyTextInputValue_ReturnsEmpty()
    {
        // Arrange
        Workflow workflow = CreateWorkflowWithBlocks();
        Block block = workflow.GetAllBlocks().First();

        // Add a TextInput with empty/null Value
        _ = new TextInput(block, "input_1")
        {
            Value = null
        };

        // Act
        List<string> result = ReplacementTagUiValidationService.ValidateAllTextFieldsForBlock(block, workflow);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ValidateAllTextFieldsForBlock_WithTextInputAndPromptTemplate_ValidatesBoth()
    {
        // Arrange
        Workflow workflow = CreateWorkflowWithBlocks();
        Block block = workflow.GetAllBlocks().First();
        block.Properties = new AiQueryProperties
        {
            UserPromptTemplate = "Check {invalid_prompt_tag}",
        };

        // Add a TextInput with an invalid tag
        _ = new TextInput(block, "input_1")
        {
            Value = "Use {invalid_input_tag} here"
        };

        // Act
        List<string> result = ReplacementTagUiValidationService.ValidateAllTextFieldsForBlock(block, workflow);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("{invalid_prompt_tag}", result);
        Assert.Contains("{invalid_input_tag}", result);
    }

    [Fact]
    public void ValidateAllTextFieldsForBlock_WithSystemReservedTagInTextInput_ReturnsEmpty()
    {
        // Arrange
        Workflow workflow = CreateWorkflowWithBlocks();
        Block block = workflow.GetAllBlocks().First();

        // Add a TextInput with a system reserved tag
        _ = new TextInput(block, "input_1")
        {
            Value = "Use {InputContext} for analysis"
        };

        // Act
        List<string> result = ReplacementTagUiValidationService.ValidateAllTextFieldsForBlock(block, workflow);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ValidateAllTextFieldsForBlock_WithWorkflowVariableInTextInput_ReturnsEmpty()
    {
        // Arrange
        Workflow workflow = CreateWorkflowWithBlocks();
        workflow.Variables.Add(new WorkflowVariable(workflow)
        {
            ReplacementTag = "company_name"
        });
        Block block = workflow.GetAllBlocks().First();

        // Add a TextInput with a workflow variable
        _ = new TextInput(block, "input_1")
        {
            Value = "Analyze {company_name} data"
        };

        // Act
        List<string> result = ReplacementTagUiValidationService.ValidateAllTextFieldsForBlock(block, workflow);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ValidateAllTextFieldsForBlock_WithTextInputReferencingAnotherTextInputOnSameBlock_ReturnsEmpty()
    {
        // Arrange
        Workflow workflow = CreateWorkflowWithBlocks();
        Block block = workflow.GetAllBlocks().First();

        _ = new TextInput(block, "company_name")
        {
            Value = "Acme Corp"
        };

        _ = new TextInput(block, "analysis_prompt")
        {
            Value = "Analyze {company_name} for investment potential"
        };

        // Act
        List<string> result = ReplacementTagUiValidationService.ValidateAllTextFieldsForBlock(block, workflow);

        // Assert - {company_name} should be valid since it's a TextInput on the same block
        Assert.Empty(result);
    }

    private static Workflow CreateWorkflowWithBlocks()
    {
        var workflow = new Workflow("test-workflow");
        var section = new Section("default", workflow);

        _ = new Block(BlockTypes.AiQuery, section)
        {
            Id = Guid.NewGuid().ToString(),
            ReplacementTag = "block_0",
        };

        _ = new Block(BlockTypes.AiQuery, section)
        {
            Id = Guid.NewGuid().ToString(),
            ReplacementTag = "block_1",
        };

        return workflow;
    }
}
