using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Tests.Data.Workflows;

public class WorkflowVariableTest
{
    [Fact]
    public void Constructor_WithWorkflow_ShouldInitializeCorrectly()
    {
        // Arrange
        var workflow = new Workflow { Id = Guid.NewGuid().ToString() };

        // Act
        var variable = new WorkflowVariable(workflow);

        // Assert
        Assert.NotNull(variable);
        Assert.Equal(workflow, variable.Workflow);
        Assert.Equal(workflow.Id, variable.WorkflowId);
        Assert.Equal("workflowVariable_1", variable.ReplacementTag);
        Assert.Equal("workflowVariable_1", variable.DisplayName);
        Assert.Contains(variable, workflow.Variables);
    }

    [Fact]
    public void Constructor_WithExistingReplacementTag_ShouldGenerateNewTag()
    {
        // Arrange
        var workflow = new Workflow { Id = Guid.NewGuid().ToString() };
        _ = new WorkflowVariable(workflow, "customTag");

        // Act
        var variable = new WorkflowVariable(workflow, "customTag");

        // Assert
        Assert.Equal("workflowVariable_2", variable.ReplacementTag);
        Assert.Equal(2, workflow.Variables.Count);
    }

    [Fact]
    public void Constructor_WithCustomReplacementTag_ShouldUseProvidedTag()
    {
        // Arrange
        var workflow = new Workflow { Id = Guid.NewGuid().ToString() };

        // Act
        var variable = new WorkflowVariable(workflow, "customTag");

        // Assert
        Assert.Equal("customTag", variable.ReplacementTag);
        Assert.Equal("customTag", variable.DisplayName);
    }

    [Fact]
    public void CloneToWorkflow_ShouldCopyAllProperties()
    {
        // Arrange
        var sourceWorkflow = new Workflow { Id = Guid.NewGuid().ToString() };
        var targetWorkflow = new Workflow { Id = Guid.NewGuid().ToString() };
        var sourceVariable = new WorkflowVariable(sourceWorkflow)
        {
            ReplacementTag = "customTag",
            DisplayName = "Test Variable",
            Description = "This is a test variable.",
            Type = WorkflowVariableTypes.String,
            IsRequired = true,
            DefaultValue = "Default",
            Options = new Dictionary<string, string> { { "Option1", "Option1" }, { "Option2", "Option2" } }
        };
        sourceWorkflow.Variables.Add(sourceVariable);

        // Act
        WorkflowVariable clonedVariable = sourceVariable.CloneToWorkflow(targetWorkflow);

        // Assert
        Assert.NotNull(clonedVariable);
        Assert.Equal(targetWorkflow, clonedVariable.Workflow);
        Assert.Equal(targetWorkflow.Id, clonedVariable.WorkflowId);
        Assert.Equal("customTag", clonedVariable.ReplacementTag);
        Assert.Equal("Test Variable", clonedVariable.DisplayName);
        Assert.Equal("This is a test variable.", clonedVariable.Description);
        Assert.Equal(WorkflowVariableTypes.String, clonedVariable.Type);
        Assert.True(clonedVariable.IsRequired);
        Assert.Equal("Default", clonedVariable.DefaultValue);
        Assert.Equal(2, clonedVariable.Options.Count);
        _ = Assert.Contains("Option1", clonedVariable.Options);
        _ = Assert.Contains("Option2", clonedVariable.Options);
    }

    [Theory]
    [InlineData(WorkflowVariableTypes.Number, "1.50", "1.50")]
    [InlineData(WorkflowVariableTypes.Number, "n/a", "\"n/a\"")]
    [InlineData(WorkflowVariableTypes.List, "a | b", "[\"a\",\"b\"]")]
    [InlineData(WorkflowVariableTypes.MultiSelect, "x|y", "[\"x\",\"y\"]")]
    [InlineData(WorkflowVariableTypes.DateTime, "2025-01-31", "\"2025-01-31T00:00\"")]
    [InlineData(WorkflowVariableTypes.DateTime, "2025-01-31T09:30", "\"2025-01-31T09:30\"")]
    [InlineData(WorkflowVariableTypes.String, "plain", "\"plain\"")]
    public void DefaultValueAsJson_ConvertsTheDefaultLikeASubmittedValue(
        WorkflowVariableTypes type, string defaultValue, string expectedJson)
    {
        var variable = new WorkflowVariable(new Workflow(), "Tag") { Type = type, DefaultValue = defaultValue };

        System.Text.Json.JsonElement? json = variable.DefaultValueAsJson();

        Assert.Equal(expectedJson, json!.Value.GetRawText());
    }

    [Fact]
    public void DefaultValueAsJson_NoDefault_ReturnsNull()
    {
        var variable = new WorkflowVariable(new Workflow(), "Tag") { Type = WorkflowVariableTypes.List };

        Assert.Null(variable.DefaultValueAsJson());
    }
}
