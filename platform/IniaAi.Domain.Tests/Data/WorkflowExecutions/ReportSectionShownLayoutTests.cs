using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Domain.Tests.Data.WorkflowExecutions;

public class ReportSectionShownLayoutTests
{
    private static readonly GridElementPosition _paragraphLeft = new() { BlockOrder = 0, Row = 1, Column = 1, RowSpan = 1, ColumnSpan = 3 };
    private static readonly GridElementPosition _chartRight = new() { BlockOrder = 1, Row = 1, Column = 4, RowSpan = 1, ColumnSpan = 2 };

    [Fact]
    public void AParagraphBesideItsChart_KeepsItsPlace()
    {
        ReportSection section = SectionShowing(FiveColumns(_paragraphLeft, _chartRight), 0, 1);

        GridLayoutOutput shown = section.ShownLayout()!;

        Assert.Equivalent(new[] { _paragraphLeft, _chartRight }, shown.Elements, strict: true);
    }

    [Fact]
    public void AParagraphWhoseChartIsMissing_TakesTheFullWidth()
    {
        ReportSection section = SectionShowing(FiveColumns(_paragraphLeft, _chartRight), 0);

        GridLayoutOutput shown = section.ShownLayout()!;

        GridElementPosition paragraph = Assert.Single(shown.Elements);
        Assert.Equivalent(new GridElementPosition { BlockOrder = 0, Row = 1, Column = 1, RowSpan = 1, ColumnSpan = 5 }, paragraph, strict: true);
    }

    [Fact]
    public void AParagraphRightOfAMissingChart_MovesToTheFirstColumn()
    {
        GridLayoutOutput chartThenParagraph = FiveColumns(
            new GridElementPosition { BlockOrder = 0, Row = 1, Column = 1, RowSpan = 1, ColumnSpan = 2 },
            new GridElementPosition { BlockOrder = 1, Row = 1, Column = 3, RowSpan = 1, ColumnSpan = 3 });

        GridLayoutOutput shown = SectionShowing(chartThenParagraph, 1).ShownLayout()!;

        GridElementPosition paragraph = Assert.Single(shown.Elements);
        Assert.Equal(1, paragraph.Column);
        Assert.Equal(5, paragraph.ColumnSpan);
    }

    [Fact]
    public void AParagraphBesideAChartThatSpansDownIntoItsRow_KeepsItsPlace()
    {
        var tallChart = new GridElementPosition { BlockOrder = 0, Row = 1, Column = 4, RowSpan = 2, ColumnSpan = 2 };
        var paragraphBelow = new GridElementPosition { BlockOrder = 1, Row = 2, Column = 1, RowSpan = 1, ColumnSpan = 3 };

        GridLayoutOutput shown = SectionShowing(FiveColumns(tallChart, paragraphBelow), 0, 1).ShownLayout()!;

        Assert.Equivalent(new[] { tallChart, paragraphBelow }, shown.Elements, strict: true);
    }

    [Fact]
    public void AnElementTheWorkflowPlacedAlone_TakesTheFullWidth()
    {
        var paragraph = new GridElementPosition { BlockOrder = 0, Row = 1, Column = 1, RowSpan = 1, ColumnSpan = 1 };
        var chart = new GridElementPosition { BlockOrder = 1, Row = 1, Column = 2, RowSpan = 1, ColumnSpan = 1 };
        var chartAloneBelow = new GridElementPosition { BlockOrder = 2, Row = 2, Column = 2, RowSpan = 1, ColumnSpan = 1 };
        var twoColumns = new GridLayoutOutput { Mode = GridLayoutOutput.GRID_MODE, Columns = 2, Elements = [paragraph, chart, chartAloneBelow] };

        GridLayoutOutput shown = SectionShowing(twoColumns, 0, 1, 2).ShownLayout()!;

        Assert.Equivalent(
            new[] { paragraph, chart, new GridElementPosition { BlockOrder = 2, Row = 2, Column = 1, RowSpan = 1, ColumnSpan = 2 } },
            shown.Elements,
            strict: true);
    }

    [Fact]
    public void ALayoutWithNoColumnCount_WidensAnElementToTheTwoColumnsTheReportDraws()
    {
        GridLayoutOutput noColumnCount = FiveColumns(_paragraphLeft, _chartRight);
        noColumnCount.Columns = 0;

        GridElementPosition paragraph = Assert.Single(SectionShowing(noColumnCount, 0).ShownLayout()!.Elements);

        Assert.Equal(2, paragraph.ColumnSpan);
    }

    [Fact]
    public void TheStoredLayout_StaysAsTheWorkflowDefinedIt()
    {
        GridLayoutOutput stored = FiveColumns(_paragraphLeft, _chartRight);
        ReportSection section = SectionShowing(stored, 0);

        _ = section.ShownLayout();

        Assert.Equivalent(new[] { _paragraphLeft, _chartRight }, stored.Elements, strict: true);
    }

    [Fact]
    public void ASequentialLayout_ReadsAsStored()
    {
        var sequential = new GridLayoutOutput { Mode = "sequential" };

        Assert.Same(sequential, SectionShowing(sequential, 0).ShownLayout());
    }

    [Fact]
    public void ASectionWithNoLayout_HasNone()
    {
        Assert.Null(new ReportSection().ShownLayout());
    }

    private static GridLayoutOutput FiveColumns(params GridElementPosition[] elements)
    {
        return new GridLayoutOutput { Name = "Summary grid", Mode = GridLayoutOutput.GRID_MODE, Columns = 5, Elements = [.. elements] };
    }

    private static ReportSection SectionShowing(GridLayoutOutput layout, params int[] blockOrders)
    {
        var section = new ReportSection { Layout = layout };
        foreach (int blockOrder in blockOrders)
        {
            section.Paragraphs.Add(new ReportParagraph { Order = blockOrder });
        }

        return section;
    }
}
