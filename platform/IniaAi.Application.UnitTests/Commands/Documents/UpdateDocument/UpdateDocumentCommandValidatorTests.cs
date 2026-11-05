using FluentValidation.Results;
using IniaAi.Application.Commands.Documents.UpdateDocument;

namespace IniaAi.Application.UnitTests.Commands.Documents.UpdateDocument;

public class UpdateDocumentCommandValidatorTests
{
    private readonly UpdateDocumentCommandValidator _validator = new();

    [Fact]
    public void Validate_WithValidMinimalCommand_Passes()
    {
        var command = new UpdateDocumentCommand(
            OrganizationId: "org-1",
            UserId: "user-1",
            DocumentId: "doc-1",
            Name: null,
            Description: null,
            DocumentCategoryId: null,
            WorkflowTypeId: null,
            Class: null,
            Topics: null,
            TableOfContents: null);

        ValidationResult result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyOrganizationId_Fails()
    {
        var command = new UpdateDocumentCommand("", "user-1", "doc-1", null, null, null, null, null, null, null);

        ValidationResult result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateDocumentCommand.OrganizationId));
    }

    [Fact]
    public void Validate_EmptyUserId_Fails()
    {
        var command = new UpdateDocumentCommand("org-1", "", "doc-1", null, null, null, null, null, null, null);

        ValidationResult result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateDocumentCommand.UserId));
    }

    [Fact]
    public void Validate_EmptyDocumentId_Fails()
    {
        var command = new UpdateDocumentCommand("org-1", "user-1", "", null, null, null, null, null, null, null);

        ValidationResult result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateDocumentCommand.DocumentId));
    }

    [Fact]
    public void Validate_MarkingStaticWithoutAWorkflowType_Fails()
    {
        var command = new UpdateDocumentCommand("org-1", "user-1", "doc-1", null, null, null, null, "Regulatory", null, null, IsStatic: true);

        ValidationResult result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateDocumentCommand.WorkflowTypeId));
    }

    [Fact]
    public void Validate_MarkingStaticWithoutADocumentClass_Fails()
    {
        var command = new UpdateDocumentCommand("org-1", "user-1", "doc-1", null, null, null, "AssetManagerCreditAnalysis", null, null, null, IsStatic: true);

        ValidationResult result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateDocumentCommand.Class));
    }

    [Fact]
    public void Validate_UnmarkingStaticWhileNamingADocumentClass_Fails()
    {
        var command = new UpdateDocumentCommand("org-1", "user-1", "doc-1", null, null, null, null, "Regulatory", null, null, IsStatic: false);

        ValidationResult result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateDocumentCommand.Class));
    }

    [Fact]
    public void Validate_UnmarkingStaticWhileNamingAWorkflowType_Fails()
    {
        var command = new UpdateDocumentCommand("org-1", "user-1", "doc-1", null, null, null, "AssetManagerCreditAnalysis", null, null, null, IsStatic: false);

        ValidationResult result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateDocumentCommand.WorkflowTypeId));
    }

    [Fact]
    public void Validate_NameExceedsLimit_Fails()
    {
        string tooLong = new('x', 251);
        var command = new UpdateDocumentCommand("org-1", "user-1", "doc-1", tooLong, null, null, null, null, null, null);

        ValidationResult result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateDocumentCommand.Name));
    }

    [Fact]
    public void Validate_DescriptionExceedsLimit_Fails()
    {
        string tooLong = new('x', 1001);
        var command = new UpdateDocumentCommand("org-1", "user-1", "doc-1", null, tooLong, null, null, null, null, null);

        ValidationResult result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateDocumentCommand.Description));
    }
}
