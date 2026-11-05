using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using Microsoft.Extensions.DependencyInjection;
using System.Text.RegularExpressions;

namespace IniaAi.Core.Tests.Workflows.Strategies;

public class StringManipulationStrategyTests : IClassFixture<WorkflowsFixture<StringManipulationStrategyTests>>
{
    private readonly IServiceProvider _testServiceProvider;
    private readonly ITestOutputHelper _outputHelper;

    public StringManipulationStrategyTests(
        WorkflowsFixture<StringManipulationStrategyTests> fixture,
        ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
        _testServiceProvider = fixture.BuildTestServiceProvider(_outputHelper);
    }

    [Fact]
    public async Task ExecuteBlockAsync_EscapeSpecialCharacters_ReplacesNewlinesWithBackslashN()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "first\nsecond",
            StringManipulationOperation.EscapeSpecialCharacters);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("first\\nsecond", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_EscapeSpecialCharacters_ReplacesTabsWithBackslashT()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "col1\tcol2",
            StringManipulationOperation.EscapeSpecialCharacters);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("col1\\tcol2", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_EscapeSpecialCharacters_ReplacesCarriageReturnsWithBackslashR()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "windows\r\nline",
            StringManipulationOperation.EscapeSpecialCharacters);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("windows\\r\\nline", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_EscapeSpecialCharacters_EscapesBackslashesBeforeOtherCharacters()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "path\\file\nend",
            StringManipulationOperation.EscapeSpecialCharacters);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("path\\\\file\\nend", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_EscapeSpecialCharacters_DoubleQuotesToggleOffByDefault()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "he said \"hi\"",
            StringManipulationOperation.EscapeSpecialCharacters);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("he said \"hi\"", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_EscapeSpecialCharacters_WithDoubleQuotesToggleOn()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "he said \"hi\"",
            StringManipulationOperation.EscapeSpecialCharacters,
            configure: p => p.EscapeDoubleQuotes = true);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("he said \\\"hi\\\"", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_EscapeSpecialCharacters_AllTogglesOffIsPassthrough()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "a\nb\tc\\d\"e",
            StringManipulationOperation.EscapeSpecialCharacters,
            configure: p =>
            {
                p.EscapeNewlines = false;
                p.EscapeTabs = false;
                p.EscapeCarriageReturns = false;
                p.EscapeBackslashes = false;
                p.EscapeDoubleQuotes = false;
            });

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("a\nb\tc\\d\"e", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_UnescapeSpecialCharacters_BackslashNBecomesNewline()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "first\\nsecond",
            StringManipulationOperation.UnescapeSpecialCharacters);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("first\nsecond", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_UnescapeSpecialCharacters_RoundTripsEscape()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "\\\\\\n",
            StringManipulationOperation.UnescapeSpecialCharacters);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("\\\n", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_UnescapeSpecialCharacters_UnknownEscapeLeftIntact()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "a\\xb",
            StringManipulationOperation.UnescapeSpecialCharacters);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("a\\xb", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_UnescapeSpecialCharacters_TrailingBackslashUnchanged()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "end\\",
            StringManipulationOperation.UnescapeSpecialCharacters);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("end\\", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_UnescapeSpecialCharacters_DoubleQuoteToggleOffLeavesBackslashQuote()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "a\\\"b",
            StringManipulationOperation.UnescapeSpecialCharacters);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("a\\\"b", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_UnescapeSpecialCharacters_DoubleQuoteToggleOnUnescapes()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "a\\\"b",
            StringManipulationOperation.UnescapeSpecialCharacters,
            configure: p => p.EscapeDoubleQuotes = true);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("a\"b", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Replace_LiteralSubstitution()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "hello world",
            StringManipulationOperation.Replace,
            configure: p =>
            {
                p.Find = "world";
                p.ReplaceWith = "there";
            });

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("hello there", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Replace_CaseInsensitiveMatches()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "Hello WORLD",
            StringManipulationOperation.Replace,
            configure: p =>
            {
                p.Find = "world";
                p.ReplaceWith = "there";
                p.CaseSensitive = false;
            });

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("Hello there", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Replace_CaseSensitiveSkipsMismatch()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "Hello WORLD",
            StringManipulationOperation.Replace,
            configure: p =>
            {
                p.Find = "world";
                p.ReplaceWith = "there";
                p.CaseSensitive = true;
            });

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("Hello WORLD", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Replace_EmptyFindLeavesInputUnchanged()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "unchanged",
            StringManipulationOperation.Replace,
            configure: p =>
            {
                p.Find = "";
                p.ReplaceWith = "X";
            });

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("unchanged", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Replace_NewlineTokenInFindMatchesRealNewline()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "line1\nline2",
            StringManipulationOperation.Replace,
            configure: p =>
            {
                p.Find = StringManipulationStrategy.TOKEN_NEWLINE;
                p.ReplaceWith = " / ";
            });

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("line1 / line2", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Replace_NewlineTokenInReplaceWithProducesRealNewline()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "a|b|c",
            StringManipulationOperation.Replace,
            configure: p =>
            {
                p.Find = "|";
                p.ReplaceWith = StringManipulationStrategy.TOKEN_NEWLINE;
            });

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("a\nb\nc", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Replace_TabAndSpaceTokens()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "col1\tcol2",
            StringManipulationOperation.Replace,
            configure: p =>
            {
                p.Find = StringManipulationStrategy.TOKEN_TAB;
                p.ReplaceWith = StringManipulationStrategy.TOKEN_SPACE;
            });

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("col1 col2", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ReplaceUsingPattern_CaptureGroups()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "price: 1250.00 USD",
            StringManipulationOperation.ReplaceUsingPattern,
            configure: p =>
            {
                p.Find = @"(\d+)\.(\d{2})";
                p.ReplaceWith = "$1 dollars $2 cents";
            });

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("price: 1250 dollars 00 cents USD", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ReplaceUsingPattern_CaseInsensitive()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "Hello HELLO hello",
            StringManipulationOperation.ReplaceUsingPattern,
            configure: p =>
            {
                p.Find = "hello";
                p.ReplaceWith = "X";
                p.CaseSensitive = false;
            });

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("X X X", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ReplaceUsingPattern_InvalidRegexLogsWarningAndProducesNoResponse()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "text",
            StringManipulationOperation.ReplaceUsingPattern,
            configure: p =>
            {
                p.Find = "[";
                p.ReplaceWith = "X";
            });

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Null(block.BlockResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_InvalidRegex_NotCritical_SetsNoError()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "text",
            StringManipulationOperation.ReplaceUsingPattern,
            configure: p =>
            {
                p.Find = "[";
                p.ReplaceWith = "X";
            });

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Null(block.ExecutionState?.Error);
    }

    [Fact]
    public async Task ExecuteBlockAsync_InvalidRegex_Critical_FailsTheBlock()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "text",
            StringManipulationOperation.ReplaceUsingPattern,
            configure: p =>
            {
                p.Find = "[";
                p.ReplaceWith = "X";
                p.IsCritical = true;
            });

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Null(block.BlockResponse);
        Assert.NotNull(block.ExecutionState);
        Assert.Equal(
            "The String Manipulation block 'String Manipulation' could not process its input, so it failed. "
            + "Check that its Find (regex pattern) is a valid regular expression.",
            block.ExecutionState.Error);
    }

    [Fact]
    public void FailureNextStep_PatternTimedOut_SaysItTookTooLongNotThatItIsInvalid()
    {
        string step = StringManipulationStrategy.FailureNextStep(
            StringManipulationOperation.ReplaceUsingPattern,
            new RegexMatchTimeoutException("aaaa!", "(a+)+$", TimeSpan.FromSeconds(1)));

        Assert.Equal(
            "Its Find (regex pattern) took too long on this input. Simplify the pattern, or split the input.",
            step);
    }

    [Fact]
    public async Task ExecuteBlockAsync_JoinOfNonList_Critical_NamesTheInputInsteadOfAFindPattern()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "not a list",
            StringManipulationOperation.Join,
            configure: p =>
            {
                p.Separator = ",";
                p.IsCritical = true;
            });

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Null(block.BlockResponse);
        Assert.NotNull(block.ExecutionState);
        Assert.Equal(
            "The String Manipulation block 'String Manipulation' could not process its input, so it failed. "
            + "Check that its input is a JSON list of text values, such as [\"a\", \"b\"].",
            block.ExecutionState.Error);
    }

    [Fact]
    public async Task ExecuteBlockAsync_JoinOfNonList_NotCritical_SetsNoErrorAndNoResponse()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "not a list",
            StringManipulationOperation.Join,
            configure: p => p.Separator = ",");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Null(block.BlockResponse);
        Assert.Null(block.ExecutionState?.Error);
    }

    [Fact]
    public async Task ExecuteBlockAsync_NullInput_Critical_StaysSilent()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: null,
            StringManipulationOperation.ChangeCase,
            configure: p => p.IsCritical = true);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Null(block.BlockResponse);
        Assert.Null(block.ExecutionState?.Error);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Substring_StartIndexAndLength()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "abcdef",
            StringManipulationOperation.Substring,
            configure: p => { p.StartIndex = 2; p.Length = 3; });

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("cde", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Substring_NullLengthReadsToEnd()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "abcdef",
            StringManipulationOperation.Substring,
            configure: p => { p.StartIndex = 2; p.Length = null; });

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("cdef", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Substring_NegativeStartIndexClampsToZero()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "abcdef",
            StringManipulationOperation.Substring,
            configure: p => { p.StartIndex = -5; p.Length = 2; });

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("ab", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Substring_StartBeyondEndReturnsEmpty()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "abc",
            StringManipulationOperation.Substring,
            configure: p => { p.StartIndex = 10; });

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Substring_LengthOverflowsIsClamped()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "abcdef",
            StringManipulationOperation.Substring,
            configure: p => { p.StartIndex = 4; p.Length = 100; });

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("ef", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Trim_BothSides()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "  hello  ",
            StringManipulationOperation.Trim);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("hello", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Trim_StartOnly()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "  hello  ",
            StringManipulationOperation.Trim,
            configure: p => p.TrimSide = StringManipulationTrimSide.Start);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("hello  ", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Trim_CustomCharacters()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "---hello---",
            StringManipulationOperation.Trim,
            configure: p => p.TrimCharacters = "-");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("hello", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Trim_NewlineTokenInTrimCharacters()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "\n\nhello\n",
            StringManipulationOperation.Trim,
            configure: p => p.TrimCharacters = StringManipulationStrategy.TOKEN_NEWLINE);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("hello", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Split_ReturnsJsonArrayWhenIndexIsNull()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "a,b,c",
            StringManipulationOperation.Split,
            configure: p => { p.Separator = ","; p.SplitIndex = null; });

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("[\"a\",\"b\",\"c\"]", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Split_PicksPositiveIndex()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "a,b,c",
            StringManipulationOperation.Split,
            configure: p => { p.Separator = ","; p.SplitIndex = 1; });

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("b", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Split_NegativeIndexFromEnd()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "a,b,c",
            StringManipulationOperation.Split,
            configure: p => { p.Separator = ","; p.SplitIndex = -1; });

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("c", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Split_OutOfBoundsIndexReturnsEmpty()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "a,b,c",
            StringManipulationOperation.Split,
            configure: p => { p.Separator = ","; p.SplitIndex = 99; });

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Split_NewlineTokenInSeparator()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "line1\nline2\nline3",
            StringManipulationOperation.Split,
            configure: p =>
            {
                p.Separator = StringManipulationStrategy.TOKEN_NEWLINE;
                p.SplitIndex = 1;
            });

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("line2", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Join_ConcatenatesArrayWithSeparator()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "[\"a\",\"b\",\"c\"]",
            StringManipulationOperation.Join,
            configure: p => p.Separator = " - ");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("a - b - c", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Join_NewlineTokenInSeparator()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "[\"a\",\"b\",\"c\"]",
            StringManipulationOperation.Join,
            configure: p => p.Separator = StringManipulationStrategy.TOKEN_NEWLINE);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("a\nb\nc", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Join_MalformedJsonLogsWarningAndProducesNoResponse()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "not-json",
            StringManipulationOperation.Join,
            configure: p => p.Separator = ",");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Null(block.BlockResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ChangeCase_Upper()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "hello WORLD",
            StringManipulationOperation.ChangeCase,
            configure: p => p.CaseType = StringManipulationCaseType.Upper);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("HELLO WORLD", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ChangeCase_Lower()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "Hello WORLD",
            StringManipulationOperation.ChangeCase,
            configure: p => p.CaseType = StringManipulationCaseType.Lower);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("hello world", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ChangeCase_Title()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "the quick brown fox",
            StringManipulationOperation.ChangeCase,
            configure: p => p.CaseType = StringManipulationCaseType.Title);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("The Quick Brown Fox", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ChangeCase_Sentence()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "hello WORLD. how Are You?",
            StringManipulationOperation.ChangeCase,
            configure: p => p.CaseType = StringManipulationCaseType.Sentence);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("Hello world. how are you?", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Length_ReturnsCharCount()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "abcde",
            StringManipulationOperation.Length);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("5", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Length_EmptyInputStillProducesZeroNotWarning()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "",
            StringManipulationOperation.Length);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("0", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Distinct_RemovesDuplicates_PreservingFirstOccurrenceOrder()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "b\na\nb\nc\na",
            StringManipulationOperation.Distinct);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("b\na\nc", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Distinct_CaseSensitive_KeepsDifferentCases()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "Foo\nFOO\nfoo",
            StringManipulationOperation.Distinct,
            configure: p => p.CaseSensitive = true);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("Foo\nFOO\nfoo", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Distinct_CaseInsensitive_TreatsCasesAsSame()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "Foo\nFOO\nfoo",
            StringManipulationOperation.Distinct,
            configure: p => p.CaseSensitive = false);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("Foo", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Distinct_TrimsEachLine_EmitsTrimmedForm()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "  foo  \nfoo",
            StringManipulationOperation.Distinct);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("foo", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Distinct_DropsEmptyAndWhitespaceOnlyLines()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "foo\n\n   \nbar",
            StringManipulationOperation.Distinct);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("foo\nbar", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Distinct_NormalizesCrlfToLf()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "a\r\nb\r\na",
            StringManipulationOperation.Distinct);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("a\nb", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Distinct_NoTrailingNewlineEvenIfInputHadOne()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "a\nb\n",
            StringManipulationOperation.Distinct);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("a\nb", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Distinct_NewlineTokenInInputResolves()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: $"a{StringManipulationStrategy.TOKEN_NEWLINE}b{StringManipulationStrategy.TOKEN_NEWLINE}a",
            StringManipulationOperation.Distinct);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("a\nb", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Distinct_SingleItem_Unchanged()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "foo",
            StringManipulationOperation.Distinct);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("foo", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Distinct_AllDuplicates_CollapsesToOne()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "foo\nfoo\nfoo",
            StringManipulationOperation.Distinct);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("foo", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Distinct_EmptyInput_EmptyOutput()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: "",
            StringManipulationOperation.Distinct);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("", block.BlockResponse.RawResponse);
    }

    // ── Token normalization applies to Input itself ────────────────────────────

    [Fact]
    public async Task ExecuteBlockAsync_TokenInInputIsNormalized()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: $"a{StringManipulationStrategy.TOKEN_NEWLINE}b",
            StringManipulationOperation.Length);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        // "a\nb" = 3 characters, not 14 (the token length)
        Assert.Equal("3", block.BlockResponse.RawResponse);
    }

    // ── Placeholder resolution ─────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteBlockAsync_PlaceholderInInputTemplate_IsResolved()
    {
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.StringManipulation, workflow)
        {
            Name = "String Manipulation",
            Properties = new StringManipulationProperties
            {
                InputTemplate = "hello {name}",
                Operation = StringManipulationOperation.ChangeCase,
                CaseType = StringManipulationCaseType.Upper,
            }
        };
        block.UpdateExecutionState(
            asPlaceHolder: true,
            inputs: new Dictionary<string, string?> { ["name"] = "world" });

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new StringManipulationStrategy();

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("HELLO WORLD", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_PlaceholderInFindAndReplaceWith_AreResolved()
    {
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.StringManipulation, workflow)
        {
            Name = "String Manipulation",
            Properties = new StringManipulationProperties
            {
                InputTemplate = "I love cats",
                Operation = StringManipulationOperation.Replace,
                Find = "{target}",
                ReplaceWith = "{replacement}",
            }
        };
        block.UpdateExecutionState(
            asPlaceHolder: true,
            inputs: new Dictionary<string, string?>
            {
                ["target"] = "cats",
                ["replacement"] = "dogs",
            });

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new StringManipulationStrategy();

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("I love dogs", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_PlaceholderInTrimCharacters_IsResolved()
    {
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.StringManipulation, workflow)
        {
            Name = "String Manipulation",
            Properties = new StringManipulationProperties
            {
                InputTemplate = "---hello---",
                Operation = StringManipulationOperation.Trim,
                TrimCharacters = "{marker}",
            }
        };
        block.UpdateExecutionState(
            asPlaceHolder: true,
            inputs: new Dictionary<string, string?> { ["marker"] = "-" });

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new StringManipulationStrategy();

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("hello", block.BlockResponse.RawResponse);
    }

    // ── NullInput ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteBlockAsync_NullInput_LogsWarningAndProducesNoResponse()
    {
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: null,
            StringManipulationOperation.EscapeSpecialCharacters);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Null(block.BlockResponse);
    }

    // ── Regex DoS guard ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteBlockAsync_ReplaceUsingPattern_CatastrophicBacktrackingTimesOutAndLogs()
    {
        // Classic catastrophic-backtracking pattern applied to input that never matches the trailing b.
        (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) = CreateTestSetup(
            input: new string('a', 30) + "!",
            StringManipulationOperation.ReplaceUsingPattern,
            configure: p =>
            {
                p.Find = "(a+)+b";
                p.ReplaceWith = "x";
            });

        Task run = strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);
        Task winner = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        Assert.Same(run, winner);
        await run;
        Assert.Null(block.BlockResponse);
    }

    private (Block block, BlockExecutionContext context, StringManipulationStrategy strategy) CreateTestSetup(
        string? input,
        StringManipulationOperation operation,
        Action<StringManipulationProperties>? configure = null)
    {
        var workflow = new Section("1234", new Workflow("1234"));
        var properties = new StringManipulationProperties
        {
            InputTemplate = input,
            Operation = operation,
        };
        configure?.Invoke(properties);

        var block = new Block(BlockTypes.StringManipulation, workflow)
        {
            Name = "String Manipulation",
            Properties = properties,
        };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new StringManipulationStrategy();
        return (block, context, strategy);
    }
}
