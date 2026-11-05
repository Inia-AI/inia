using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.WebApp.Models;
using System.Text;

namespace IniaAi.WebApp.Tests.Models;

/// <summary>
/// A report started from the editor runs on its own document rows. A stage writes a run's
/// decisions onto the rows it runs on, so a row shared with the editor would carry one run's
/// decided class into the next report as a hand-picked one, and one run's dropped document into
/// every later report.
/// </summary>
public class WorkflowEditorContextRunDocumentsTests
{
    private const string ORGANIZATION_ID = "org-editor";

    [Fact]
    public void EachReport_GetsItsOwnRows_CarryingTheEditorsChoices()
    {
        WorkflowEditorContext context = CreateContext(
            EditorDocument("statement.pdf", documentClass: "CustodyStatement", shouldUseOcr: true),
            EditorDocument("notes.pdf", documentClass: string.Empty, shouldUseOcr: false));

        IReadOnlyList<DocumentInfo> first = context.RunDocumentsFor("job-1");
        IReadOnlyList<DocumentInfo> second = context.RunDocumentsFor("job-2");

        Assert.All(first, row => Assert.Equal("job-1", row.JobId));
        Assert.All(second, row => Assert.Equal("job-2", row.JobId));
        Assert.Empty(first.Select(row => row.Id).Intersect(second.Select(row => row.Id)));
        Assert.Empty(first.Select(row => row.Id).Intersect(context.Documents.Select(row => row.Id)));
        Assert.Equal(context.Documents.Select(row => row.DocumentId), first.Select(row => row.DocumentId));
        Assert.Equal(["CustodyStatement", string.Empty], first.Select(row => row.DocumentClass));
        Assert.Equal([true, false], first.Select(row => row.ShouldUseOcr));
        Assert.All(first, row => Assert.Same(context.Documents.Single(editorRow => editorRow.DocumentId == row.DocumentId).Document, row.Document));
    }

    [Fact]
    public void WhatARunDecidesOnItsRows_NeverReachesTheEditor()
    {
        WorkflowEditorContext context = CreateContext(EditorDocument("statement.pdf", documentClass: string.Empty, shouldUseOcr: true));

        DocumentInfo runRow = Assert.Single(context.RunDocumentsFor("job-1"));
        runRow.DocumentClass = "CustodyStatement";
        runRow.ExcludeFromRun("NotNeeded", DateTime.UtcNow);

        DocumentInfo editorRow = Assert.Single(context.Documents);
        Assert.Equal(string.Empty, editorRow.DocumentClass);
        Assert.False(editorRow.IsExcludedFromRun);
        Assert.Null(editorRow.JobId);
        Assert.Equal(string.Empty, Assert.Single(context.RunDocumentsFor("job-2")).DocumentClass);
    }

    private static WorkflowEditorContext CreateContext(params DocumentInfo[] documents)
    {
        return new WorkflowEditorContext("EditorType", new Workflow { WorkflowTypeId = "EditorType" }, workflowHandler: null!)
        {
            Documents = [.. documents],
        };
    }

    private static DocumentInfo EditorDocument(string fileName, string documentClass, bool shouldUseOcr)
    {
        var document = new Document(new FileMetadata(ORGANIZATION_ID, fileName, Encoding.UTF8.GetBytes(fileName)), MediaType.ApplicationPdf);
        return new DocumentInfo(document.Id)
        {
            Document = document,
            DocumentClass = documentClass,
            ShouldUseOcr = shouldUseOcr,
        };
    }
}
