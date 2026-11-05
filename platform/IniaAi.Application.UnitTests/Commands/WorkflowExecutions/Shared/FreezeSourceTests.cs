using IniaAi.Application.Commands.WorkflowExecutions.Shared;

namespace IniaAi.Application.UnitTests.Commands.WorkflowExecutions.Shared;

/// <summary>
/// The submission's freeze option as one value. A submission naming two sources does not compile,
/// so what is left to pin is how the wire's two optional identifiers become that one value.
/// </summary>
public class FreezeSourceTests
{
    [Fact]
    public void FromSubmissionOptions_WithNeitherIdentifier_IsNoFreezeSource()
    {
        Assert.Null(FreezeSource.FromSubmissionOptions(null, null));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void FromSubmissionOptions_WithABlankIdentifier_IsNoFreezeSource(string blank)
    {
        Assert.Null(FreezeSource.FromSubmissionOptions(blank, blank));
    }

    [Fact]
    public void FromSubmissionOptions_WithAnExecutionId_NamesTheExecution()
    {
        FreezeSource.Execution source = Assert.IsType<FreezeSource.Execution>(
            FreezeSource.FromSubmissionOptions("exec-1", null));

        Assert.Equal("exec-1", source.ExecutionId);
    }

    [Fact]
    public void FromSubmissionOptions_WithACaseId_NamesTheCase()
    {
        FreezeSource.Case source = Assert.IsType<FreezeSource.Case>(
            FreezeSource.FromSubmissionOptions(null, "case-1"));

        Assert.Equal("case-1", source.CaseId);
    }

    /// <summary>
    /// A blank identifier beside a real one names the real one — the wire's own rule is that a
    /// submission carries at most one, and a blank string carries nothing at all.
    /// </summary>
    [Fact]
    public void FromSubmissionOptions_WithABlankExecutionIdBesideACaseId_NamesTheCase()
    {
        FreezeSource.Case source = Assert.IsType<FreezeSource.Case>(
            FreezeSource.FromSubmissionOptions(" ", "case-1"));

        Assert.Equal("case-1", source.CaseId);
    }

    /// <summary>
    /// The validator answers this 400 before the handler runs, so reaching here is a programming
    /// error — and dropping one of the two silently would stamp the execution as if no case were
    /// involved.
    /// </summary>
    [Fact]
    public void FromSubmissionOptions_NamingBothSources_IsAProgrammingError()
    {
        _ = Assert.Throws<InvalidOperationException>(
            () => FreezeSource.FromSubmissionOptions("exec-1", "case-1"));
    }
}
