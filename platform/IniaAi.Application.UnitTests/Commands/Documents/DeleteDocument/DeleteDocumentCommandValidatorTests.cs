using FluentValidation.Results;
using IniaAi.Application.Commands.Documents.DeleteDocument;

namespace IniaAi.Application.UnitTests.Commands.Documents.DeleteDocument;

public class DeleteDocumentCommandValidatorTests
{
    private readonly DeleteDocumentCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidCommand_Passes()
    {
        var command = new DeleteDocumentCommand("org-1", "user-1", "doc-1");

        ValidationResult result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("", "user-1", "doc-1", nameof(DeleteDocumentCommand.OrganizationId))]
    [InlineData("org-1", "", "doc-1", nameof(DeleteDocumentCommand.UserId))]
    [InlineData("org-1", "user-1", "", nameof(DeleteDocumentCommand.DocumentId))]
    public void Validate_EmptyField_Fails(string org, string user, string docId, string expectedPropertyName)
    {
        var command = new DeleteDocumentCommand(org, user, docId);

        ValidationResult result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == expectedPropertyName);
    }
}
