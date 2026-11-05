using FluentValidation.TestHelper;
using IniaAi.Application.Commands.Signatures.CastSignatureOverrideVote;
using IniaAi.Domain.Entities.Signatures;

namespace IniaAi.Application.UnitTests.Commands.Signatures.CastSignatureOverrideVote;

public class CastSignatureOverrideVoteCommandValidatorTests
{
    private readonly CastSignatureOverrideVoteCommandValidator _validator = new();

    private static CastSignatureOverrideVoteCommand Valid() => new(
        OrganizationId: "org-1",
        UserId: "user-1",
        PendingOverrideId: "pending-1",
        VoteKind: SignatureVoteKind.Approve,
        Comment: null,
        SuggestedValue: null);

    [Fact]
    public void Should_Pass_For_Valid_Command()
    {
        TestValidationResult<CastSignatureOverrideVoteCommand> result =
            _validator.TestValidate(Valid());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Have_Error_When_OrganizationId_Empty()
    {
        TestValidationResult<CastSignatureOverrideVoteCommand> result =
            _validator.TestValidate(Valid() with { OrganizationId = string.Empty });
        _ = result.ShouldHaveValidationErrorFor(x => x.OrganizationId);
    }

    [Fact]
    public void Should_Have_Error_When_UserId_Empty()
    {
        TestValidationResult<CastSignatureOverrideVoteCommand> result =
            _validator.TestValidate(Valid() with { UserId = string.Empty });
        _ = result.ShouldHaveValidationErrorFor(x => x.UserId);
    }

    [Fact]
    public void Should_Have_Error_When_PendingOverrideId_Empty()
    {
        TestValidationResult<CastSignatureOverrideVoteCommand> result =
            _validator.TestValidate(Valid() with { PendingOverrideId = string.Empty });
        _ = result.ShouldHaveValidationErrorFor(x => x.PendingOverrideId);
    }

    [Fact]
    public void Should_Have_Error_When_VoteKind_Out_Of_Range()
    {
        TestValidationResult<CastSignatureOverrideVoteCommand> result =
            _validator.TestValidate(Valid() with { VoteKind = (SignatureVoteKind)999 });
        _ = result.ShouldHaveValidationErrorFor(x => x.VoteKind);
    }

    [Fact]
    public void Should_Have_Error_When_Comment_Too_Long()
    {
        TestValidationResult<CastSignatureOverrideVoteCommand> result =
            _validator.TestValidate(Valid() with { Comment = new string('x', 2001) });
        _ = result.ShouldHaveValidationErrorFor(x => x.Comment);
    }

    [Fact]
    public void Should_Have_Error_When_SuggestedValue_Too_Long()
    {
        TestValidationResult<CastSignatureOverrideVoteCommand> result =
            _validator.TestValidate(Valid() with { SuggestedValue = new string('x', 4001) });
        _ = result.ShouldHaveValidationErrorFor(x => x.SuggestedValue);
    }
}
