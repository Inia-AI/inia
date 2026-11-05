using IniaAi.Application.Commands.WorkflowTemplates.DuplicateWorkflowType;

namespace IniaAi.Application.UnitTests.Commands.WorkflowTemplates.DuplicateWorkflowType;

public class DuplicateWorkflowTypeCommandValidatorTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";
    private const string SOURCE = "WT-Report";
    private const string TARGET = "WT-Report-EU";
    private const string NAME = "Report Workflow (EU)";

    private readonly DuplicateWorkflowTypeCommandValidator _validator = new();

    [Fact]
    public void AcceptsValidCommand()
    {
        Assert.True(_validator.Validate(Command()).IsValid);
    }

    [Fact]
    public void RejectsEmptyRequiredFields()
    {
        Assert.False(_validator.Validate(Command(organizationId: "")).IsValid);
        Assert.False(_validator.Validate(Command(userId: "")).IsValid);
        Assert.False(_validator.Validate(Command(sourceWorkflowTypeId: "")).IsValid);
        Assert.False(_validator.Validate(Command(newWorkflowTypeId: "")).IsValid);
        Assert.False(_validator.Validate(Command(newName: "")).IsValid);
    }

    [Fact]
    public void RejectsOverlongWorkflowTypeIds()
    {
        Assert.False(_validator.Validate(Command(sourceWorkflowTypeId: new string('x', 101))).IsValid);
        Assert.False(_validator.Validate(Command(newWorkflowTypeId: new string('x', 101))).IsValid);
        Assert.True(_validator.Validate(Command(newWorkflowTypeId: new string('x', 100))).IsValid);
    }

    [Fact]
    public void RejectsNameLongerThanTheColumn_NotTheAnnotation()
    {
        Assert.True(_validator.Validate(Command(newName: new string('x', 250))).IsValid);
        Assert.False(_validator.Validate(Command(newName: new string('x', 251))).IsValid);
    }

    [Fact]
    public void RejectsDuplicatingATypeOntoItself()
    {
        Assert.False(_validator.Validate(Command(newWorkflowTypeId: SOURCE)).IsValid);
    }

    private static DuplicateWorkflowTypeCommand Command(
        string organizationId = ORG,
        string userId = USER,
        string sourceWorkflowTypeId = SOURCE,
        string newWorkflowTypeId = TARGET,
        string newName = NAME)
    {
        return new DuplicateWorkflowTypeCommand(
            organizationId,
            userId,
            sourceWorkflowTypeId,
            newWorkflowTypeId,
            newName);
    }
}
