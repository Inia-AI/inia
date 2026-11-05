using IniaAi.Application.Commands.WorkflowTemplates.SaveWorkflowTemplateContent;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Application.UnitTests.Commands.WorkflowTemplates.SaveWorkflowTemplateContent;

public class SaveWorkflowTemplateContentCommandValidatorTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";
    private const string TYPE = "WT-Report";
    private const string JSON = "{}";

    private readonly SaveWorkflowTemplateContentCommandValidator _validator = new();

    [Fact]
    public void RejectsEmptyRequiredFields()
    {
        Assert.False(_validator.Validate(new SaveWorkflowTemplateContentCommand("", USER, TYPE, JSON, null)).IsValid);
        Assert.False(_validator.Validate(new SaveWorkflowTemplateContentCommand(ORG, "", TYPE, JSON, null)).IsValid);
        Assert.False(_validator.Validate(new SaveWorkflowTemplateContentCommand(ORG, USER, "", JSON, null)).IsValid);
        Assert.False(_validator.Validate(new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE, "", null)).IsValid);
    }

    [Fact]
    public void AcceptsValidCommand()
    {
        Assert.True(_validator.Validate(new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE, JSON, null)).IsValid);
    }

    [Fact]
    public void RejectsOverlongWorkflowTypeId()
    {
        string overlongId = new('x', 101);

        Assert.False(_validator.Validate(
            new SaveWorkflowTemplateContentCommand(ORG, USER, overlongId, JSON, null)).IsValid);
        Assert.True(_validator.Validate(
            new SaveWorkflowTemplateContentCommand(ORG, USER, new string('x', 100), JSON, null)).IsValid);
    }

    [Fact]
    public void RejectsNewTypeNameLongerThanTheWorkflowTypeNameColumn()
    {
        Assert.False(_validator.Validate(new SaveWorkflowTemplateContentCommand(
            ORG, USER, TYPE, JSON, null,
            NewTypeName: new string('x', WorkflowType.NAME_MAX_LENGTH + 1))).IsValid);
        Assert.True(_validator.Validate(new SaveWorkflowTemplateContentCommand(
            ORG, USER, TYPE, JSON, null,
            NewTypeName: new string('x', WorkflowType.NAME_MAX_LENGTH))).IsValid);
        Assert.True(_validator.Validate(
            new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE, JSON, null, NewTypeName: "fine")).IsValid);
    }
}
