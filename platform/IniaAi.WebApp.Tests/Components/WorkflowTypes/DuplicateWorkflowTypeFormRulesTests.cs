using IniaAi.WebApp.Components.WorkflowTypes.Common;

namespace IniaAi.WebApp.Tests.Components.WorkflowTypes;

public class DuplicateWorkflowTypeFormRulesTests
{
    private const string SOURCE = "CorporateAnalysisPart2";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingId_ReturnsError(string? newId)
    {
        string? error = DuplicateWorkflowTypeFormRules.Validate(SOURCE, newId, "A name", []);

        Assert.Equal("ID is required", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingName_ReturnsError(string? name)
    {
        string? error = DuplicateWorkflowTypeFormRules.Validate(SOURCE, "NewType", name, []);

        Assert.Equal("Name is required", error);
    }

    [Fact]
    public void Validate_IdMatchingSource_ReturnsError()
    {
        string? error = DuplicateWorkflowTypeFormRules.Validate(SOURCE, SOURCE, "A name", []);

        Assert.NotNull(error);
        Assert.Contains("different", error);
    }

    [Fact]
    public void Validate_TakenId_ReturnsError_CaseInsensitively()
    {
        string? error = DuplicateWorkflowTypeFormRules.Validate(
            SOURCE,
            "corporateanalysispart2eu",
            "A name",
            ["CorporateAnalysisPart2EU"]);

        Assert.NotNull(error);
        Assert.Contains("already exists", error);
    }

    [Fact]
    public void Validate_OverlongId_ReturnsError()
    {
        string? error = DuplicateWorkflowTypeFormRules.Validate(SOURCE, new string('x', 101), "A name", []);

        Assert.NotNull(error);
        Assert.Contains("100", error);
    }

    [Fact]
    public void Validate_OverlongName_ReturnsError_AtTheColumnLimit()
    {
        Assert.Null(DuplicateWorkflowTypeFormRules.Validate(SOURCE, "NewType", new string('x', 250), []));

        string? error = DuplicateWorkflowTypeFormRules.Validate(SOURCE, "NewType", new string('x', 251), []);
        Assert.NotNull(error);
        Assert.Contains("250", error);
    }

    [Fact]
    public void Validate_ValidInput_ReturnsNull()
    {
        Assert.Null(DuplicateWorkflowTypeFormRules.Validate(SOURCE, "NewType", "A name", ["Other"]));
    }

    [Fact]
    public void SuggestId_AppendsCopySuffix_AndAvoidsTakenIds()
    {
        Assert.Equal("SourceCopy", DuplicateWorkflowTypeFormRules.SuggestId("Source", []));
        Assert.Equal("SourceCopy2", DuplicateWorkflowTypeFormRules.SuggestId("Source", ["SourceCopy"]));
        Assert.Equal("SourceCopy3", DuplicateWorkflowTypeFormRules.SuggestId("Source", ["SourceCopy", "SourceCopy2"]));
    }

    [Fact]
    public void SuggestId_StaysWithinTheColumnLimit()
    {
        string suggested = DuplicateWorkflowTypeFormRules.SuggestId(new string('x', 100), []);

        Assert.True(suggested.Length <= 100);
    }

    [Fact]
    public async Task SuggestId_TerminatesWhenTheSourceIdFillsTheColumnAndTheRootIsTaken()
    {
        string source = new('x', DuplicateWorkflowTypeFormRules.ID_MAX_LENGTH);
        string[] takenIds = [source, $"{source[..96]}Copy"];

        Task<string> suggestion = Task.Run(
            () => DuplicateWorkflowTypeFormRules.SuggestId(source, takenIds),
            TestContext.Current.CancellationToken);
        Task settled = await Task.WhenAny(
            suggestion,
            Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        Assert.Same(suggestion, settled);

        string suggested = await suggestion;
        Assert.True(suggested.Length <= DuplicateWorkflowTypeFormRules.ID_MAX_LENGTH);
        Assert.DoesNotContain(suggested, takenIds);
    }
}
