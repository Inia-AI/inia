using IniaAi.Application.Queries.Workflows.Versions;
using IniaAi.WebApi.Contracts.Enums;
using IniaAi.WebApi.Contracts.Responses.WorkflowVersions;

namespace IniaAi.Application.UnitTests.Queries.Workflows.Versions;

public class WorkflowCardComparerTests
{
    private static readonly DateTime _baseTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Compare_IdenticalCards_AllMatch()
    {
        CurrencyComparisonDto grid = WorkflowCardComparer.Compare(
        [
            Card("Inst-A", Entry("WT1", _baseTime, "h1")),
            Card("Inst-B", Entry("WT1", _baseTime, "h1")),
        ]);

        CurrencyComparisonRowDto row = Assert.Single(grid.Rows);
        Assert.All(row.Cells, cell => Assert.Equal(CurrencyCellStatus.Match, cell.Status));
    }

    [Fact]
    public void Compare_EarlierPointInSameLine_IsBehind()
    {
        CurrencyComparisonDto grid = WorkflowCardComparer.Compare(
        [
            Card("Inst-A", Entry("WT1", _baseTime.AddDays(1), "h1", "h2")),
            Card("Inst-B", Entry("WT1", _baseTime, "h1")),
        ]);

        CurrencyComparisonRowDto row = Assert.Single(grid.Rows);
        Assert.Equal(CurrencyCellStatus.Match, Cell(row, "Inst-A").Status);
        Assert.Equal(CurrencyCellStatus.Behind, Cell(row, "Inst-B").Status);
    }

    [Fact]
    public void Compare_LaterSaveButShorterLineage_IsAhead()
    {
        CurrencyComparisonDto grid = WorkflowCardComparer.Compare(
        [
            Card("Inst-A", Entry("WT1", _baseTime.AddDays(1), "h1")),
            Card("Inst-B", Entry("WT1", _baseTime, "h1", "h2")),
        ]);

        CurrencyComparisonRowDto row = Assert.Single(grid.Rows);
        Assert.Equal(CurrencyCellStatus.Match, Cell(row, "Inst-A").Status);
        Assert.Equal(CurrencyCellStatus.Ahead, Cell(row, "Inst-B").Status);
    }

    [Fact]
    public void Compare_CommonAncestorThenIndependentEdits_IsDiverged()
    {
        CurrencyComparisonDto grid = WorkflowCardComparer.Compare(
        [
            Card("Inst-A", Entry("WT1", _baseTime, "h0", "hA")),
            Card("Inst-B", Entry("WT1", _baseTime.AddDays(1), "h0", "hB")),
        ]);

        CurrencyComparisonRowDto row = Assert.Single(grid.Rows);
        Assert.Equal(CurrencyCellStatus.Match, Cell(row, "Inst-B").Status);
        Assert.Equal(CurrencyCellStatus.Diverged, Cell(row, "Inst-A").Status);
    }

    [Fact]
    public void Compare_EntryWithoutLineage_DoesNotThrow_AndDiverges()
    {
        CurrencyCardDto withLineage = Card("Inst-A", Entry("WT1", _baseTime.AddDays(1), "h1", "h2"));
        var noLineage = new CurrencyCardDto("Inst-B", _baseTime,
        [
            new CurrencyCardEntryDto("WT1", "ver-x", "hX", "hX", _baseTime, "user", "User Name", null!),
        ]);

        CurrencyComparisonDto grid = WorkflowCardComparer.Compare([withLineage, noLineage]);

        CurrencyComparisonRowDto row = Assert.Single(grid.Rows);
        Assert.Equal(CurrencyCellStatus.Match, Cell(row, "Inst-A").Status);
        Assert.Equal(CurrencyCellStatus.Diverged, Cell(row, "Inst-B").Status);
    }

    [Fact]
    public void Compare_WorkflowMissingOnOneInstance_MissingCell()
    {
        CurrencyComparisonDto grid = WorkflowCardComparer.Compare(
        [
            Card("Inst-A", Entry("WT1", _baseTime, "h1")),
            Card("Inst-B"),
        ]);

        CurrencyComparisonRowDto row = Assert.Single(grid.Rows);
        Assert.Equal(CurrencyCellStatus.Match, Cell(row, "Inst-A").Status);
        Assert.Equal(CurrencyCellStatus.Missing, Cell(row, "Inst-B").Status);
    }

    [Fact]
    public void Compare_SortsInstancesAndWorkflows()
    {
        CurrencyComparisonDto grid = WorkflowCardComparer.Compare(
        [
            Card("Zeta", Entry("WT-B", _baseTime, "h"), Entry("WT-A", _baseTime, "h")),
            Card("Alpha", Entry("WT-A", _baseTime, "h")),
        ]);

        Assert.Equal(["Alpha", "Zeta"], grid.InstanceNames);
        Assert.Equal(["WT-A", "WT-B"], [.. grid.Rows.Select(r => r.WorkflowTypeId)]);
    }

    [Fact]
    public void Compare_ARow_IsNamedAsTheNewestReportNamesItsWorkflowType()
    {
        CurrencyComparisonDto grid = WorkflowCardComparer.Compare(
        [
            Card("Inst-A", Entry("WT1", _baseTime, "h1") with { WorkflowTypeName = "Old name" }),
            Card("Inst-B", Entry("WT1", _baseTime.AddDays(1), "h1", "h2") with { WorkflowTypeName = "Form 10-K analysis" }),
        ]);

        Assert.Equal("Form 10-K analysis", Assert.Single(grid.Rows).WorkflowTypeName);
    }

    [Fact]
    public void Compare_ARowFromReportsThatCarryNoName_ShowsTheIdentifierUnaltered()
    {
        CurrencyComparisonDto grid = WorkflowCardComparer.Compare(
        [
            Card("Inst-A", Entry("QA-qa-20260917T184538Z-a9bfcb-fixture", _baseTime, "h1")),
            Card("Inst-B", Entry("Form10KAnalysis", _baseTime, "h1")),
        ]);

        Assert.Equal(
            ["Form10KAnalysis", "QA-qa-20260917T184538Z-a9bfcb-fixture"],
            grid.Rows.Select(row => row.WorkflowTypeName));
    }

    [Fact]
    public void Compare_Rows_AreOrderedByTheNameTheGridShows()
    {
        CurrencyComparisonDto grid = WorkflowCardComparer.Compare(
        [
            Card(
                "Inst-A",
                Entry("A_Type", _baseTime, "h1") with { WorkflowTypeName = "Zeta review" },
                Entry("B_Type", _baseTime, "h2") with { WorkflowTypeName = "alpha review" },
                Entry("C_Type", _baseTime, "h3") with { WorkflowTypeName = "Zeta review" }),
        ]);

        Assert.Equal(["B_Type", "A_Type", "C_Type"], grid.Rows.Select(row => row.WorkflowTypeId));
    }

    private static CurrencyComparisonCellDto Cell(CurrencyComparisonRowDto row, string instanceName)
    {
        return row.Cells.Single(c => c.InstanceName == instanceName);
    }

    private static CurrencyCardDto Card(string instance, params CurrencyCardEntryDto[] entries)
    {
        return new CurrencyCardDto(instance, _baseTime, entries);
    }

    private static CurrencyCardEntryDto Entry(string workflowTypeId, DateTime savedUtc, params string[] historyHashes)
    {
        string head = historyHashes[^1];
        return new CurrencyCardEntryDto(
            workflowTypeId, $"ver-{head}", head, head, savedUtc, "user", "User Name", historyHashes);
    }
}
