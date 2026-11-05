using FluentValidation.TestHelper;
using IniaAi.Application.Queries.WorkflowExecutions.GetExecutionBlocks;

namespace IniaAi.Application.UnitTests.Queries.WorkflowExecutions.GetExecutionBlocks;

public class GetExecutionBlocksQueryValidatorTests
{
    private readonly GetExecutionBlocksQueryValidator _validator = new();

    private static GetExecutionBlocksQuery Valid() => new(
        OrganizationId: "org-1",
        UserId: "user-1",
        WorkflowExecutionId: "exec-1");

    [Fact]
    public void Should_Pass_For_Valid_Query()
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
}
