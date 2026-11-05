using FluentValidation.TestHelper;
using IniaAi.Application.Commands.Signatures.SubmitPendingSignatureOverride;

namespace IniaAi.Application.UnitTests.Commands.Signatures.SubmitPendingSignatureOverride;

public class SubmitPendingSignatureOverrideCommandValidatorTests
{
    private readonly SubmitPendingSignatureOverrideCommandValidator _validator = new();

    private static SubmitPendingSignatureOverrideCommand Valid() => new(
        OrganizationId: "org-1",
        UserId: "user-1",
        WorkflowTypeId: "WT-1",
        PlaceholderName: "field_a",
        ProposedValue: "ok");

    [Fact]
    public void Should_Pass_For_Valid_Command()
    {
        TestValidationResult<SubmitPendingSignatureOverrideCommand> result =
            _validator.TestValidate(Valid());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Have_Error_When_OrganizationId_Empty()
    {
        TestValidationResult<SubmitPendingSignatureOverrideCommand> result =
            _validator.TestValidate(Valid() with { OrganizationId = string.Empty });
        _ = result.ShouldHaveValidationErrorFor(x => x.OrganizationId);
    }

    [Fact]
    public void Should_Have_Error_When_UserId_Empty()
    {
        TestValidationResult<SubmitPendingSignatureOverrideCommand> result =
            _validator.TestValidate(Valid() with { UserId = string.Empty });
        _ = result.ShouldHaveValidationErrorFor(x => x.UserId);
    }

    [Fact]
    public void Should_Have_Error_When_WorkflowTypeId_Empty()
    {
        TestValidationResult<SubmitPendingSignatureOverrideCommand> result =
            _validator.TestValidate(Valid() with { WorkflowTypeId = string.Empty });
        _ = result.ShouldHaveValidationErrorFor(x => x.WorkflowTypeId);
    }

    [Fact]
    public void Should_Have_Error_When_PlaceholderName_Empty()
    {
        TestValidationResult<SubmitPendingSignatureOverrideCommand> result =
            _validator.TestValidate(Valid() with { PlaceholderName = string.Empty });
        _ = result.ShouldHaveValidationErrorFor(x => x.PlaceholderName);
    }

    [Fact]
    public void Should_Have_Error_When_ProposedValue_Too_Long()
    {
        TestValidationResult<SubmitPendingSignatureOverrideCommand> result =
            _validator.TestValidate(Valid() with { ProposedValue = new string('x', 4001) });
        _ = result.ShouldHaveValidationErrorFor(x => x.ProposedValue);
    }

    [Fact]
    public void Should_Not_Have_Error_When_ProposedValue_Null()
    {
        TestValidationResult<SubmitPendingSignatureOverrideCommand> result =
            _validator.TestValidate(Valid() with { ProposedValue = null });
        result.ShouldNotHaveValidationErrorFor(x => x.ProposedValue);
    }
}
