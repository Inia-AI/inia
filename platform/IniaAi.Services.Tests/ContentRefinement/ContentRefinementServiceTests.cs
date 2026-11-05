using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.AwsBedrock;
using IniaAi.AiConnection.Common;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Application.Commands.TextProcessing.RefineContent;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Configuration;
using IniaAi.Services.ContentRefinement;
using IniaAi.Common.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using IniaAi.Common.Interfaces.Services;

namespace IniaAi.Services.Tests.ContentRefinement;

public class ContentRefinementServiceTests
{
    private const string SAMPLE_CONTENT = "The company reported strong growth in 2025.";
    private const string SAMPLE_INSTRUCTION = "Make this more formal.";

    private static ResponseContentSequence CreateResponse(string text)
    {
        var response = new ResponseContentSequence();
        response.Add(new ResponseCommonContent(text));
        return response;
    }

    private static IChatConnector StubConnectorReturning(ResponseContentSequence response)
    {
        IChatConnector connector = Substitute.For<IChatConnector>();
        _ = connector
            .GetResponseAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<string?>(),
                Arg.Any<ModelFamily?>(),
                Arg.Any<ModelEffort?>(),
                Arg.Any<CancellationToken>())
            .Returns(response);
        return connector;
    }

    private static ContentRefinementService CreateService(
        IChatConnector chatConnector,
        ILogger<ContentRefinementService>? logger = null)
    {
        IOptions<PlatformSettings> settings = Options.Create(new PlatformSettings());
        var resolver = new TenantModelFamilyResolver(
            settings,
            NullLogger<TenantModelFamilyResolver>.Instance);

        return new ContentRefinementService(
            logger ?? NullLogger<ContentRefinementService>.Instance,
            chatConnector,
            resolver,
            new RequestModelUse());
    }

    [Fact]
    public async Task RefineAsync_WithOutOfScopeTag_ReturnsIsOutOfScopeTrue()
    {
        ContentRefinementService service = CreateService(
            StubConnectorReturning(CreateResponse("<out_of_scope>true</out_of_scope>")));

        ContentRefinementOutput? output = await service.RefineAsync(
            SAMPLE_CONTENT,
            SAMPLE_INSTRUCTION,
            conversationHistory: [],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(output);
        Assert.True(output.IsOutOfScope);
        Assert.Null(output.RefinedContent);
        Assert.Null(output.Explanation);
    }

    [Fact]
    public async Task RefineAsync_WithOutOfScopeAndRefinedContent_OutOfScopeWinsAndLogsWarning()
    {
        ILogger<ContentRefinementService> logger = Substitute.For<ILogger<ContentRefinementService>>();
        _ = logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        string mixedResponse =
            "<out_of_scope>true</out_of_scope><explanation>Should be ignored.</explanation><refined_content>This should be discarded.</refined_content>";

        ContentRefinementService service = CreateService(
            StubConnectorReturning(CreateResponse(mixedResponse)),
            logger);

        ContentRefinementOutput? output = await service.RefineAsync(
            SAMPLE_CONTENT,
            SAMPLE_INSTRUCTION,
            conversationHistory: [],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(output);
        Assert.True(output.IsOutOfScope);

        logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Theory]
    [InlineData("<out_of_scope>false</out_of_scope><explanation>Adjusted tone.</explanation><refined_content>The corporation reported robust growth.</refined_content>")]
    [InlineData("<out_of_scope></out_of_scope><explanation>Adjusted tone.</explanation><refined_content>The corporation reported robust growth.</refined_content>")]
    [InlineData("<out_of_scope>   </out_of_scope><explanation>Adjusted tone.</explanation><refined_content>The corporation reported robust growth.</refined_content>")]
    public async Task RefineAsync_WhenOutOfScopeValueIsNotTrue_DoesNotTriggerOutOfScope(string modelResponse)
    {
        ContentRefinementService service = CreateService(
            StubConnectorReturning(CreateResponse(modelResponse)));

        ContentRefinementOutput? output = await service.RefineAsync(
            SAMPLE_CONTENT,
            SAMPLE_INSTRUCTION,
            conversationHistory: [],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(output);
        Assert.False(output.IsOutOfScope);
        Assert.Equal("The corporation reported robust growth.", output.RefinedContent);
    }

    [Fact]
    public async Task RefineAsync_WhenExplanationMentionsTagLiteral_DoesNotTriggerOutOfScope()
    {
        string response =
            "<explanation>I considered emitting &lt;out_of_scope/&gt; but the request was clear.</explanation>"
            + "<refined_content>Refined text.</refined_content>";

        ContentRefinementService service = CreateService(
            StubConnectorReturning(CreateResponse(response)));

        ContentRefinementOutput? output = await service.RefineAsync(
            SAMPLE_CONTENT,
            SAMPLE_INSTRUCTION,
            conversationHistory: [],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(output);
        Assert.False(output.IsOutOfScope);
        Assert.Equal("Refined text.", output.RefinedContent);
    }

    [Fact]
    public async Task RefineAsync_WithRefinedContentOnly_ReturnsSuccess()
    {
        string response =
            "<explanation>Adjusted tone to be more formal.</explanation><refined_content>The corporation reported robust growth in 2025.</refined_content>";

        ContentRefinementService service = CreateService(
            StubConnectorReturning(CreateResponse(response)));

        ContentRefinementOutput? output = await service.RefineAsync(
            SAMPLE_CONTENT,
            SAMPLE_INSTRUCTION,
            conversationHistory: [],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(output);
        Assert.False(output.IsOutOfScope);
        Assert.Equal("The corporation reported robust growth in 2025.", output.RefinedContent);
        Assert.Equal("Adjusted tone to be more formal.", output.Explanation);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Sorry, I cannot help with that.")]
    [InlineData("<refined_content>   </refined_content>")]
    public async Task RefineAsync_WithUnrecognisedOrEmptyResponse_ReturnsNull(string modelResponse)
    {
        ContentRefinementService service = CreateService(
            StubConnectorReturning(CreateResponse(modelResponse)));

        ContentRefinementOutput? output = await service.RefineAsync(
            SAMPLE_CONTENT,
            SAMPLE_INSTRUCTION,
            conversationHistory: [],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(output);
    }

    [Fact]
    public async Task RefineAsync_WhenCancelled_PropagatesOperationCanceledException()
    {
        IChatConnector connector = Substitute.For<IChatConnector>();
        _ = connector
            .GetResponseAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<string?>(),
                Arg.Any<ModelFamily?>(),
                Arg.Any<ModelEffort?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<ResponseContentSequence>>(_ => throw new OperationCanceledException());

        ContentRefinementService service = CreateService(connector);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _ = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.RefineAsync(
                SAMPLE_CONTENT,
                SAMPLE_INSTRUCTION,
                conversationHistory: [],
                cancellationToken: cts.Token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RefineAsync_WithEmptyContent_ThrowsArgumentException(string? content)
    {
        ContentRefinementService service = CreateService(
            StubConnectorReturning(CreateResponse("ignored")));

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RefineAsync(
                content!,
                SAMPLE_INSTRUCTION,
                conversationHistory: [],
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("content", ex.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RefineAsync_WithEmptyInstruction_ThrowsArgumentException(string? instruction)
    {
        ContentRefinementService service = CreateService(
            StubConnectorReturning(CreateResponse("ignored")));

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RefineAsync(
                SAMPLE_CONTENT,
                instruction!,
                conversationHistory: [],
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("instruction", ex.ParamName);
    }

    // ─── BuildPrompt structural assertions ──────────────────────────────────
    // BuildPrompt is private but observable by capturing the prompt sent to
    // IChatConnector. These tests pin the structural invariants that the
    // system prompt instructions and the parsing logic rely on.

    private static (IChatConnector Connector, Func<string> GetCapturedPrompt) StubConnectorCapturingPrompt(
        ResponseContentSequence response)
    {
        IChatConnector connector = Substitute.For<IChatConnector>();
        string? captured = null;
        _ = connector
            .GetResponseAsync(
                Arg.Do<string>(p => captured = p),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<string?>(),
                Arg.Any<ModelFamily?>(),
                Arg.Any<ModelEffort?>(),
                Arg.Any<CancellationToken>())
            .Returns(response);
        return (connector, () => captured ?? throw new InvalidOperationException("Prompt was not captured"));
    }

    [Fact]
    public async Task BuildPrompt_WrapsCurrentTextAndRequest()
    {
        (IChatConnector connector, Func<string> getPrompt) =
            StubConnectorCapturingPrompt(CreateResponse("<refined_content>ok</refined_content>"));
        ContentRefinementService service = CreateService(connector);

        _ = await service.RefineAsync(
            "the original content",
            "make it formal.",
            conversationHistory: [],
            cancellationToken: TestContext.Current.CancellationToken);

        string prompt = getPrompt();
        Assert.Contains("<current_text>", prompt);
        Assert.Contains("the original content", prompt);
        Assert.Contains("</current_text>", prompt);
        Assert.Contains("<request>", prompt);
        Assert.Contains("make it formal.", prompt);
        Assert.Contains("</request>", prompt);
    }

    [Fact]
    public async Task BuildPrompt_WithEmptyHistory_EmitsFirstTurnMarker()
    {
        (IChatConnector connector, Func<string> getPrompt) =
            StubConnectorCapturingPrompt(CreateResponse("<refined_content>ok</refined_content>"));
        ContentRefinementService service = CreateService(connector);

        _ = await service.RefineAsync(
            SAMPLE_CONTENT,
            SAMPLE_INSTRUCTION,
            conversationHistory: [],
            cancellationToken: TestContext.Current.CancellationToken);

        string prompt = getPrompt();
        Assert.Contains("<history>", prompt);
        Assert.Contains("first refinement turn", prompt);
        Assert.DoesNotContain("<turn index=", prompt);
    }

    [Fact]
    public async Task BuildPrompt_WithAlternatingPair_EmitsTurnWithInstructionAndVersion()
    {
        (IChatConnector connector, Func<string> getPrompt) =
            StubConnectorCapturingPrompt(CreateResponse("<refined_content>ok</refined_content>"));
        ContentRefinementService service = CreateService(connector);

        var history = new List<RefinementTurnDto>
        {
            new("user", "shorten it"),
            new("assistant", "the shortened version"),
        };

        _ = await service.RefineAsync(
            SAMPLE_CONTENT,
            SAMPLE_INSTRUCTION,
            conversationHistory: history,
            cancellationToken: TestContext.Current.CancellationToken);

        string prompt = getPrompt();
        Assert.Contains("<turn index=\"1\">", prompt);
        Assert.Contains("<instruction>", prompt);
        Assert.Contains("shorten it", prompt);
        Assert.Contains("</instruction>", prompt);
        Assert.Contains("<version>", prompt);
        Assert.Contains("the shortened version", prompt);
        Assert.Contains("</version>", prompt);
        Assert.DoesNotContain("first refinement turn", prompt);
    }

    [Fact]
    public async Task BuildPrompt_WithMalformedPair_LogsWarningAndSkips()
    {
        (IChatConnector connector, Func<string> getPrompt) =
            StubConnectorCapturingPrompt(CreateResponse("<refined_content>ok</refined_content>"));
        ILogger<ContentRefinementService> logger = Substitute.For<ILogger<ContentRefinementService>>();
        ContentRefinementService service = CreateService(connector, logger);

        // assistant-then-user — wrong order; validator would normally reject this,
        // but the service must not silently drop it.
        var history = new List<RefinementTurnDto>
        {
            new("assistant", "first"),
            new("user", "second"),
        };

        _ = await service.RefineAsync(
            SAMPLE_CONTENT,
            SAMPLE_INSTRUCTION,
            conversationHistory: history,
            cancellationToken: TestContext.Current.CancellationToken);

        string prompt = getPrompt();
        Assert.DoesNotContain("<turn index=", prompt);
        logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(v => v.ToString()!.Contains("malformed conversation pair")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>()!);
    }
}
