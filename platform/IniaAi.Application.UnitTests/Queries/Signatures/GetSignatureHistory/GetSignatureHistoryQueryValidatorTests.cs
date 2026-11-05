using FluentValidation.TestHelper;
using IniaAi.Application.Queries.Signatures.GetSignatureHistory;

namespace IniaAi.Application.UnitTests.Queries.Signatures.GetSignatureHistory;

public class GetSignatureHistoryQueryValidatorTests
{
    private readonly GetSignatureHistoryQueryValidator _validator = new();

    private static GetSignatureHistoryQuery Valid() => new(
        OrganizationId: "org-1",
        UserId: "user-1",
        WorkflowTypeId: "WT-Test");

    [Fact]
    public void Should_Pass_For_Valid_Query_With_Defaults()
    {
        TestValidationResult<GetSignatureHistoryQuery> result = _validator.TestValidate(Valid());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Have_Error_When_OrganizationId_Empty()
    {
        TestValidationResult<GetSignatureHistoryQuery> result =
            _validator.TestValidate(Valid() with { OrganizationId = string.Empty });
        _ = result.ShouldHaveValidationErrorFor(x => x.OrganizationId);
    }

    [Fact]
    public void Should_Have_Error_When_UserId_Empty()
    {
        TestValidationResult<GetSignatureHistoryQuery> result =
            _validator.TestValidate(Valid() with { UserId = string.Empty });
        _ = result.ShouldHaveValidationErrorFor(x => x.UserId);
    }

    [Fact]
    public void Should_Have_Error_When_WorkflowTypeId_Empty()
    {
        TestValidationResult<GetSignatureHistoryQuery> result =
            _validator.TestValidate(Valid() with { WorkflowTypeId = string.Empty });
        _ = result.ShouldHaveValidationErrorFor(x => x.WorkflowTypeId);
    }

    [Fact]
    public void Should_Have_Error_When_WorkflowTypeId_Too_Long()
    {
        TestValidationResult<GetSignatureHistoryQuery> result =
            _validator.TestValidate(Valid() with { WorkflowTypeId = new string('x', 201) });
        _ = result.ShouldHaveValidationErrorFor(x => x.WorkflowTypeId);
    }

    [Fact]
    public void Should_Have_Error_When_PlaceholderName_Too_Long()
    {
        TestValidationResult<GetSignatureHistoryQuery> result =
            _validator.TestValidate(Valid() with { PlaceholderName = new string('x', 201) });
        _ = result.ShouldHaveValidationErrorFor(x => x.PlaceholderName);
    }

    [Fact]
    public void Should_Pass_When_PlaceholderName_Within_Limit()
    {
        TestValidationResult<GetSignatureHistoryQuery> result =
            _validator.TestValidate(Valid() with { PlaceholderName = new string('x', 200) });
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Have_Error_When_ActorUserId_Too_Long()
    {
        TestValidationResult<GetSignatureHistoryQuery> result =
            _validator.TestValidate(Valid() with { ActorUserId = new string('x', 451) });
        _ = result.ShouldHaveValidationErrorFor(x => x.ActorUserId);
    }

    [Fact]
    public void Should_Have_Error_When_Skip_Negative()
    {
        TestValidationResult<GetSignatureHistoryQuery> result =
            _validator.TestValidate(Valid() with { Skip = -1 });
        _ = result.ShouldHaveValidationErrorFor(x => x.Skip);
    }

    [Fact]
    public void Should_Have_Error_When_Take_Zero()
    {
        TestValidationResult<GetSignatureHistoryQuery> result =
            _validator.TestValidate(Valid() with { Take = 0 });
        _ = result.ShouldHaveValidationErrorFor(x => x.Take);
    }

    [Fact]
    public void Should_Have_Error_When_Take_Exceeds_Max()
    {
        TestValidationResult<GetSignatureHistoryQuery> result =
            _validator.TestValidate(Valid() with { Take = 201 });
        _ = result.ShouldHaveValidationErrorFor(x => x.Take);
    }

    [Fact]
    public void Should_Pass_When_Take_At_Max()
    {
        TestValidationResult<GetSignatureHistoryQuery> result =
            _validator.TestValidate(Valid() with { Take = 200 });
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Have_Error_When_FromUtc_After_ToUtc()
    {
        DateTime t0 = DateTime.UtcNow;
        TestValidationResult<GetSignatureHistoryQuery> result = _validator.TestValidate(
            Valid() with { FromUtc = t0.AddDays(1), ToUtc = t0 });
        // Error binds to ToUtc so the response envelope surfaces it on a named
        // property key — anchoring to RuleFor(x => x) would have produced an
        // empty-string key that most error serializers strip.
        _ = result.ShouldHaveValidationErrorFor(x => x.ToUtc);
    }

    [Fact]
    public void Should_Pass_When_Only_FromUtc_Set()
    {
        TestValidationResult<GetSignatureHistoryQuery> result =
            _validator.TestValidate(Valid() with { FromUtc = DateTime.UtcNow });
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Pass_When_Only_ToUtc_Set()
    {
        TestValidationResult<GetSignatureHistoryQuery> result =
            _validator.TestValidate(Valid() with { ToUtc = DateTime.UtcNow });
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Pass_When_FromUtc_Equals_ToUtc()
    {
        DateTime t0 = DateTime.UtcNow;
        TestValidationResult<GetSignatureHistoryQuery> result =
            _validator.TestValidate(Valid() with { FromUtc = t0, ToUtc = t0 });
        result.ShouldNotHaveAnyValidationErrors();
    }
}
