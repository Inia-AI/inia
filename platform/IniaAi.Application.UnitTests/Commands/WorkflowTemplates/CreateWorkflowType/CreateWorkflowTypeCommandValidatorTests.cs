using IniaAi.Application.Commands.WorkflowTemplates.CreateWorkflowType;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Application.UnitTests.Commands.WorkflowTemplates.CreateWorkflowType;

public class CreateWorkflowTypeCommandValidatorTests
{
    private readonly CreateWorkflowTypeCommandValidator _validator = new();

    [Fact]
    public void AcceptsValidCommand()
    {
        Assert.True(_validator.Validate(Command()).IsValid);
    }

    [Fact]
    public void RejectsEmptyRequiredFields()
    {
        Assert.False(_validator.Validate(Command() with { UserId = "" }).IsValid);
        Assert.False(_validator.Validate(Command() with { WorkflowTypeId = "" }).IsValid);
        Assert.False(_validator.Validate(Command() with { Name = "" }).IsValid);
    }

    [Fact]
    public void WithoutAnOrganization_TellsTheAuthorToChooseOne()
    {
        string message = Assert.Single(_validator.Validate(Command() with { OrganizationId = "" }).Errors).ErrorMessage;

        Assert.StartsWith("Choose an organization", message);
    }

    [Fact]
    public void RejectsValuesLongerThanTheirColumns()
    {
        Assert.False(_validator.Validate(Command() with { WorkflowTypeId = new string('x', WorkflowType.ID_MAX_LENGTH + 1) }).IsValid);
        Assert.True(_validator.Validate(Command() with { WorkflowTypeId = new string('x', WorkflowType.ID_MAX_LENGTH) }).IsValid);
        Assert.False(_validator.Validate(Command() with { Name = new string('x', WorkflowType.NAME_MAX_LENGTH + 1) }).IsValid);
    }

    private static CreateWorkflowTypeCommand Command()
    {
        return new CreateWorkflowTypeCommand("org-test", "user-test", "qa-se01", "QA SE01", null, null, []);
    }
}
