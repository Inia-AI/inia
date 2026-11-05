using ErrorOr;
using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Application.UnitTests.Common.Workflows;

public class DocumentClassEditorTests
{
    private readonly DocumentClassEditor _editor = new();

    [Fact]
    public void Upsert_Create_AddsDocumentClassWithFields()
    {
        var workflow = new Workflow("root");
        DocumentClassEdit edit = Edit(originalKey: null, key: "invoice", displayName: "Invoice");

        ErrorOr<DocumentClass> result = _editor.Upsert(workflow, edit);

        Assert.False(result.IsError);
        Assert.Equal("invoice", result.Value.Key);
        Assert.Equal("Invoice", result.Value.DisplayName);
        Assert.Contains(workflow.DocumentClasses, dc => dc.Key == "invoice");
    }

    [Fact]
    public void Upsert_Update_RenamesKeyAndCascadesToBlocks()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        _ = new DocumentClass(workflow, "invoice");
        var block = new Block(BlockTypes.AiQuery, section)
        {
            ReplacementTag = "a",
            SupportedDocumentClasses = ["invoice"],
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };

        ErrorOr<DocumentClass> result = _editor.Upsert(
            workflow,
            Edit(originalKey: "invoice", key: "invoice_v2", displayName: "Invoice v2"));

