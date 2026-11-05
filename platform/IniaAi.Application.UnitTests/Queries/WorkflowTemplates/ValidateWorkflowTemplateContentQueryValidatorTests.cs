using IniaAi.Application.Commands.WorkflowTemplates.SaveWorkflowTemplateContent;
using IniaAi.Application.Queries.WorkflowTemplates.ValidateWorkflowTemplateContent;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Application.UnitTests.Queries.WorkflowTemplates;

public class ValidateWorkflowTemplateContentQueryValidatorTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";
    private const string TYPE = "WT-Report";
    private const string JSON = "{}";

    private readonly ValidateWorkflowTemplateContentQueryValidator _validator = new();
    private readonly SaveWorkflowTemplateContentCommandValidator _saveValidator = new();

    [Fact]
    public void RejectsEmptyRequiredFields()
    {
        Assert.False(_validator.Validate(new ValidateWorkflowTemplateContentQuery("", USER, TYPE, JSON, null)).IsValid);
        Assert.False(_validator.Validate(new ValidateWorkflowTemplateContentQuery(ORG, "", TYPE, JSON, null)).IsValid);
        Assert.False(_validator.Validate(new ValidateWorkflowTemplateContentQuery(ORG, USER, "", JSON, null)).IsValid);
        Assert.False(_validator.Validate(new ValidateWorkflowTemplateContentQuery(ORG, USER, TYPE, "", null)).IsValid);
    }

    [Fact]
    public void AcceptsValidQuery()
    {
        Assert.True(_validator.Validate(new ValidateWorkflowTemplateContentQuery(ORG, USER, TYPE, JSON, null)).IsValid);
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void MatchesTheSaveOnWorkflowTypeIdLength(int length, bool expectedValid)
    {
        string id = new('x', length);

        Assert.Equal(
            expectedValid,
            _validator.Validate(new ValidateWorkflowTemplateContentQuery(ORG, USER, id, JSON, null)).IsValid);
        Assert.Equal(
            expectedValid,
            _saveValidator.Validate(new SaveWorkflowTemplateContentCommand(ORG, USER, id, JSON, null)).IsValid);
    }

    [Theory]
    [InlineData(WorkflowType.NAME_MAX_LENGTH, true)]
    [InlineData(WorkflowType.NAME_MAX_LENGTH + 1, false)]
    public void MatchesTheSaveOnNewTypeNameLength(int length, bool expectedValid)
    {
        string name = new('x', length);

        Assert.Equal(
            expectedValid,
            _validator.Validate(
                new ValidateWorkflowTemplateContentQuery(ORG, USER, TYPE, JSON, null, NewTypeName: name)).IsValid);
        Assert.Equal(
            expectedValid,
            _saveValidator.Validate(
                new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE, JSON, null, NewTypeName: name)).IsValid);
    }
}
