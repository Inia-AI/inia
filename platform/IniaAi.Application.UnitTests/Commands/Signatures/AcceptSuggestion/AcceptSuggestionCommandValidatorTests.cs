using FluentValidation.TestHelper;
using IniaAi.Application.Commands.Signatures.AcceptSuggestion;

namespace IniaAi.Application.UnitTests.Commands.Signatures.AcceptSuggestion;

public class AcceptSuggestionCommandValidatorTests
{
    private readonly AcceptSuggestionCommandValidator _validator = new();

    private static AcceptSuggestionCommand Valid() => new(
        OrganizationId: "org-1",
        UserId: "user-1",
        PendingOverrideId: "pending-1",
        VoteId: "vote-1");

    [Fact]
    public void Should_Pass_For_Valid_Command()
    {
        TestValidationResult<AcceptSuggestionCommand> result =
            _validator.TestValidate(Valid());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Have_Error_When_OrganizationId_Empty()
    {
        TestValidationResult<AcceptSuggestionCommand> result =
            _validator.TestValidate(Valid() with { OrganizationId = string.Empty });
        _ = result.ShouldHaveValidationErrorFor(x => x.OrganizationId);
    }

    [Fact]
    public void Should_Have_Error_When_UserId_Empty()
    {
        TestValidationResult<AcceptSuggestionCommand> result =
            _validator.TestValidate(Valid() with { UserId = string.Empty });
        _ = result.ShouldHaveValidationErrorFor(x => x.UserId);
    }

    [Fact]
    public void Should_Have_Error_When_PendingOverrideId_Empty()
    {
        TestValidationResult<AcceptSuggestionCommand> result =
            _validator.TestValidate(Valid() with { PendingOverrideId = string.Empty });
        _ = result.ShouldHaveValidationErrorFor(x => x.PendingOverrideId);
    }

    [Fact]
    public void Should_Have_Error_When_VoteId_Empty()
    {
        TestValidationResult<AcceptSuggestionCommand> result =
            _validator.TestValidate(Valid() with { VoteId = string.Empty });
        _ = result.ShouldHaveValidationErrorFor(x => x.VoteId);
    }
}
