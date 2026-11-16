using ErrorOr;
using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Application.UnitTests.Common.Workflows;

/// <summary>
/// An edit sets the input's Made by link, clears it, or leaves it alone when it names none, so the templates API's
/// per-class update keeps a link it does not carry. An output class is made by its own workflow and has no link.
/// </summary>
public class DocumentClassMadeByEditTests
{
    private static readonly DocumentClassMadeBy Part1Spread = new("CorpPart1TypeA", "Financial Spread Output");

    private readonly DocumentClassEditor _editor = new();

    [Fact]
    public void AnEditWithALink_SetsIt()
    {
        var workflow = new Workflow("Part 2");

        ErrorOr<DocumentClass> created = _editor.Upsert(workflow, Edit(originalKey: null) with { MadeBy = Part1Spread });

        Assert.Equal("CorpPart1TypeA", created.Value.MadeByWorkflowTypeId);
        Assert.Equal("Financial Spread Output", created.Value.MadeByDocumentClassKey);
    }

    [Fact]
    public void AnEditThatNamesNoLink_LeavesTheStoredOne()
    {
        var workflow = new Workflow("Part 2");
        _ = new DocumentClass(workflow, "CorporateFinancialModel") { MadeByWorkflowTypeId = "CorpPart1TypeA", MadeByDocumentClassKey = "Financial Spread Output" };

        ErrorOr<DocumentClass> updated = _editor.Upsert(workflow, Edit(originalKey: "CorporateFinancialModel") with { DisplayName = "Spreads" });

        Assert.Equal("CorpPart1TypeA", updated.Value.MadeByWorkflowTypeId);
        Assert.Equal("Financial Spread Output", updated.Value.MadeByDocumentClassKey);
    }

    [Fact]
    public void AnEditWithAnEmptyLink_ClearsIt()
    {
        var workflow = new Workflow("Part 2");
        _ = new DocumentClass(workflow, "CorporateFinancialModel") { MadeByWorkflowTypeId = "CorpPart1TypeA", MadeByDocumentClassKey = "Financial Spread Output" };

        ErrorOr<DocumentClass> updated = _editor.Upsert(workflow, Edit(originalKey: "CorporateFinancialModel") with { MadeBy = DocumentClassMadeBy.None });

        Assert.Null(updated.Value.MadeByWorkflowTypeId);
        Assert.Null(updated.Value.MadeByDocumentClassKey);
    }

    [Fact]
    public void AnOutputClass_HasNoLink()
    {
        var workflow = new Workflow("Part 2");

        ErrorOr<DocumentClass> created = _editor.Upsert(workflow, Edit(originalKey: null) with { IsOutputDocument = true, MadeBy = Part1Spread });

        Assert.False(created.Value.IsMadeByAnotherWorkflow);
    }

    private static DocumentClassEdit Edit(string? originalKey)
    {
        return new DocumentClassEdit(
            OriginalKey: originalKey,
            Key: "CorporateFinancialModel",
            DisplayName: "Financial Spreads",
            Description: string.Empty,
            DocumentCategoryId: null,
            Requirement: DocumentClassRequirement.Required,
            ShouldBeSingular: true,
            IsOutputDocument: false);
    }
}
