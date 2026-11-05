using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Anthropic.ApiModel;
using IniaAi.AiConnection.Anthropic.ApiModel.Enums;
using IniaAi.AiConnection.AwsBedrock;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using IniaAi.Common.Extensions;
using IniaAi.Configuration;
using IniaAi.Configuration.Enums.Anthropic;
using IniaAi.Testing;
using IniaAi.Testing.Mocks;
using IniaAi.Testing.XunitAttributes;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StopReasonType = IniaAi.AiConnection.Anthropic.ApiModel.Enums.StopReasonType;

namespace IniaAi.AiConnection.Tests.AwsBedrock;

[CollectionDefinition(nameof(NotThreadSafeResourceCollection), DisableParallelization = true)]
public class NotThreadSafeResourceCollection { }

[Collection(nameof(NotThreadSafeResourceCollection))]
public class AwsBedrockChatProviderTests : IClassFixture<TestsFixture<AwsBedrockChatProvider>>
{
    private const string CALLS_BEDROCK = "Calls Bedrock";

    private readonly TestsFixture<AwsBedrockChatProvider> _fixture;
    private readonly ITestOutputHelper _output;

    public PlatformSettings AnthropicPlatformSettings
    {
        get
        {
            PlatformSettings settings = _fixture.PlatformSettings.Clone() as PlatformSettings
                ?? throw new InvalidOperationException("Failed to clone PlatformSettings.");

            settings.Anthropic.BasicModel = AiModel.UsAwsHaiku4_5;
            settings.Anthropic.AdvancedModel = AiModel.UsAwsSonnet4_6;
            settings.Anthropic.BasicModelIdOverride = null;
            settings.Anthropic.AdvancedModelIdOverride = null;

            return settings;
        }
    }

    public AwsBedrockChatProviderTests(TestsFixture<AwsBedrockChatProvider> fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [PaidApiFact(CALLS_BEDROCK, alsoInCi: true)]
    public async Task GetResponseAsync_WithAnthropicModel_ReturnsResponseAsync()
    {
        // Arrange
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestsLoggerProvider(_output)));
        var provider = new AwsBedrockChatProvider(
            AnthropicPlatformSettings,
            loggerFactory.CreateLogger<AwsBedrockChatProvider>(),
            loggerFactory,
            new NoOpLimiter());

        string userInput = "What is the capital of France?";
        string anthropicModel = AiModel.UsAwsHaiku4_5.GetEnumMember();

        // Act
        ResponseContentSequence responseSequence = await provider.GetResponseAsync(userInput, model: anthropicModel, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(responseSequence);
        Assert.DoesNotContain(IChatConnector.ERROR_PREFIX, responseSequence.GetPlainText());
        Assert.Contains("Paris", responseSequence.GetPlainText(), StringComparison.OrdinalIgnoreCase);
        Assert.True(responseSequence.InputTokens > 0);
        Assert.True(responseSequence.OutputTokens > 0);

        _output.WriteLine($"Anthropic Response: {responseSequence.GetPlainText()}");
    }

