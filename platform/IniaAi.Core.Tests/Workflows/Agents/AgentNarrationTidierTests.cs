using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;
using IniaAi.Core.Workflows.Agents;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace IniaAi.Core.Tests.Workflows.Agents;

public class AgentNarrationTidierTests
{
    private static readonly DateTimeOffset _origin = new(2026, 8, 13, 13, 39, 0, TimeSpan.Zero);

    private readonly IChatConnector _chatConnector = Substitute.For<IChatConnector>();
    private readonly AgentNarrationTidier _tidier;

    public AgentNarrationTidierTests()
    {
        _tidier = new AgentNarrationTidier(_chatConnector, NullLogger<AgentNarrationTidier>.Instance);
    }

    private static AgentNarrationLine Line(string key, int offsetSeconds, string text)
    {
        return new AgentNarrationLine(key, _origin.AddSeconds(offsetSeconds), text);
    }

    private static AgentNarrationLine ToolLine(string key, int offsetSeconds, string text)
    {
        return new AgentNarrationLine(key, _origin.AddSeconds(offsetSeconds), text)
        {
            Kind = AgentActivityLineKind.ToolAction,
        };
    }

    private List<string?> ArrangeBySystemPrompt(Func<string?, string> respond)
    {
        List<string?> systemPrompts = [];
        _ = _chatConnector
            .GetResponseAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<string?>(),
                Arg.Any<IniaAi.AiConnection.AwsBedrock.ModelFamily?>(),
                Arg.Any<IniaAi.Common.Enums.ModelEffort?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                string? system = call.ArgAt<string?>(1);
                lock (systemPrompts)
                {
                    systemPrompts.Add(system);
                }

                return Task.FromResult(
                    new ResponseContentSequence { new ResponseCommonContent(respond(system)) });
            });

        return systemPrompts;
    }

    private void ArrangeResponse(string responseText)
    {
        var response = new ResponseContentSequence { new ResponseCommonContent(responseText) };
        _ = _chatConnector
            .GetResponseAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<string?>(),
                Arg.Any<IniaAi.AiConnection.AwsBedrock.ModelFamily?>(),
                Arg.Any<IniaAi.Common.Enums.ModelEffort?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));
    }

    [Fact]
    public async Task Tidy_MapsNumberedResponseLinesBackByKeyAndHonoursDrop()
    {
        ArrangeResponse("1. Found the statement and read it in full\n2. DROP");

        IReadOnlyDictionary<string, string?> tidied = await _tidier.TidyAsync(
            [
                Line("a:0", 5, "Found the actual statement around line 5778. Let me view the full statement."),
                Line("b:0", 9, "Now the next file:"),
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal("Found the statement and read it in full", tidied["a:0"]);
        Assert.Null(tidied["b:0"]);
    }

    [Fact]
    public async Task Tidy_TheModelNeverSeesAnythingButTheAgentsOwnLines()
    {
        string? capturedInput = null;
        string? capturedSystem = null;
        var response = new ResponseContentSequence { new ResponseCommonContent("1. Tidied") };
        _ = _chatConnector
            .GetResponseAsync(
                Arg.Do<string>(input => capturedInput = input),
                Arg.Do<string?>(system => capturedSystem = system),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<string?>(),
                Arg.Any<IniaAi.AiConnection.AwsBedrock.ModelFamily?>(),
                Arg.Any<IniaAi.Common.Enums.ModelEffort?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        _ = await _tidier.TidyAsync(
            [
                Line("a:0", 0, "I'll begin by reading the manifest."),
                Line("b:0", 65, "Statement confirmed matches text extraction exactly."),
            ],
            TestContext.Current.CancellationToken);

        Assert.NotNull(capturedInput);
        Assert.Contains("Statement confirmed matches text extraction exactly.", capturedInput);
        Assert.Contains("[1m05s]", capturedInput);
        Assert.NotNull(capturedSystem);
        Assert.Contains("Never add anything the note does not say", capturedSystem);
    }

    [Fact]
    public async Task Tidy_UsesTheSimpleModelNeverTheAdvancedOne()
    {
        ArrangeResponse("1. Tidied");

        _ = await _tidier.TidyAsync(
            [Line("a:0", 0, "Some narration")],
            TestContext.Current.CancellationToken);

        _ = await _chatConnector.Received(1).GetResponseAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            shouldUseAdvancedModel: false,
            Arg.Any<string?>(),
            Arg.Any<IniaAi.AiConnection.AwsBedrock.ModelFamily?>(),
            Arg.Any<IniaAi.Common.Enums.ModelEffort?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tidy_CachesByRawTextSoALineIsPaidForOnce()
    {
        ArrangeResponse("1. Tidied once");

        IReadOnlyDictionary<string, string?> first = await _tidier.TidyAsync(
            [Line("a:0", 0, "The same narration line")],
            TestContext.Current.CancellationToken);
        IReadOnlyDictionary<string, string?> second = await _tidier.TidyAsync(
            [Line("later-key:0", 30, "The same narration line")],
            TestContext.Current.CancellationToken);

        Assert.Equal("Tidied once", first["a:0"]);
        Assert.Equal("Tidied once", second["later-key:0"]);
        _ = await _chatConnector.Received(1).GetResponseAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<bool>(),
            Arg.Any<string?>(),
            Arg.Any<IniaAi.AiConnection.AwsBedrock.ModelFamily?>(),
            Arg.Any<IniaAi.Common.Enums.ModelEffort?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tidy_AThrowingModelFailsOpenToAnEmptyMap()
    {
        _ = _chatConnector
            .GetResponseAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<string?>(),
                Arg.Any<IniaAi.AiConnection.AwsBedrock.ModelFamily?>(),
                Arg.Any<IniaAi.Common.Enums.ModelEffort?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<ResponseContentSequence>>(static _ => throw new InvalidOperationException("model down"));

        IReadOnlyDictionary<string, string?> tidied = await _tidier.TidyAsync(
            [Line("a:0", 0, "Some narration")],
            TestContext.Current.CancellationToken);

        Assert.Empty(tidied);
    }

    [Fact]
    public async Task Tidy_AnOverlongRewriteIsRejectedSoTheLineFallsBackRaw()
    {
        ArrangeResponse("1. " + new string('x', 400));

        IReadOnlyDictionary<string, string?> tidied = await _tidier.TidyAsync(
            [Line("a:0", 0, "Short note")],
            TestContext.Current.CancellationToken);

        Assert.False(tidied.ContainsKey("a:0"));
    }

    [Fact]
    public async Task Tidy_ARewritePastOneClause_IsTrimmedRatherThanRejected()
    {
        const string SPRAWLING =
            "Extracting and reconciling the three-year income statement, balance sheet and cash flows";
        ArrangeResponse("1. " + SPRAWLING);

        IReadOnlyDictionary<string, string?> tidied = await _tidier.TidyAsync(
            [ToolLine("t:0", 0, new string('r', 300))],
            TestContext.Current.CancellationToken);

        Assert.Equal("Extracting and reconciling the three-year income statement", tidied["t:0"]);
    }

    [Fact]
    public async Task Tidy_ARewritePastOneClauseWithNoComma_IsTrimmedToAWordBoundary()
    {
        ArrangeResponse("1. " + string.Join(' ', Enumerable.Repeat("reconciling", 12)));

        IReadOnlyDictionary<string, string?> tidied = await _tidier.TidyAsync(
            [ToolLine("t:0", 0, new string('r', 300))],
            TestContext.Current.CancellationToken);

        string trimmed = Assert.IsType<string>(tidied["t:0"]);
        Assert.True(trimmed.Length <= 81, $"{trimmed.Length} chars: {trimmed}");
        Assert.EndsWith("\u2026", trimmed, StringComparison.Ordinal);
        Assert.DoesNotContain("reconcil\u2026", trimmed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tidy_ADropIsNeverCachedSoOneUnluckyBatchCannotSuppressTheWordsForever()
    {
        ArrangeResponse("1. DROP");
        IReadOnlyDictionary<string, string?> first = await _tidier.TidyAsync(
            [ToolLine("t:0", 0, "Check the footings tie")],
            TestContext.Current.CancellationToken);

        ArrangeResponse("1. Checking the footings tie");
        IReadOnlyDictionary<string, string?> second = await _tidier.TidyAsync(
            [ToolLine("later:0", 30, "Check the footings tie")],
            TestContext.Current.CancellationToken);

        Assert.Null(first["t:0"]);
        Assert.Equal("Checking the footings tie", second["later:0"]);
    }

    [Fact]
    public async Task Tidy_ARefusalIsRejectedSoTheLineFallsBackRaw()
    {
        ArrangeResponse("1. I can't assist with that request.");

        IReadOnlyDictionary<string, string?> tidied = await _tidier.TidyAsync(
            [Line("a:0", 0, "Some narration")],
            TestContext.Current.CancellationToken);

        Assert.False(tidied.ContainsKey("a:0"));
    }

    [Fact]
    public async Task Tidy_ToolDescriptions_AreRewrittenByAPromptOfTheirOwnAndCanBeDropped()
    {
        List<string?> systemPrompts = ArrangeBySystemPrompt(
            static _ => "1. Finding the 2025 balance sheet\n2. DROP");

        IReadOnlyDictionary<string, string?> tidied = await _tidier.TidyAsync(
            [
                ToolLine("t1:1", 0, "Locate balance sheet statement in 2025 report"),
                ToolLine("t2:2", 6, "Show workspace root, docs directory, and manifest"),
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal("Finding the 2025 balance sheet", tidied["t1:1"]);
        Assert.True(tidied.ContainsKey("t2:2"));
        Assert.Null(tidied["t2:2"]);
        string? toolPrompt = Assert.Single(systemPrompts);
        Assert.NotNull(toolPrompt);
        Assert.Contains("credit analyst", toolPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DROP", toolPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tidy_MixedLines_CostOneCallPerKindNotOnePerLine()
    {
        List<string?> systemPrompts = ArrangeBySystemPrompt(static _ => "1. One\n2. Two\n3. Three");

        IReadOnlyDictionary<string, string?> tidied = await _tidier.TidyAsync(
            [
                Line("n1:0", 0, "Good, this confirms both years are present."),
                ToolLine("t1:1", 3, "Convert both annual report PDFs to text with layout preserved"),
                ToolLine("t2:2", 6, "Locate balance sheet statement in 2025 report"),
                Line("n2:0", 9, "Reconciliation ties exactly."),
                ToolLine("t3:3", 12, "Check out directory contents"),
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal(5, tidied.Count);
        Assert.Equal(2, systemPrompts.Count);
        Assert.Equal(2, systemPrompts.Distinct().Count());
    }

    [Fact]
    public async Task Tidy_OneKindFailing_NeverDiscardsTheOtherKindsAnswers()
    {
        _ = ArrangeBySystemPrompt(static system =>
            system!.Contains("credit analyst", StringComparison.OrdinalIgnoreCase)
                ? throw new InvalidOperationException("model down")
                : "1. Confirmed both years are present");

        IReadOnlyDictionary<string, string?> tidied = await _tidier.TidyAsync(
            [
                Line("n1:0", 0, "Good, this confirms both years are present."),
                ToolLine("t1:1", 3, "Locate balance sheet statement in 2025 report"),
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal("Confirmed both years are present", tidied["n1:0"]);
        Assert.False(tidied.ContainsKey("t1:1"));
    }

    [Fact]
    public async Task Tidy_TheSameWordsUnderEitherKind_AreNeverServedFromTheOthersCacheEntry()
    {
        _ = ArrangeBySystemPrompt(static system =>
            system!.Contains("credit analyst", StringComparison.OrdinalIgnoreCase)
                ? "1. Reading the notes index"
                : "1. Read the notes index");

        IReadOnlyDictionary<string, string?> asNarration = await _tidier.TidyAsync(
            [Line("n1:0", 0, "Read the notes index in the 2025 report")],
            TestContext.Current.CancellationToken);
        IReadOnlyDictionary<string, string?> asToolAction = await _tidier.TidyAsync(
            [ToolLine("t1:1", 3, "Read the notes index in the 2025 report")],
            TestContext.Current.CancellationToken);

        Assert.Equal("Read the notes index", asNarration["n1:0"]);
        Assert.Equal("Reading the notes index", asToolAction["t1:1"]);
    }

    [Fact]
    public async Task Tidy_AMalformedResponseYieldsNoEntryForTheGarbledLine()
    {
        ArrangeResponse("1. Good line\ncompletely unnumbered nonsense");

        IReadOnlyDictionary<string, string?> tidied = await _tidier.TidyAsync(
            [
                Line("a:0", 0, "First narration"),
                Line("b:0", 5, "Second narration"),
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal("Good line", tidied["a:0"]);
        Assert.False(tidied.ContainsKey("b:0"));
    }
}
