using FluentValidation.TestHelper;
using IniaAi.Application.Commands.Signatures.TestSignaturePrompt;

namespace IniaAi.Application.UnitTests.Commands.Signatures.TestSignaturePrompt;

public class TestSignaturePromptCommandValidatorTests
{
    private readonly TestSignaturePromptCommandValidator _validator = new();

    private static TestSignaturePromptCommand Valid() => new(
        OrganizationId: "org-1",
        UserId: "user-1",
        WorkflowExecutionId: "exec-1",
        BlockId: "block-1",
        PlaceholderName: "field_a",
        TestValue: "some new value");

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

    [Fact]
    public void Should_Have_Error_When_BlockId_Empty()
    {
        _ = _validator.TestValidate(Valid() with { BlockId = string.Empty })
            .ShouldHaveValidationErrorFor(x => x.BlockId);
    }

    [Fact]
    public void Should_Have_Error_When_PlaceholderName_Empty()
    {
        _ = _validator.TestValidate(Valid() with { PlaceholderName = string.Empty })
            .ShouldHaveValidationErrorFor(x => x.PlaceholderName);
    }

    [Fact]
    public void Should_Have_Error_When_TestValue_Empty()
    {
        _ = _validator.TestValidate(Valid() with { TestValue = string.Empty })
            .ShouldHaveValidationErrorFor(x => x.TestValue);
    }

    // Boundary: BlockId is capped at 64 chars to match the sibling signatures
    // validator convention.
    [Fact]
    public void Should_Pass_When_BlockId_At_Limit()
    {
        _validator.TestValidate(Valid() with { BlockId = new string('b', 64) })
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Have_Error_When_BlockId_Exceeds_Limit()
    {
        _ = _validator.TestValidate(Valid() with { BlockId = new string('b', 65) })
            .ShouldHaveValidationErrorFor(x => x.BlockId);
    }

    [Fact]
    public void Should_Pass_When_PlaceholderName_At_Limit()
    {
        _validator.TestValidate(Valid() with { PlaceholderName = new string('p', 200) })
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Have_Error_When_PlaceholderName_Exceeds_Limit()
    {
        _ = _validator.TestValidate(Valid() with { PlaceholderName = new string('p', 201) })
            .ShouldHaveValidationErrorFor(x => x.PlaceholderName);
    }

    // TestValue cap matches the capture-side 64 KB ceiling — see
    // AiQueryProperties.MAX_CAPTURED_VALUE_LENGTH.
    [Fact]
    public void Should_Pass_When_TestValue_At_Limit()
    {
        _validator.TestValidate(Valid() with { TestValue = new string('t', 64 * 1024) })
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Have_Error_When_TestValue_Exceeds_Limit()
    {
        _ = _validator.TestValidate(Valid() with { TestValue = new string('t', (64 * 1024) + 1) })
            .ShouldHaveValidationErrorFor(x => x.TestValue);
    }
}
