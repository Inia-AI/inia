using IniaAi.Core.Workflows.Diff;
using IniaAi.WebApp.Components.WorkflowEditor.Common;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Common;

public class SaveConflictDisclosureTests
{
    [Fact]
    public void BuildRevertLines_MixedDiff_NamesEveryChangedSurface()
    {
        var diff = new WorkflowDiff(
            Sections: [new SectionDiff("Risk", 0, DiffChangeKind.Added)],
            Blocks:
            [
                new BlockDiff(
                    "rate",
                    "Rate Analysis",
                    "AiQuery",
                    DiffChangeKind.Modified,
                    [new FieldDiff("Properties.UserPromptTemplate", "old", "new")]),
                new BlockDiff("legacy", "Legacy Note", "AiQuery", DiffChangeKind.Removed, []),
            ],
            Variables: [new VariableDiff("client", "Client Name", DiffChangeKind.Removed)],
            DocumentClasses: [new DocumentClassDiff("contract", "Contract", DiffChangeKind.Added)])
        {
            WorkflowFields = [new FieldDiff("OutputTitle", "A", "B")],
        };

        List<string> lines = SaveConflictDisclosure.BuildRevertLines(diff);

        Assert.Equal(
            [
                "Section 'Risk' — added",
                "Block 'Rate Analysis' — changed: Properties.UserPromptTemplate",
                "Block 'Legacy Note' — removed",
                "Variable 'Client Name' — removed",
                "Document class 'Contract' — added",
                "Template field 'OutputTitle' — changed",
            ],
            lines);
    }

    [Fact]
    public void BuildRevertLines_UntitledSection_DescribesItByPositionInsteadOfEmptyQuotes()
    {
        var diff = new WorkflowDiff(
            Sections:
            [
                new SectionDiff(string.Empty, 3, DiffChangeKind.Added),
                new SectionDiff("   ", 0, DiffChangeKind.Removed),
            ],
            Blocks: [],
            Variables: [],
            DocumentClasses: []);

        List<string> lines = SaveConflictDisclosure.BuildRevertLines(diff);

        Assert.Equal(
            [
                "Section 4 (untitled) — added",
                "Section 1 (untitled) — removed",
            ],
            lines);
    }

    [Fact]
    public void BuildRevertLines_EmptyDiff_ReturnsNoLines()
    {
        var diff = new WorkflowDiff([], [], [], []);

        Assert.Empty(SaveConflictDisclosure.BuildRevertLines(diff));
    }

    [Fact]
    public void BuildRevertLines_OversizedDiff_CapsWithRemainderLine()
    {
        List<BlockDiff> blocks = [.. Enumerable.Range(1, 20).Select(i =>
            new BlockDiff($"b{i}", $"Block {i}", "AiQuery", DiffChangeKind.Modified, []))];
        var diff = new WorkflowDiff([], blocks, [], []);

        List<string> lines = SaveConflictDisclosure.BuildRevertLines(diff);

        Assert.Equal(SaveConflictDisclosure.MAX_LINES, lines.Count);
        Assert.Equal("Block 'Block 11' — changed", lines[10]);
        Assert.Equal("…and 9 more changes", lines[^1]);
    }
}
