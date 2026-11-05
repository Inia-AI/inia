using FluentValidation.TestHelper;
using IniaAi.Application.Commands.WorkflowExecutions.UpdateWorkflowExecutionState;
using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.Application.UnitTests.Commands.WorkflowExecutions.UpdateWorkflowExecutionState;

public class UpdateWorkflowExecutionStateCommandValidatorTests
{
    private readonly UpdateWorkflowExecutionStateCommandValidator _validator = new();

    private static UpdateWorkflowExecutionStateCommand Valid() => new(
        OrganizationId: "org-1",
        UserId: "user-1",
        WorkflowExecutionId: "exec-1",
        TargetState: WorkflowState.Cancelled);

    [Fact]
    public void Should_Pass_For_Valid_Command()
    {
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Have_Error_When_OrganizationId_Empty()
    {
        _ = _validator.TestValidate(Valid() with { OrganizationId = string.Empty })
            .ShouldHaveValidationErrorFor(x => x.OrganizationId);
    }

    [Fact]
    public void Should_Have_Error_When_UserId_Empty()
    {
        _ = _validator.TestValidate(Valid() with { UserId = string.Empty })
            .ShouldHaveValidationErrorFor(x => x.UserId);
    }

    [Fact]
    public void Should_Have_Error_When_WorkflowExecutionId_Empty()
    {
        _ = _validator.TestValidate(Valid() with { WorkflowExecutionId = string.Empty })
            .ShouldHaveValidationErrorFor(x => x.WorkflowExecutionId);
    }

    [Theory]
    [InlineData(WorkflowState.Undefined)]
    [InlineData(WorkflowState.Running)]
    [InlineData(WorkflowState.Draft)]
    [InlineData(WorkflowState.Confirmed)]
    [InlineData(WorkflowState.Failed)]
    [InlineData(WorkflowState.ProcessingDocuments)]
    [InlineData(WorkflowState.Queued)]
    public void Should_Have_Error_When_TargetState_Is_Not_Cancelled(WorkflowState targetState)
    {
        _ = _validator.TestValidate(Valid() with { TargetState = targetState })
            .ShouldHaveValidationErrorFor(x => x.TargetState);
    }
}
