using FluentValidation.Results;
using IniaAi.Application.Commands.Documents.BulkDeleteDocuments;

namespace IniaAi.Application.UnitTests.Commands.Documents.BulkDeleteDocuments;

public class BulkDeleteDocumentsCommandValidatorTests
{
    private readonly BulkDeleteDocumentsCommandValidator _validator = new();

    [Fact]
    public void Validate_WithSingleValidId_Passes()
    {
        var command = new BulkDeleteDocumentsCommand("org-1", "user-1", ["doc-1"]);

        ValidationResult result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithEmptyList_Fails()
    {
        var command = new BulkDeleteDocumentsCommand("org-1", "user-1", []);

        ValidationResult result = _validator.Validate(command);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_WithWhitespaceOnlyId_Fails()
    {
        var command = new BulkDeleteDocumentsCommand("org-1", "user-1", ["  "]);

        ValidationResult result = _validator.Validate(command);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_WithTooManyIds_Fails()
    {
        List<string> ids = [.. Enumerable.Range(0, 501).Select(i => $"doc-{i}")];
        var command = new BulkDeleteDocumentsCommand("org-1", "user-1", ids);

        ValidationResult result = _validator.Validate(command);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_WithEmptyOrganization_Fails()
    {
        var command = new BulkDeleteDocumentsCommand("", "user-1", ["doc-1"]);

        ValidationResult result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(BulkDeleteDocumentsCommand.OrganizationId));
    }
}
