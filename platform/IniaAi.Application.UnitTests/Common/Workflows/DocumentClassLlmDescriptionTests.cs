using ErrorOr;
using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Application.UnitTests.Common.Workflows;

/// <summary>
/// The model-facing description survives every path that rebuilds a class from an edit: create,
/// update, the key rename that the rename handler expresses as an edit, and the view the API
/// returns. An empty edit clears it rather than storing an empty string.
/// </summary>
public class DocumentClassLlmDescriptionTests
{
    private const string MODEL_TEXT = "A monthly statement from a custodian; not a trade confirmation.";

    private readonly DocumentClassEditor _editor = new();

    [Fact]
    public void Create_StoresTheModelDescription()
    {
        var workflow = new Workflow("root");

        ErrorOr<DocumentClass> created = _editor.Upsert(workflow, Edit(originalKey: null, key: "statement", llmDescription: MODEL_TEXT));

        Assert.False(created.IsError);
        Assert.Equal(MODEL_TEXT, created.Value.LlmDescription);
    }

    [Fact]
    public void RenameExpressedAsAnEdit_KeepsTheModelDescription()
    {
        var workflow = new Workflow("root");
        _ = new DocumentClass(workflow, "statement") { LlmDescription = MODEL_TEXT };

        ErrorOr<DocumentClass> renamed = _editor.Upsert(workflow, Edit(originalKey: "statement", key: "custodian_statement", llmDescription: MODEL_TEXT));

        Assert.False(renamed.IsError);
        Assert.Equal("custodian_statement", renamed.Value.Key);
        Assert.Equal(MODEL_TEXT, renamed.Value.LlmDescription);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AnEmptyEdit_ClearsTheModelDescription(string? cleared)
    {
        var workflow = new Workflow("root");
        _ = new DocumentClass(workflow, "statement") { LlmDescription = MODEL_TEXT };

        ErrorOr<DocumentClass> updated = _editor.Upsert(workflow, Edit(originalKey: "statement", key: "statement", llmDescription: cleared));

        Assert.False(updated.IsError);
        Assert.Null(updated.Value.LlmDescription);
    }

    [Fact]
    public void TheView_CarriesTheModelDescription()
    {
        var workflow = new Workflow("root");
        var documentClass = new DocumentClass(workflow, "statement") { LlmDescription = MODEL_TEXT };

        DocumentClassView view = DocumentClassView.From(documentClass);

        Assert.Equal(MODEL_TEXT, view.LlmDescription);
    }

    private static DocumentClassEdit Edit(string? originalKey, string key, string? llmDescription)
    {
        return new DocumentClassEdit(
            originalKey,
            key,
            DisplayName: key,
            Description: "Shown to people",
            DocumentCategoryId: null,
            Requirement: DocumentClassRequirement.Optional,
            ShouldBeSingular: false,
            IsOutputDocument: false,
            LlmDescription: llmDescription);
    }
}
