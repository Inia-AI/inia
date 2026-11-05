using IniaAi.WebApp.Services;

namespace IniaAi.WebApp.Tests.Services;

public class DocumentClassSelectionCanonicalizerTests
{
    [Fact]
    public void ApplyClick_TickedNoneOverAny_UnticksAny()
    {
        HashSet<string> ticked = DocumentClassSelectionCanonicalizer.ApplyClick(
            [VariantDocumentClassEditor.ANY],
            [VariantDocumentClassEditor.ANY, VariantDocumentClassEditor.NONE]);

        Assert.Equal([VariantDocumentClassEditor.NONE], ticked);
    }

    [Fact]
    public void ApplyClick_TickedNoneOverClasses_UnticksEveryClass()
    {
        HashSet<string> ticked = DocumentClassSelectionCanonicalizer.ApplyClick(
            ["Contract", "Invoice"],
            ["Contract", "Invoice", VariantDocumentClassEditor.NONE]);

        Assert.Equal([VariantDocumentClassEditor.NONE], ticked);
    }

    [Fact]
    public void ApplyClick_TickedAnyOverNone_UnticksNone()
    {
        HashSet<string> ticked = DocumentClassSelectionCanonicalizer.ApplyClick(
            [VariantDocumentClassEditor.NONE],
            [VariantDocumentClassEditor.NONE, VariantDocumentClassEditor.ANY]);

        Assert.Equal([VariantDocumentClassEditor.ANY], ticked);
    }

    [Fact]
    public void ApplyClick_TickedAnyOverClasses_UnticksEveryClass()
    {
        HashSet<string> ticked = DocumentClassSelectionCanonicalizer.ApplyClick(
            ["Contract", "Invoice"],
            ["Contract", "Invoice", VariantDocumentClassEditor.ANY]);

        Assert.Equal([VariantDocumentClassEditor.ANY], ticked);
    }

    [Fact]
    public void ApplyClick_TickedClassOverAny_UnticksAny()
    {
        HashSet<string> ticked = DocumentClassSelectionCanonicalizer.ApplyClick(
            [VariantDocumentClassEditor.ANY],
            [VariantDocumentClassEditor.ANY, "Contract"]);

        Assert.Equal(["Contract"], ticked);
    }

    [Fact]
    public void ApplyClick_TickedClassOverNone_UnticksNone()
    {
        HashSet<string> ticked = DocumentClassSelectionCanonicalizer.ApplyClick(
            [VariantDocumentClassEditor.NONE],
            [VariantDocumentClassEditor.NONE, "Contract"]);

        Assert.Equal(["Contract"], ticked);
    }

    [Fact]
    public void ApplyClick_TickedAnotherClass_KeepsTheTickedClasses()
    {
        HashSet<string> ticked = DocumentClassSelectionCanonicalizer.ApplyClick(
            ["Contract"],
            ["Contract", "Invoice"]);

        Assert.Equal(["Contract", "Invoice"], ticked);
    }

    [Fact]
    public void ApplyClick_Untick_StandsAsItIs()
    {
        HashSet<string> ticked = DocumentClassSelectionCanonicalizer.ApplyClick(
            ["Contract", "Invoice"],
            ["Contract"]);

        Assert.Equal(["Contract"], ticked);
    }

    [Fact]
    public void ApplyClick_UntickedTheLastItem_LeavesNothingTicked()
    {
        HashSet<string> ticked = DocumentClassSelectionCanonicalizer.ApplyClick(
            [VariantDocumentClassEditor.ANY],
            []);

        Assert.Empty(ticked);
    }

    [Fact]
    public void Commit_EmptiedNonEmptySelection_MeansNone()
    {
        IReadOnlyList<string> saved = DocumentClassSelectionCanonicalizer.Commit(["Contract"], []);

        Assert.Equal([VariantDocumentClassEditor.NONE], saved);
    }

    [Fact]
    public void Commit_UntouchedEmptySelection_StaysEmpty()
    {
        IReadOnlyList<string> saved = DocumentClassSelectionCanonicalizer.Commit([], []);

        Assert.Empty(saved);
    }

    [Fact]
    public void Commit_KeepsTheTickedItemsInTheirOrder()
    {
        IReadOnlyList<string> saved = DocumentClassSelectionCanonicalizer.Commit(
            [VariantDocumentClassEditor.ANY],
            ["Invoice", "Contract"]);

        Assert.Equal(["Invoice", "Contract"], saved);
    }
}
