using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApp.Services;

namespace IniaAi.WebApp.Tests.Services;

public class VariantClassMapEditorTests
{
    #region CollectBlockClasses

    [Fact]
    public void CollectBlockClasses_CollectsDistinctSortedClassesExcludingSentinels()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        _ = new Block(BlockTypes.AiQuery, section)
        {
            ReplacementTag = "a",
            SupportedDocumentClasses = ["Fund1_OfferingMemo", "Fund1_AuditedFinancials"],
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };
        _ = new Block(BlockTypes.AiQuery, section)
        {
            ReplacementTag = "b",
            SupportedDocumentClasses = ["Fund1_AuditedFinancials", VariantDocumentClassEditor.ANY, VariantDocumentClassEditor.NONE],
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };

        List<string> classes = VariantClassMapEditor.CollectBlockClasses(section);

        Assert.Equal(["Fund1_AuditedFinancials", "Fund1_OfferingMemo"], classes);
    }

    [Fact]
    public void CollectBlockClasses_NoBlockClasses_ReturnsEmpty()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        _ = new Block(BlockTypes.AiQuery, section)
        {
            ReplacementTag = "a",
            SupportedDocumentClasses = [],
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };

        Assert.Empty(VariantClassMapEditor.CollectBlockClasses(section));
    }

    #endregion

    #region DeriveRows

    [Fact]
    public void DeriveRows_NoSavedMap_AllRowsUnchanged()
    {
        List<VariantClassMapEditor.Row> rows =
            VariantClassMapEditor.DeriveRows(["ClassA", "ClassB"], null);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Empty(row.Targets);
            Assert.False(row.IsStale);
        });
        Assert.Equal(["ClassA", "ClassB"], rows.Select(r => r.Source));
    }

    [Fact]
    public void DeriveRows_SavedMapEntryForBlockClass_PopulatesTargets()
    {
        List<VariantClassMapEditor.Row> rows = VariantClassMapEditor.DeriveRows(
            ["ClassA", "ClassB"],
            new Dictionary<string, List<string>> { ["ClassA"] = ["ClassA_F2", "ClassA_Extra"] });

        Assert.Equal(["ClassA_F2", "ClassA_Extra"], rows[0].Targets);
        Assert.Empty(rows[1].Targets);
    }

    [Fact]
    public void DeriveRows_SavedMapKeyNotOnAnyBlock_ProducesStaleRow()
    {
        List<VariantClassMapEditor.Row> rows = VariantClassMapEditor.DeriveRows(
            ["ClassA"],
            new Dictionary<string, List<string>> { ["Removed"] = ["Removed_F2"] });

        Assert.Equal(2, rows.Count);
        Assert.False(rows[0].IsStale);
        VariantClassMapEditor.Row staleRow = rows[1];
        Assert.True(staleRow.IsStale);
        Assert.Equal("Removed", staleRow.Source);
        Assert.Equal(["Removed_F2"], staleRow.Targets);
    }

    #endregion

    #region NormalizeTargets

    [Fact]
    public void NormalizeTargets_AddingSentinel_ReplacesSelection()
    {
        List<string> result = VariantClassMapEditor.NormalizeTargets(
            ["ClassA"], ["ClassA", VariantDocumentClassEditor.NONE]);

        Assert.Equal([VariantDocumentClassEditor.NONE], result);
    }

    [Fact]
    public void NormalizeTargets_AddingRealClass_StripsSentinels()
    {
        List<string> result = VariantClassMapEditor.NormalizeTargets(
            [VariantDocumentClassEditor.ANY], [VariantDocumentClassEditor.ANY, "ClassA"]);

        Assert.Equal(["ClassA"], result);
    }

    [Fact]
    public void NormalizeTargets_RemovingClass_KeepsRemaining()
    {
        List<string> result = VariantClassMapEditor.NormalizeTargets(
            ["ClassA", "ClassB"], ["ClassB"]);

        Assert.Equal(["ClassB"], result);
    }

    #endregion

    #region BuildMap

    [Fact]
    public void BuildMap_OmitsUnchangedAndIdentityRows()
    {
        List<VariantClassMapEditor.Row> rows =
        [
            new() { Source = "ClassA", Targets = ["ClassA_F2"] },
            new() { Source = "ClassB", Targets = [] },
            new() { Source = "ClassC", Targets = ["ClassC"] },
        ];

        Dictionary<string, List<string>> map = VariantClassMapEditor.BuildMap(rows);

        Assert.Single(map);
        Assert.Equal(["ClassA_F2"], map["ClassA"]);
    }

    [Fact]
    public void BuildMap_MultiTargetRow_KeptEvenWhenContainingSource()
    {
        List<VariantClassMapEditor.Row> rows =
        [
            new() { Source = "ClassA", Targets = ["ClassA", "ClassB"] },
        ];

        Dictionary<string, List<string>> map = VariantClassMapEditor.BuildMap(rows);

        Assert.Equal(["ClassA", "ClassB"], map["ClassA"]);
    }

    [Fact]
    public void BuildMap_AllRowsUnchanged_ReturnsEmptyDictionaryNotNull()
    {
        List<VariantClassMapEditor.Row> rows =
        [
            new() { Source = "ClassA", Targets = [] },
        ];

        Dictionary<string, List<string>> map = VariantClassMapEditor.BuildMap(rows);

        Assert.NotNull(map);
        Assert.Empty(map);
    }

    [Fact]
    public void BuildMap_IncludesRetainedStaleRows()
    {
        List<VariantClassMapEditor.Row> rows =
        [
            new() { Source = "ClassA", Targets = ["ClassA_F2"] },
            new() { Source = "Removed", Targets = ["Removed_F2"], IsStale = true },
        ];

        Dictionary<string, List<string>> map = VariantClassMapEditor.BuildMap(rows);

        Assert.Equal(2, map.Count);
        Assert.Equal(["Removed_F2"], map["Removed"]);
    }

    #endregion
}
