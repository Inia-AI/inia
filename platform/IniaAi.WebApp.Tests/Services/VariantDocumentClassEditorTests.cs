using IniaAi.WebApp.Services;

namespace IniaAi.WebApp.Tests.Services;

public class VariantDocumentClassEditorTests
{
    [Fact]
    public void Add_Any_ReplacesEntireSelectionWithAny()
    {
        List<string> classes = ["Contract", "Invoice"];

        VariantDocumentClassEditor.Add(classes, VariantDocumentClassEditor.ANY);

        Assert.Equal([VariantDocumentClassEditor.ANY], classes);
    }

    [Fact]
    public void Add_None_ReplacesEntireSelectionWithNone()
    {
        List<string> classes = ["Contract", VariantDocumentClassEditor.ANY];

        VariantDocumentClassEditor.Add(classes, VariantDocumentClassEditor.NONE);

        Assert.Equal([VariantDocumentClassEditor.NONE], classes);
    }

    [Fact]
    public void Add_RealClass_StripsAnyAndNoneFirst()
    {
        List<string> classes = [VariantDocumentClassEditor.ANY, VariantDocumentClassEditor.NONE];

        VariantDocumentClassEditor.Add(classes, "Contract");

        Assert.Equal(["Contract"], classes);
    }

    [Fact]
    public void Add_RealClass_KeepsExistingRealClasses()
    {
        List<string> classes = ["Contract"];

        VariantDocumentClassEditor.Add(classes, "Invoice");

        Assert.Equal(["Contract", "Invoice"], classes);
    }

    [Fact]
    public void Add_Duplicate_IsNoOp()
    {
        List<string> classes = ["Contract"];

        VariantDocumentClassEditor.Add(classes, "Contract");

        Assert.Equal(["Contract"], classes);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Add_BlankValue_IsNoOp(string? value)
    {
        List<string> classes = ["Contract"];

        VariantDocumentClassEditor.Add(classes, value!);

        Assert.Equal(["Contract"], classes);
    }

    [Fact]
    public void Remove_RemovesMatchingValueOnly()
    {
        List<string> classes = ["Contract", "Invoice", "Report"];

        VariantDocumentClassEditor.Remove(classes, "Invoice");

        Assert.Equal(["Contract", "Report"], classes);
    }

    [Fact]
    public void Remove_LastClass_FallsBackToAny()
    {
        List<string> classes = ["Contract"];

        VariantDocumentClassEditor.Remove(classes, "Contract");

        Assert.Equal([VariantDocumentClassEditor.ANY], classes);
    }

    [Fact]
    public void Remove_NoneWhenItIsTheOnlyEntry_FallsBackToAny()
    {
        List<string> classes = [VariantDocumentClassEditor.NONE];

        VariantDocumentClassEditor.Remove(classes, VariantDocumentClassEditor.NONE);

        Assert.Equal([VariantDocumentClassEditor.ANY], classes);
    }

    [Fact]
    public void Remove_ValueNotPresent_LeavesSelectionUnchanged()
    {
        List<string> classes = ["Contract", "Invoice"];

        VariantDocumentClassEditor.Remove(classes, "Report");

        Assert.Equal(["Contract", "Invoice"], classes);
    }
}
