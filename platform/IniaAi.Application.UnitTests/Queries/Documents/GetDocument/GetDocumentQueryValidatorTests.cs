using FluentValidation.Results;
using IniaAi.Application.Queries.Documents.GetDocument;

namespace IniaAi.Application.UnitTests.Queries.Documents.GetDocument;

public class GetDocumentQueryValidatorTests
{
    private readonly GetDocumentQueryValidator _validator = new();

    [Fact]
    public void Validate_WithAllFields_Passes()
    {
        var query = new GetDocumentQuery("org-1", "user-1", "doc-1");

        ValidationResult result = _validator.Validate(query);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("", "user-1", "doc-1", nameof(GetDocumentQuery.OrganizationId))]
    [InlineData("org-1", "", "doc-1", nameof(GetDocumentQuery.UserId))]
    [InlineData("org-1", "user-1", "", nameof(GetDocumentQuery.DocumentId))]
    public void Validate_EmptyField_Fails(string org, string user, string docId, string expectedPropertyName)
    {
        var query = new GetDocumentQuery(org, user, docId);

        ValidationResult result = _validator.Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == expectedPropertyName);
    }
}