    [PaidApiFact(CALLS_BEDROCK, alsoInCi: true)]
    public async Task GetResponseAsync_WithSystemPromptAndAnthropicModel_ReturnsResponseAsync()
    {
        // Arrange
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestsLoggerProvider(_output)));
        var provider = new AwsBedrockChatProvider(
            AnthropicPlatformSettings,
            loggerFactory.CreateLogger<AwsBedrockChatProvider>(),
            loggerFactory,
            new NoOpLimiter());

        string userInput = "What is the largest planet?";
        string systemPrompt = "You are a knowledgeable astronomy expert. Answer in exactly one word.";
        string anthropicModel = AiModel.UsAwsHaiku4_5.GetEnumMember();

        // Act
        ResponseContentSequence responseSequence = await provider.GetResponseAsync(userInput, systemPrompt, model: anthropicModel, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(responseSequence);
        Assert.DoesNotContain(IChatConnector.ERROR_PREFIX, responseSequence.GetPlainText());
        Assert.Contains("Jupiter", responseSequence.GetPlainText(), StringComparison.OrdinalIgnoreCase);

        _output.WriteLine($"Anthropic Response with System Prompt: {responseSequence.GetPlainText()}");
    }

    [PaidApiFact(CALLS_BEDROCK, alsoInCi: true)]
    public async Task GetResponseAsync_WithContentSequenceAndAnthropicModel_ReturnsResponseAsync()
    {
        // Arrange
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestsLoggerProvider(_output)));
        var provider = new AwsBedrockChatProvider(
            AnthropicPlatformSettings,
            loggerFactory.CreateLogger<AwsBedrockChatProvider>(),
            loggerFactory,
            new NoOpLimiter());

        var image = new BinaryData(File.ReadAllBytes("TestResources/elephant.jpg"));
        var contentSequence = new RequestContentSequence()
        {
            "What is in the image below?",
            new BinaryContent(image, MediaType.ImageJpeg)
        };
        string systemPrompt = "You are an assistant who answers questions about images in ONE word.";
        string anthropicModel = AiModel.UsAwsHaiku4_5.GetEnumMember();

        // Act
        ResponseContentSequence responseSequence = await provider.GetResponseAsync(contentSequence, systemPrompt, model: anthropicModel, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(responseSequence);
        Assert.DoesNotContain(IChatConnector.ERROR_PREFIX, responseSequence.GetPlainText());
        Assert.Contains("elephant", responseSequence.GetPlainText(), StringComparison.OrdinalIgnoreCase);
        Assert.True(responseSequence.InputTokens > 0);
        Assert.True(responseSequence.OutputTokens > 0);

        _output.WriteLine($"Anthropic Image Response: {responseSequence.GetPlainText()}");
    }

    [PaidApiFact(CALLS_BEDROCK, alsoInCi: true)]
    public async Task GetResponseAsync_DefaultAnthropicModel_UsesConfiguredAdvancedModel()
    {
        // Arrange
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestsLoggerProvider(_output)));
        var provider = new AwsBedrockChatProvider(
            AnthropicPlatformSettings,
            loggerFactory.CreateLogger<AwsBedrockChatProvider>(),
            loggerFactory,
            new NoOpLimiter());

        string userInput = "What is the capital of Italy?";

        // Act - Don't specify model, should use default Anthropic advanced model
        ResponseContentSequence responseSequence = await provider.GetResponseAsync(userInput, shouldUseAdvancedModel: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(responseSequence);
        Assert.DoesNotContain(IChatConnector.ERROR_PREFIX, responseSequence.GetPlainText());
        Assert.Contains("Rome", responseSequence.GetPlainText(), StringComparison.OrdinalIgnoreCase);

        _output.WriteLine($"Default Anthropic Advanced Model Response: {responseSequence.GetPlainText()}");
    }

    [Fact]
    public async Task GetResponseAsync_WithInvalidModel_ThrowsException()
    {
        // Arrange
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestsLoggerProvider(_output)));
        var provider = new AwsBedrockChatProvider(
            AnthropicPlatformSettings,
            loggerFactory.CreateLogger<AwsBedrockChatProvider>(),
            loggerFactory,
            new NoOpLimiter());

        string userInput = "Test question";
        string invalidModel = "invalid.model.id";

        // Act & Assert
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await provider.GetResponseAsync(userInput, model: invalidModel, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains(invalidModel, exception.Message);
    }

    [Theory]
    [InlineData("us.anthropic.claude-sonnet-5-5", true, "eu.anthropic.claude-sonnet-5-5")]
    [InlineData("global.anthropic.claude-opus-5-5", true, "eu.anthropic.claude-opus-5-5")]
    [InlineData("us.anthropic.claude-opus-4-8", false, "eu.anthropic.claude-opus-4-8")]
    [InlineData("claude-haiku-5-5", false, "eu.anthropic.claude-haiku-5-5")]
    public void GetModelId_AStoredIdOfAnotherRegion_CallsTheEuProfile_OnAnEuStack(
        string storedModel, bool shouldUseAdvancedModel, string expected)
    {
        AwsBedrockChatProvider provider = CreateProvider(AiModel.EuAwsSonnet5, AiModel.EuAwsHaiku5_5);

        Assert.Equal(expected, provider.GetModelId(shouldUseAdvancedModel, storedModel));
    }

    [Fact]
    public void GetModelId_AStoredUsId_IsUnchanged_OnAUsStack()
    {
        AwsBedrockChatProvider provider = CreateProvider(AiModel.UsAwsSonnet5, AiModel.UsAwsHaiku5_5);
        string storedModel = AiModel.UsAwsSonnet5_5.GetEnumMember();

        Assert.Equal(storedModel, provider.GetModelId(shouldUseAdvancedModel: true, storedModel));
    }

    [Fact]
    public async Task GetResponseAsync_AModelTheStacksRegionDoesNotOffer_FailsBeforeAnyRequest()
    {
        AwsBedrockChatProvider provider = CreateProvider(AiModel.GlobalAwsSonnet5, AiModel.GlobalAwsHaiku5_5);
        string storedModel = AiModel.UsAwsHaiku4_5.GetEnumMember();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetResponseAsync("Hello", model: storedModel, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(storedModel, exception.Message);
        Assert.Contains("not offered in this deployment's region", exception.Message);
    }

    private AwsBedrockChatProvider CreateProvider(AiModel advancedModel, AiModel basicModel)
    {
        PlatformSettings settings = AnthropicPlatformSettings;
        settings.Anthropic.AdvancedModel = advancedModel;
        settings.Anthropic.BasicModel = basicModel;
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestsLoggerProvider(_output)));

        return new AwsBedrockChatProvider(
            settings,
            loggerFactory.CreateLogger<AwsBedrockChatProvider>(),
            loggerFactory,
            new NoOpLimiter());
    }

    [PaidApiFact(CALLS_BEDROCK, alsoInCi: true)]
    public async Task GetThinkingResponseAsync_WithAnthropicModel_ReturnsResponseAsync()
    {
        // Arrange
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestsLoggerProvider(_output)));
        var provider = new AwsBedrockChatProvider(
            AnthropicPlatformSettings,
            loggerFactory.CreateLogger<AwsBedrockChatProvider>(),
            loggerFactory,
            new NoOpLimiter());

        var contentSequence = new RequestContentSequence()
        {
            "Contemplate your existence, then respond with 'YES'"
        };
        string systemPrompt = "You are a philosopher who likes to follow instructions. Answer in ONE word";
        string anthropicModel = AiModel.UsAwsHaiku4_5.GetEnumMember();
        int maxTokens = 2000;
        int thinkingBudget = 1024;

        // Act
        ResponseContentSequence responseSequence = await provider.GetThinkingResponseAsync(
            contentSequence,
            systemPrompt: systemPrompt,
            maxTokens: maxTokens,
            thinkingBudget: thinkingBudget,
            shouldUseAdvancedModel: true,
            model: anthropicModel,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(responseSequence);
        Assert.Contains("YES", responseSequence.GetPlainText(), StringComparison.OrdinalIgnoreCase);
        Assert.True(responseSequence.InputTokens > 0);
        Assert.True(responseSequence.OutputTokens > 0);
        // Verify thinking mode was actually used by checking for thinking content in response
        Assert.NotNull(provider.LastRawResponseJson);
        Assert.Contains("thinking", provider.LastRawResponseJson, StringComparison.OrdinalIgnoreCase);

        // Parse and verify thinking block structure
        ResponseBody? responseObj = JsonConvert.DeserializeObject<ResponseBody>(provider.LastRawResponseJson);
        Assert.NotNull(responseObj);

        // Verify at least one thinking block exists
        var thinkingBlocks = responseObj.Content.Where(c => c.Type is ContentType.Thinking).ToList();
        Assert.NotEmpty(thinkingBlocks);

        // Verify thinking content is not empty
        Assert.All(thinkingBlocks, block => Assert.False(string.IsNullOrWhiteSpace(block.Thinking)));

        // Verify there's also a text response block
        var textBlocks = responseObj.Content.Where(c => c.Type is ContentType.Text).ToList();
        Assert.NotEmpty(textBlocks);

        // Verify stop reason is valid
        _ = Assert.NotNull(responseObj.StopReason);
        Assert.Contains(responseObj.StopReason.Value, new[] { StopReasonType.EndTurn, StopReasonType.MaxTokens, StopReasonType.StopSequence });

        // Verify token counts are reasonable
        Assert.True(responseSequence.InputTokens > 0, "Input tokens should be greater than 0");
        Assert.True(responseSequence.OutputTokens > 0, "Output tokens should be greater than 0");

        _output.WriteLine($"Thinking Response: {responseSequence.GetPlainText()}");
        _output.WriteLine($"Thinking blocks: {thinkingBlocks.Count}, Text blocks: {textBlocks.Count}");
        _output.WriteLine($"Input tokens: {responseSequence.InputTokens}, Output tokens: {responseSequence.OutputTokens}");
    }

    [PaidApiFact("Live Bedrock call whose first leg depends on how long the model thinks")]
    public async Task GetThinkingResponseAsync_AdaptiveModelThinksThroughMaxTokens_SendsAgainAtLowEffortAsync()
    {
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestsLoggerProvider(_output)));
        var provider = new AwsBedrockChatProvider(
            AnthropicPlatformSettings,
            loggerFactory.CreateLogger<AwsBedrockChatProvider>(),
            loggerFactory,
            new NoOpLimiter());

        var contentSequence = new RequestContentSequence()
        {
            "How many primes below 5000 have a digit sum of 23? Check every candidate exhaustively, then reply with the number only."
        };
        const int maxTokens = 1024;

        ResponseContentSequence responseSequence = await provider.GetThinkingResponseAsync(
            contentSequence,
            maxTokens: maxTokens,
            model: AiModel.UsAwsSonnet5.GetEnumMember(),
            effort: ModelEffort.Max,
            cancellationToken: TestContext.Current.CancellationToken);

        _output.WriteLine($"Output tokens: {responseSequence.OutputTokens}, thinking only: {responseSequence.IsThinkingOnly}");
        _output.WriteLine($"Response: {responseSequence.GetPlainText()}");
        Assert.True(
            responseSequence.OutputTokens > maxTokens,
            $"One request cannot emit more than {maxTokens} output tokens, so {responseSequence.OutputTokens} means the request was not sent again");
    }

    [Fact]
    public async Task GetThinkingResponseAsync_InvalidThinkingBudget_ThrowsExceptionAsync()
    {
        // Arrange
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestsLoggerProvider(_output)));
        var provider = new AwsBedrockChatProvider(
            AnthropicPlatformSettings,
            loggerFactory.CreateLogger<AwsBedrockChatProvider>(),
            loggerFactory,
            new NoOpLimiter());

        var contentSequence = new RequestContentSequence()
        {
            "Test message"
        };
        int invalidBudget = 500; // Less than minimum 1024
        string anthropicModel = AiModel.UsAwsHaiku4_5.GetEnumMember();

        // Act & Assert
        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await provider.GetThinkingResponseAsync(
                contentSequence,
                thinkingBudget: invalidBudget,
                model: anthropicModel,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("budget must be at least 1024", exception.Message);
        _output.WriteLine($"Exception Message: {exception.Message}");
    }

    [Fact]
    public async Task GetThinkingResponseAsync_BudgetExceedsMaxTokens_ThrowsExceptionAsync()
    {
        // Arrange
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestsLoggerProvider(_output)));
        var provider = new AwsBedrockChatProvider(
            AnthropicPlatformSettings,
            loggerFactory.CreateLogger<AwsBedrockChatProvider>(),
            loggerFactory,
            new NoOpLimiter());

        var contentSequence = new RequestContentSequence()
        {
            "Test message"
        };
        int maxTokens = 2000;
        int exceedingBudget = 3000;
        string anthropicModel = AiModel.UsAwsHaiku4_5.GetEnumMember();

        // Act & Assert
        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await provider.GetThinkingResponseAsync(
                contentSequence,
                maxTokens: maxTokens,
                thinkingBudget: exceedingBudget,
                model: anthropicModel,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("budget cannot be greater than", exception.Message);
        _output.WriteLine($"Exception Message: {exception.Message}");
    }

    [PaidApiFact(CALLS_BEDROCK, alsoInCi: true)]
    public async Task GetResponseWithCachingAsync_ValidContentSequences_ReturnsResponseAsync()
    {
        // Arrange
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestsLoggerProvider(_output)));
        var provider = new AwsBedrockChatProvider(
            AnthropicPlatformSettings,
            loggerFactory.CreateLogger<AwsBedrockChatProvider>(),
            loggerFactory,
            new NoOpLimiter());

        var userContent = new RequestContentSequence()
        {
            """
            Answer the questions below.

            1. What is the story about? Answer in ONE word. Options are:
                a. Lion
                b. Family
                c. Space
            2. What is on the image? Answer in ONE word. Options are:
                a. Elephant
                b. Car
                c. Flower
            """,
        };

        var cachedContent = new RequestContentSequence()
        {
            SHORT_STORY,
            new BinaryContent(File.ReadAllBytes("TestResources/elephant.jpg"), MediaType.ImageJpeg)
        };

        string systemPrompt = "You are an AI assistant who answers questions about the provided content in ONE word per question";
        string anthropicModel = AiModel.UsAwsHaiku4_5.GetEnumMember();

        // Act
        ResponseContentSequence responseSequence1 = await provider.GetResponseWithCachingAsync(
            userContent,
            cachedContent,
            systemPrompt: systemPrompt,
            maxTokens: 20,
            model: anthropicModel,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(responseSequence1);
        Assert.Contains("family", responseSequence1.GetPlainText(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("elephant", responseSequence1.GetPlainText(), StringComparison.OrdinalIgnoreCase);
        Assert.True(responseSequence1.InputTokens > 0);
        Assert.True(responseSequence1.OutputTokens > 0);
        // Note: AwsBedrockChatProvider doesn't expose cache token metrics directly
        // Cache functionality is handled internally by the adapters

        // Test cache hit by making the same request again
        // Waits on purpose: gives the provider's prompt cache time to store the first request.
        Thread.Sleep(2000);

        // Act
        ResponseContentSequence responseSequence2 = await provider.GetResponseWithCachingAsync(
            userContent,
            cachedContent,
            systemPrompt: systemPrompt,
            maxTokens: 20,
            model: anthropicModel,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(responseSequence2);
        Assert.Contains("family", responseSequence2.GetPlainText(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("elephant", responseSequence2.GetPlainText(), StringComparison.OrdinalIgnoreCase);

        _output.WriteLine($"Cached Content Response 1: {responseSequence1.GetPlainText()}");
        _output.WriteLine($"Cached Content Response 2: {responseSequence2.GetPlainText()}");
    }

    [PaidApiFact(CALLS_BEDROCK, alsoInCi: true)]
    public async Task GetThinkingResponseWithCachingAsync_WhenCalled_ReturnsResponseAsync()
    {
        // Arrange
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestsLoggerProvider(_output)));
        var provider = new AwsBedrockChatProvider(
            AnthropicPlatformSettings,
            loggerFactory.CreateLogger<AwsBedrockChatProvider>(),
            loggerFactory,
            new NoOpLimiter());

        var userContent = new RequestContentSequence()
        {
            """
            Think deeply about these questions:
            1. What is the story about? Answer in ONE word.
            2. What really happened between the parents? Think about it carefully.
            """
        };

        var cachedContent = new RequestContentSequence()
        {
            SHORT_STORY
        };

        string systemPrompt = "You are an AI assistant tasked with analyzing stories. Think deeply before answering.";
        string anthropicModel = AiModel.UsAwsHaiku4_5.GetEnumMember();
        int maxTokens = 2000;
        int thinkingBudget = 1024;

        // Act
        ResponseContentSequence responseSequence = await provider.GetThinkingResponseWithCachingAsync(
            userContent,
            cachedContent,
            systemPrompt: systemPrompt,
            maxTokens: maxTokens,
            thinkingBudget: thinkingBudget,
            model: anthropicModel,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(responseSequence);
        Assert.True(responseSequence.InputTokens > 0);
        Assert.True(responseSequence.OutputTokens > 0);
        // Note: Cache functionality is handled internally by the adapters

        // Verify thinking mode was actually used by checking for thinking content in response
        Assert.NotNull(provider.LastRawResponseJson);
        Assert.Contains("thinking", provider.LastRawResponseJson, StringComparison.OrdinalIgnoreCase);

        // Parse and verify thinking block structure
        ResponseBody? responseObj = JsonConvert.DeserializeObject<ResponseBody>(provider.LastRawResponseJson);
        Assert.NotNull(responseObj);

        // Verify at least one thinking block exists
        var thinkingBlocks = responseObj.Content.Where(c => c.Type is ContentType.Thinking).ToList();
        Assert.NotEmpty(thinkingBlocks);

        // Verify thinking content is not empty
        Assert.All(thinkingBlocks, block => Assert.False(string.IsNullOrWhiteSpace(block.Thinking)));

        // Verify there's also a text response block
        var textBlocks = responseObj.Content.Where(c => c.Type is ContentType.Text).ToList();
        Assert.NotEmpty(textBlocks);

        // Verify stop reason is valid
        _ = Assert.NotNull(responseObj.StopReason);
        Assert.Contains(responseObj.StopReason.Value, new[] { StopReasonType.EndTurn, StopReasonType.MaxTokens, StopReasonType.StopSequence });

        // Verify token counts are reasonable
        Assert.True(responseSequence.InputTokens > 0, "Input tokens should be greater than 0");
        Assert.True(responseSequence.OutputTokens > 0, "Output tokens should be greater than 0");

        _output.WriteLine($"Thinking with Caching Response: {responseSequence.GetPlainText()}");
        _output.WriteLine($"Thinking blocks: {thinkingBlocks.Count}, Text blocks: {textBlocks.Count}");
        _output.WriteLine($"Input tokens: {responseSequence.InputTokens}, Output tokens: {responseSequence.OutputTokens}");
    }

    [PaidApiFact(CALLS_BEDROCK, alsoInCi: true)]
    public async Task GetResponseAsync_EmptyMessage_ReturnsErrorMessageAsync()
    {
        // Arrange
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestsLoggerProvider(_output)));
        var provider = new AwsBedrockChatProvider(
            AnthropicPlatformSettings,
            loggerFactory.CreateLogger<AwsBedrockChatProvider>(),
            loggerFactory,
            new NoOpLimiter());

        var emptySequence = new RequestContentSequence();
        string anthropicModel = AiModel.UsAwsHaiku4_5.GetEnumMember();

        // Act & Assert
        ResponseContentSequence responseSequence = await provider.GetResponseAsync(emptySequence, model: anthropicModel, cancellationToken: TestContext.Current.CancellationToken);

        string? responseText = responseSequence.GetPlainText();
        Assert.NotNull(responseText);
        Assert.Contains(IChatConnector.ERROR_PREFIX, responseText);
        // AWS Bedrock may return different error format than direct Anthropic API
        Assert.Contains("InvalidRequest", responseText);
        // Check for either the specific error or general malformed input message
        Assert.True(responseText.Contains("messages.0") || responseText.Contains("Malformed input"),
            $"Expected error about messages or malformed input, but got: {responseText}");

        _output.WriteLine($"Empty Message Error: {responseText}");
    }

    private const string SHORT_STORY =
    """
    Reunion

    The ferry was late, as it had always been, and my father was waiting on the quay as if the thirty years had not happened. He wore the same grey coat, or one so like it that I could not tell the difference, and he lifted his hand once when he saw me at the rail. He did not wave. He had never waved.

    "You found the right boat, then," he said when I stepped onto the stones.

    "There is only one boat, Dad."

    "There used to be two." He took my bag before I could stop him and turned towards the lane that climbed from the harbour to the house. "Your mother used to say that the second one was for leaving."

    I had not heard him mention her in a decade. I followed him up the lane, past the chapel with its green door, past the shop that now sold coffee to walkers, past the bench where I had carved my initials and been made to sand them out again. The island was smaller than I remembered, the way places are when you return to them taller and tired.

    The house stood where it always had, at the top of the hill with its back to the wind. Someone had painted the window frames blue. Inside, the kitchen smelled of peat smoke and the same lemon soap, and the table was set for two, with the good plates.

    "Sit," he said. "The kettle won't be long."

    I sat. On the dresser, where the wedding photograph had once stood, there was a clock that had stopped at ten past four. I asked him about it.

    "It stopped the week she went," he said. "I never saw the point of winding it."

    "You told me she went to Glasgow."

    "I did tell you that." He poured the water over the leaves and set the pot down between us. "Drink your tea."

    We talked about the weather, which was the island's way of talking about everything else. We talked about the new pier and the old minister and the price of diesel. He asked about my work, and I told him I built bridges, which was nearly true. He nodded as if bridges were a reasonable thing for a man to build. When the tea was finished he stood, rinsed both cups, and said he would show me the room.

    My room had not changed. The narrow bed, the shelf of paperbacks with their spines bleached by the window, the map of the world with a pin still pushed into Buenos Aires, where I had once decided I would live. I sat on the bed and listened to him moving about downstairs, and I tried to remember the last evening my mother had been in the house.

    I remembered shouting. I remembered that I had been sent to bed early and that I had lain awake listening to the sea and, under the sea, my parents' voices. I remembered a door, and then the ferry horn the next morning, and my father at the breakfast table saying she had gone to stay with her sister and would write. She did write, once. A postcard of the Clyde with four words on it: Be good. Love, Mum.

    That was what I had carried for thirty years. Four words and a ferry horn.

    I had built a life on top of those four words. I had chosen a university on the far side of the country because it was far from the island. I had married a woman who never asked about my mother, and when the marriage ended I told myself that some people simply leave, that leaving was a thing that ran in families like red hair or bad teeth. I had stopped answering my father's letters at twenty and started again at forty, when my own son was nine and I found that I could not look at him without wondering what he would carry.

    The invitation had come in March, on a card with a lighthouse on it. Seventy years, it said in my father's square hand, and then, underneath, in a different pen, as if he had come back to it after a long time: It would mean a great deal. I had read it on the train to work and then again on the train home, and on the third reading I had booked the ferry.

    In the morning my father was already out when I came down. There was bread on the board and a note in his square hand: Gone to the boat. Back at noon. I ate standing at the window and watched the gulls work the wind above the harbour. Then, because I was a grown man in an empty house and had once been a boy who was not allowed to touch anything, I began to open drawers.

    I am not proud of it. But there was a tin at the back of the dresser that I had never been allowed to open, a biscuit tin with a picture of a Highland cow on the lid, and when I lifted it down it was heavy with paper.

    They were letters. Dozens of them, in my mother's looping hand, addressed to me. The earliest was postmarked six weeks after she left; the latest was dated the spring I turned eighteen. None of them had been opened.

    I sat at the kitchen table and opened the first one with a butter knife.

    My darling boy, it began. Your father says it is better that you do not hear from me for a while, until things are settled. I do not agree, but I have promised. I want you to know that I did not leave because of you, and I did not leave because I stopped loving your father. I left because there was no money left to stay. The house is in my name, and the men from the bank will not take it from a woman with a child in it, so your father and I agreed that I would go to my sister and work, and send what I could, and you would keep your home. It is only for a little while.

    I read it twice. Then I opened the next one, and the next. They told a story that no one had ever told me. My father had borrowed against the boat to cover a bad season, and then against the house to cover the boat. When the debts came due, my mother had taken a job in a hotel laundry in the city and sent home almost every penny she earned. She asked about my school reports. She asked whether I still hated porridge. She asked, in letter after letter, whether she could come home for Christmas, and in the next letter she would say she understood why it was better not to.

    By the letters from the third year, the tone had changed. Your father has told me you are settled and happy, and that seeing me would only unsettle you. Perhaps he is right. I send you this anyway, so that one day, if you want to, you will know that I wrote.

    He was right about nothing. I was not settled. I was not happy. I was a boy who had been told his mother had chosen a city over him.

    When I heard the gate I did not put the letters away. My father came in with a box of fish under his arm and stopped in the doorway when he saw the tin open on the table.

    For a long time neither of us said anything. The clock on the dresser said ten past four, as it had for thirty years.

    "You kept them from me," I said at last.

    "I kept them for you." He set the fish down in the sink. "There is a difference, though I grant you it is a small one."

    "She sent money. She paid for this house. You told me she ran off."

    "I told you she went to Glasgow. That was true." He sat down across from me, slowly, a man of seventy with a fisherman's hands. "What I did not tell you was that I had lost everything we had, and that she was the one who saved it. I could not say that to a boy of nine. I could barely say it to myself."

    "And when I was eighteen? Twenty? Thirty?"

    "By then the lie had become the house we lived in," he said. "You do not take down the walls of the house you live in. Not when it is the only shelter you have."

    "Is she alive?"

    He looked at the stopped clock. "She is. She lives in a flat in Partick with a view of the river. She is not well. I telephone her on Sundays." He paused. "She asked me, last Sunday, whether you would come."

    "Come where?"

    "Here. For my birthday. It was her idea, the letter I sent you. I would not have had the courage." He pushed the tin towards me. "She is on the afternoon boat. I was going to tell you at noon. I was going to tell you everything at noon."

    I looked at my watch. It was five minutes past twelve.

    We walked down to the harbour together in the early afternoon, the two of us, along the lane past the chapel and the shop and the bench where my initials had been sanded away. The ferry was late, as it had always been. When it came round the point my father took my arm, and I let him.

    She was smaller than I remembered, and she stood at the rail with a scarf over her hair, and when she saw us on the quay she lifted her hand and waved, and waved, and did not stop waving until the boat had touched the stones.

    That evening my father wound the clock.
    """;
}
