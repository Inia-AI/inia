using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Anthropic;
using IniaAi.AiConnection.Anthropic.ApiModel;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using IniaAi.Configuration;
using IniaAi.Configuration.LLMProviders;
using IniaAi.Testing;
using IniaAi.Testing.Mocks;
using IniaAi.Testing.XunitAttributes;

namespace IniaAi.AiConnection.Tests.Anthropic;

public class AnthropicChatConnectorTests : IClassFixture<TestsFixture<AnthropicChatConnector>>
{
    private const string CALLS_THE_ANTHROPIC_API = "Calls the Anthropic API";

    private readonly TestsFixture<AnthropicChatConnector> _fixture;
    private readonly ITestOutputHelper _output;

    public AnthropicChatConnectorTests(TestsFixture<AnthropicChatConnector> fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [PaidApiFact(CALLS_THE_ANTHROPIC_API, alsoInCi: true)]
    public async Task GetResponseAsync_WhenCalled_ReturnsResponseAsync()
    {
        // Arrange
        var connector = new AnthropicChatConnector(
            _fixture.PlatformSettings,
            TestsLogger.CreateLogger<AnthropicChatConnector>(_output), new NoOpLimiter());
        var image = new BinaryData(File.ReadAllBytes("TestResources/elephant.jpg"));
        var contentSequence = new RequestContentSequence()
        {
            "What is in the image below?",
            new BinaryContent(image, MediaType.ImageJpeg)
        };
        Message message = new(contentSequence);
        string systemPrompt = "You are an assistant who answers to question about images in ONE word";
        RequestBody request = new(message, maxTokens: 2, systemPrompt: systemPrompt);

        // Act
        ResponseContentSequence responseSequence = await connector.GetResponseAsync(request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(responseSequence);
        Assert.Contains("elephant", responseSequence.GetPlainText(), StringComparison.OrdinalIgnoreCase);
        Assert.True(responseSequence.InputTokens > 0);
        Assert.True(responseSequence.OutputTokens > 0);
    }

    [PaidApiFact(CALLS_THE_ANTHROPIC_API, alsoInCi: true)]
    public async Task GetResponseAsync_WithSpecifiedModel_ReturnsResponseAsync()
    {
        // Arrange
        var connector = new AnthropicChatConnector(
            _fixture.PlatformSettings,
            TestsLogger.CreateLogger<AnthropicChatConnector>(_output), new NoOpLimiter());
        var contentSequence = new RequestContentSequence()
        {
            "Respond with 'YES'"
        };
        string systemPrompt = "You are an assistant who answers to question about images in ONE word";
        string model = "claude-haiku-4-5-20251001";

        // Act
        ResponseContentSequence responseSequence = await connector.GetResponseAsync(
            contentSequence,
            systemPrompt: systemPrompt,
            maxTokens: 2,
            shouldUseAdvancedModel: false,
            model: model,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(responseSequence);
        Assert.Contains("YES", responseSequence.GetPlainText(), StringComparison.OrdinalIgnoreCase);
        Assert.True(responseSequence.InputTokens > 0);
        Assert.True(responseSequence.OutputTokens > 0);
    }

    // Skipped 2026-04-24: Anthropic regression in prompt caching (confirmed with raw curl).
    // Content after the explicit cache_control breakpoint (~72 tokens of userContent) is being
    // write-cached as ephemeral_5m on every identical repeat but never served as cache_read,
    // so strict equality of CacheCreationInputTokens across back-to-back identical calls is broken.
    // Contradicts https://docs.claude.com/en/docs/build-with-claude/prompt-caching (tokens after
    // last breakpoint are documented as not eligible for cache). Repro request-id:
    // req_011CaNUis98sMnwCMNvndTFb. Un-skip once Anthropic fixes the regression.
    [Fact(Skip = "Anthropic prompt-caching regression — see comment above the attribute")]
    public async Task GetResponseWithCachingAsync_ValidContentSequences_ReturnsResponseAsync()
    {
        // Arrange
        var connector = new AnthropicChatConnector(
            _fixture.PlatformSettings,
            TestsLogger.CreateLogger<AnthropicChatConnector>(_output), new NoOpLimiter());

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

        // Act
        ResponseContentSequence responseSequence1 = await connector.GetResponseWithCachingAsync(
            userContent,
            cachedContent,
            systemPrompt: systemPrompt,
            maxTokens: 20,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(responseSequence1);
        Assert.Contains("family", responseSequence1.GetPlainText(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("elephant", responseSequence1.GetPlainText(), StringComparison.OrdinalIgnoreCase);
        Assert.True(responseSequence1.InputTokens > 0);
        Assert.True(responseSequence1.OutputTokens > 0);
        Assert.True(connector.InputTokens > 0);
        Assert.True(connector.OutputTokens > 0);
        Assert.True(connector.CacheCreationInputTokens > 0 || connector.CacheReadInputTokens > 0);

        // Arrange
        int cacheReadInputTokens = connector.CacheReadInputTokens;
        int cacheCreationInputTokens = connector.CacheCreationInputTokens;

        // Act
        ResponseContentSequence responseSequence2 = await connector.GetResponseWithCachingAsync(
            userContent,
            cachedContent,
            systemPrompt: systemPrompt,
            maxTokens: 20,
            cancellationToken: TestContext.Current.CancellationToken);

        // Anthropic prompt cache is best-effort (routing/eviction); retry once if the second call missed it.
        if (connector.CacheReadInputTokens == cacheReadInputTokens)
        {
            cacheCreationInputTokens = connector.CacheCreationInputTokens;

            responseSequence2 = await connector.GetResponseWithCachingAsync(
                userContent,
                cachedContent,
                systemPrompt: systemPrompt,
                maxTokens: 20,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        // Assert
        Assert.NotEmpty(responseSequence2);
        Assert.Contains("family", responseSequence2.GetPlainText(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("elephant", responseSequence2.GetPlainText(), StringComparison.OrdinalIgnoreCase);
        Assert.True(responseSequence2.InputTokens > 0);
        Assert.True(responseSequence2.OutputTokens > 0);
        Assert.True(connector.CacheReadInputTokens > cacheReadInputTokens);
        Assert.True(connector.CacheCreationInputTokens == cacheCreationInputTokens);
    }

    [PaidApiFact(CALLS_THE_ANTHROPIC_API, alsoInCi: true)]
    public async Task GetThinkingResponseAsync_WhenCalled_ReturnsResponseAsync()
    {
        // Arrange
        var connector = new AnthropicChatConnector(
            _fixture.PlatformSettings,
            TestsLogger.CreateLogger<AnthropicChatConnector>(_output), new NoOpLimiter());
        var contentSequence = new RequestContentSequence()
        {
            "Contemplate your existence, then respond with 'YES'"
        };
        string systemPrompt = "You are a philosopher who likes to follow instructions. Answer in ONE word";
        int maxTokens = 2000;
        int thinkingBudget = 1024;

        // Act
        ResponseContentSequence responseSequence = await connector.GetThinkingResponseAsync(
            contentSequence,
            systemPrompt: systemPrompt,
            maxTokens: maxTokens,
            thinkingBudget: thinkingBudget,
            shouldUseAdvancedModel: true,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(responseSequence);
        Assert.Contains("YES", responseSequence.GetPlainText(), StringComparison.OrdinalIgnoreCase);
        Assert.True(responseSequence.InputTokens > 0);
        Assert.True(responseSequence.OutputTokens > 0);
    }

    [PaidApiFact(CALLS_THE_ANTHROPIC_API, alsoInCi: true)]
    public async Task GetThinkingResponseWithCachingAsync_WhenCalled_ReturnsResponseAsync()
    {
        // Arrange
        var connector = new AnthropicChatConnector(
            _fixture.PlatformSettings,
            TestsLogger.CreateLogger<AnthropicChatConnector>(_output), new NoOpLimiter());

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
        int maxTokens = 2000;
        int thinkingBudget = 1024;

        // Act
        ResponseContentSequence responseSequence = await connector.GetThinkingResponseWithCachingAsync(
            userContent,
            cachedContent,
            systemPrompt: systemPrompt,
            maxTokens: maxTokens,
            thinkingBudget: thinkingBudget,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(responseSequence);
        Assert.True(responseSequence.InputTokens > 0);
        Assert.True(responseSequence.OutputTokens > 0);
        Assert.True(connector.CacheCreationInputTokens > 0 || connector.CacheReadInputTokens > 0);
    }

    [PaidApiFact(CALLS_THE_ANTHROPIC_API, alsoInCi: true)]
    public async Task GetResponseAsync_EmptyMessage_ReturnsErrorMessageAsync()
    {
        // Arrange
        var connector = new AnthropicChatConnector(
            _fixture.PlatformSettings,
            TestsLogger.CreateLogger<AnthropicChatConnector>(_output), new NoOpLimiter());
        var emptySequence = new RequestContentSequence();

        // Act & Assert
        ResponseContentSequence responseSequence = await connector.GetResponseAsync(emptySequence, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(IChatConnector.ERROR_PREFIX, responseSequence.GetPlainText());
        Assert.Contains("InvalidRequest: messages.0", responseSequence.GetPlainText());
    }

    [Fact]
    public async Task GetThinkingResponseAsync_InvalidThinkingBudget_ReturnsErrorAsync()
    {
        // Arrange
        AnthropicChatConnector connector = CreateConnectorWithAKeyItNeverSends();
        var contentSequence = new RequestContentSequence()
        {
            "Test message"
        };
        int invalidBudget = 500; // Less than minimum 1024

        // Act — pin a non-adaptive model; budget validation only applies to legacy models
        ResponseContentSequence responseSequence = await connector.GetThinkingResponseAsync(
            contentSequence,
            thinkingBudget: invalidBudget,
            model: "us.anthropic.claude-sonnet-4-6",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(IChatConnector.ERROR_PREFIX, responseSequence.GetPlainText());
        Assert.Contains("budget must be at least 1024", responseSequence.GetPlainText());
    }

    [Fact]
    public async Task GetThinkingResponseAsync_BudgetExceedsMaxTokens_ReturnsErrorAsync()
    {
        // Arrange
        AnthropicChatConnector connector = CreateConnectorWithAKeyItNeverSends();
        var contentSequence = new RequestContentSequence()
        {
            "Test message"
        };
        int maxTokens = 2000;
        int exceedingBudget = 3000;

        // Act — pin a non-adaptive model; budget validation only applies to legacy models
        ResponseContentSequence responseSequence = await connector.GetThinkingResponseAsync(
            contentSequence,
            maxTokens: maxTokens,
            thinkingBudget: exceedingBudget,
            model: "us.anthropic.claude-sonnet-4-6",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(IChatConnector.ERROR_PREFIX, responseSequence.GetPlainText());
        Assert.Contains("budget cannot be greater than", responseSequence.GetPlainText());
    }

    [PaidApiFact(CALLS_THE_ANTHROPIC_API, alsoInCi: true)]
    public async Task GetResponseAsync_WithStringInput_ReturnsResponseAsync()
    {
        // Arrange
        var connector = new AnthropicChatConnector(
            _fixture.PlatformSettings,
            TestsLogger.CreateLogger<AnthropicChatConnector>(_output), new NoOpLimiter());
        string userInput = "Respond with 'YES'";
        string systemPrompt = "You are an assistant who follows instructions. Answer in ONE word";
        int maxTokens = 2;

        // Act
        ResponseContentSequence responseSequence = await connector.GetResponseAsync(
            userInput,
            systemPrompt: systemPrompt,
            maxTokens: maxTokens,
            shouldUseAdvancedModel: false,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(responseSequence);
        Assert.Contains("YES", responseSequence.GetPlainText(), StringComparison.OrdinalIgnoreCase);
        Assert.True(connector.InputTokens > 0);
        Assert.True(connector.OutputTokens > 0);
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

    /// <summary>
    /// The budget checks refuse the request before it is sent, so these tests need a key in the
    /// settings but never use it, and run with no Anthropic key in the environment.
    /// </summary>
    private AnthropicChatConnector CreateConnectorWithAKeyItNeverSends()
    {
        return new AnthropicChatConnector(
            new PlatformSettings { Anthropic = new AnthropicSettings { ApiKey = "never-sent" } },
            TestsLogger.CreateLogger<AnthropicChatConnector>(_output), new NoOpLimiter());
    }
}