        Assert.False(result.IsError);
        Assert.Equal("invoice_v2", result.Value.Key);
        Assert.Equal(["invoice_v2"], block.SupportedDocumentClasses);
    }

    [Fact]
    public void Upsert_Update_UpdatesFieldsWithoutRename()
    {
        var workflow = new Workflow("root");
        _ = new DocumentClass(workflow, "invoice") { DisplayName = "Old" };

        ErrorOr<DocumentClass> result = _editor.Upsert(
            workflow,
            Edit(originalKey: "invoice", key: "invoice", displayName: "New", isOutput: true));

        Assert.False(result.IsError);
        Assert.Equal("invoice", result.Value.Key);
        Assert.Equal("New", result.Value.DisplayName);
        Assert.True(result.Value.IsOutputDocument);
        _ = Assert.Single(workflow.DocumentClasses);
    }

    [Fact]
    public void Upsert_EmptyKey_ReturnsValidationError()
    {
        var workflow = new Workflow("root");

        ErrorOr<DocumentClass> result = _editor.Upsert(
            workflow,
            Edit(originalKey: null, key: "  ", displayName: "Invoice"));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
    }

    [Fact]
    public void Upsert_EmptyDisplayName_ReturnsValidationError()
    {
        var workflow = new Workflow("root");

        ErrorOr<DocumentClass> result = _editor.Upsert(
            workflow,
            Edit(originalKey: null, key: "invoice", displayName: " "));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
    }

    [Theory]
    [InlineData("Any")]
    [InlineData("None")]
    public void Upsert_ReservedKey_ReturnsValidationError(string reserved)
    {
        var workflow = new Workflow("root");

        ErrorOr<DocumentClass> result = _editor.Upsert(
            workflow,
            Edit(originalKey: null, key: reserved, displayName: "X"));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
    }

    [Fact]
    public void Upsert_DuplicateKey_ReturnsConflict()
    {
        var workflow = new Workflow("root");
        _ = new DocumentClass(workflow, "invoice");

        ErrorOr<DocumentClass> result = _editor.Upsert(
            workflow,
            Edit(originalKey: null, key: "INVOICE", displayName: "Dup"));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
    }

    [Fact]
    public void Upsert_Update_OriginalKeyDiffersOnlyByCase_FindsClass()
    {
        var workflow = new Workflow("root");
        _ = new DocumentClass(workflow, "invoice") { DisplayName = "Old" };

        ErrorOr<DocumentClass> result = _editor.Upsert(
            workflow,
            Edit(originalKey: "INVOICE", key: "invoice", displayName: "New"));

        Assert.False(result.IsError);
        Assert.Equal("invoice", result.Value.Key);
        Assert.Equal("New", result.Value.DisplayName);
        _ = Assert.Single(workflow.DocumentClasses);
    }

    [Fact]
    public void Upsert_UpdateNonexistentOriginal_ReturnsNotFound()
    {
        var workflow = new Workflow("root");

        ErrorOr<DocumentClass> result = _editor.Upsert(
            workflow,
            Edit(originalKey: "ghost", key: "ghost", displayName: "Ghost"));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public void Remove_ExistingClass_RemovesAndCascadesToBlocks()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        DocumentClass documentClass = new(workflow, "invoice");
        var block = new Block(BlockTypes.AiQuery, section)
        {
            ReplacementTag = "a",
            SupportedDocumentClasses = ["invoice"],
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };

        ErrorOr<Deleted> result = _editor.Remove(workflow, "invoice");

        Assert.False(result.IsError);
        Assert.DoesNotContain(documentClass, workflow.DocumentClasses);
        Assert.Equal([DocumentClass.KEY_ANY], block.SupportedDocumentClasses);
    }

    [Fact]
    public void Remove_KeyDiffersOnlyByCase_RemovesClass()
    {
        var workflow = new Workflow("root");
        DocumentClass documentClass = new(workflow, "invoice");

        ErrorOr<Deleted> result = _editor.Remove(workflow, "INVOICE");

        Assert.False(result.IsError);
        Assert.DoesNotContain(documentClass, workflow.DocumentClasses);
    }

    [Fact]
    public void Remove_NonexistentClass_ReturnsNotFound()
    {
        var workflow = new Workflow("root");

        ErrorOr<Deleted> result = _editor.Remove(workflow, "ghost");

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public void Upsert_StoresTheClassOverviewPrompts_AsItsOwnCopy()
    {
        var workflow = new Workflow("root");
        var prompts = new PageOverviewPrompts { Pages = new PageOverviewPrompt { UserPrompt = "Annual report page {0}." } };

        DocumentClass saved = _editor.Upsert(workflow, Edit(null, "report", "Report") with { PageOverviews = prompts }).Value;

        Assert.Equal("Annual report page {0}.", saved.PageOverviews!.Pages!.UserPrompt);
        Assert.NotSame(prompts, saved.PageOverviews);
    }

    [Fact]
    public void Upsert_BlankOverviewPrompts_ClearTheClassPrompts()
    {
        var workflow = new Workflow("root");
        _ = new DocumentClass(workflow, "report") { PageOverviews = new PageOverviewPrompts { Pages = new PageOverviewPrompt { UserPrompt = "Old." } } };

        DocumentClass saved = _editor.Upsert(
            workflow,
            Edit("report", "report", "Report") with { PageOverviews = new PageOverviewPrompts { Pages = new PageOverviewPrompt { UserPrompt = " " } } }).Value;

        Assert.Null(saved.PageOverviews);
    }

    [Fact]
    public void Upsert_WithoutOverviewPrompts_LeavesTheClassPromptsAlone()
    {
        var workflow = new Workflow("root");
        _ = new DocumentClass(workflow, "report") { PageOverviews = new PageOverviewPrompts { Pages = new PageOverviewPrompt { UserPrompt = "Kept." } } };

        DocumentClass saved = _editor.Upsert(workflow, Edit("report", "report", "Renamed")).Value;

        Assert.Equal("Kept.", saved.PageOverviews!.Pages!.UserPrompt);
    }

    private static DocumentClassEdit Edit(
        string? originalKey,
        string key,
        string displayName,
        bool isOutput = false)
    {
        return new DocumentClassEdit(
            originalKey,
            key,
            displayName,
            Description: string.Empty,
            DocumentCategoryId: null,
            Requirement: DocumentClassRequirement.Optional,
            ShouldBeSingular: false,
            IsOutputDocument: isOutput);
    }
}
