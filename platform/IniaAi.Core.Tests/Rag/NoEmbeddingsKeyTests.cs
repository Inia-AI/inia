using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Cohere;
using IniaAi.Common.Enums;
using IniaAi.Configuration;
using IniaAi.Configuration.LLMProviders;
using IniaAi.Core.Rag;
using IniaAi.Core.Tests.Workflows;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using FileMetadata = IniaAi.Domain.Entities.Documents.FileMetadata;

namespace IniaAi.Core.Tests.Rag;

/// <summary>
/// A deployment with no Cohere key runs every filter rule that reads no embeddings, even one that
/// carries filter expressions. A rule that ranks pages by embeddings ranks the pages that have one,
/// and when it cannot rank, the block says which setting it needs, or that no page has an embedding.
/// Pages without a key carry the empty vectors that document processing leaves.
/// </summary>
public class NoEmbeddingsKeyTests : IClassFixture<WorkflowsFixture<NoEmbeddingsKeyTests>>
{
    private const string NEEDS_THE_KEY = "needs a Cohere API key";
    private const string EMBED_PATH = "/v2/embed";
    private const string FAILED_EMBEDDING_MODEL = "ERROR";
    private const string FILTER_EXPRESSION = "revenue for the year";

    private readonly WorkflowsFixture<NoEmbeddingsKeyTests> _fixture;
    private readonly ITestOutputHelper _outputHelper;

    public NoEmbeddingsKeyTests(WorkflowsFixture<NoEmbeddingsKeyTests> fixture, ITestOutputHelper outputHelper)
    {
        _fixture = fixture;
        _outputHelper = outputHelper;
    }

    [Theory]
    [InlineData(RagTypes.WholeDocument, "", 3, null)]
    [InlineData(RagTypes.WholeDocument, "", 3, "")]
    [InlineData(RagTypes.ExactPages, "", 2, "")]
    [InlineData(RagTypes.NPagesStartingFromIndex, "2", 2, "")]
    public async Task ARuleThatReadsNoEmbeddings_SelectsItsPages_ThoughItCarriesFilterExpressions(RagTypes type, string numberOfPages, int expectedPages, string? key)
    {
        using var cohere = new LocalTestHttpServer();
        IServiceProvider services = ServicesWith(KeylessCohere(key, cohere));
        RagSettings settings = Settings(type);
        settings.ExactIndexesInput = "1,2";
        settings.StartIndexInput = "2";
        settings.NumberOfPagesInput = numberOfPages;
        settings.SmartFilterExpressions = [FILTER_EXPRESSION];

        IReadOnlyList<Page> selected = await SelectAsync(services, settings, DocumentWithoutEmbeddings());

        Assert.Equal(expectedPages, selected.Count);
        Assert.Equal(0, cohere.PathHits(EMBED_PATH));
    }

    [Theory]
    [InlineData("1", new[] { 1, 2 })]
    [InlineData("3", new[] { 3, 4 })]
    public async Task APageRangeRule_CountsEveryPage_ThoughNoPageHasAnEmbeddingRecord(string startIndex, int[] expectedPages)
    {
        using var cohere = new LocalTestHttpServer();
        IServiceProvider services = ServicesWith(KeylessCohere(string.Empty, cohere));
        Document document = DocumentWithoutEmbeddings(pages: 4);
        foreach (Page page in document.Pages)
        {
            page.EmbeddingVector = null;
        }

        RagSettings settings = Settings(RagTypes.NPagesStartingFromIndex);
        settings.StartIndexInput = startIndex;
        settings.NumberOfPagesInput = "2";

        IReadOnlyList<Page> selected = await SelectAsync(services, settings, document);

        Assert.Equal(expectedPages, selected.Select(static page => page.PageNumber));
    }

    [Fact]
    public async Task AKeywordRuleWithoutFilterExpressions_SelectsThePagesWithItsKeyword()
    {
        using var cohere = new LocalTestHttpServer();
        IServiceProvider services = ServicesWith(KeylessCohere(string.Empty, cohere));
        RagSettings settings = Settings(RagTypes.KeywordBased);
        settings.Keywords = ["Page 2"];

        IReadOnlyList<Page> selected = await SelectAsync(services, settings, DocumentWithoutEmbeddings());

        Assert.Equal([2], selected.Select(static page => page.PageNumber));
        Assert.Equal(0, cohere.PathHits(EMBED_PATH));
    }

    [Fact]
    public async Task AKeywordRuleWithFilterExpressions_SaysWhichSettingItNeeds()
    {
        using var cohere = new LocalTestHttpServer();
        IServiceProvider services = ServicesWith(KeylessCohere(string.Empty, cohere));
        RagSettings settings = Settings(RagTypes.KeywordBased);
        settings.Keywords = ["Page 2"];
        settings.SmartFilterExpressions = [FILTER_EXPRESSION];

        EmbeddingsUnavailableException unavailable = await Assert.ThrowsAsync<EmbeddingsUnavailableException>(
            () => SelectAsync(services, settings, DocumentWithoutEmbeddings()));

        Assert.Contains(NEEDS_THE_KEY, unavailable.Message, StringComparison.Ordinal);
        Assert.Equal(0, cohere.PathHits(EMBED_PATH));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnAiQueryBlockWhoseRuleRanksPagesByEmbeddings_IsSkipped_WithTheSettingItNeeds(bool withFilterExpressions)
    {
        using var cohere = new LocalTestHttpServer();
        IServiceProvider services = ServicesWith(KeylessCohere(string.Empty, cohere));

        Block block = await RunAiQueryAsync(services, DocumentWithoutEmbeddings(), withFilterExpressions);

        Assert.Equal(ExecutionStatus.Skipped, block.ExecutionState?.Status);
        Assert.Contains(NEEDS_THE_KEY, block.ExecutionState?.Error, StringComparison.Ordinal);
        Assert.Equal(0, cohere.PathHits(EMBED_PATH));
    }

    [Fact]
    public async Task AnAiQueryBlockOnPagesEmbeddedEarlier_IsSkipped_WhenTheKeyIsGone()
    {
        using var cohere = new LocalTestHttpServer();
        IServiceProvider services = ServicesWith(KeylessCohere(string.Empty, cohere));
        Document document = await DocumentAsync(new WordHashEmbeddingsConnector(), "revenue for the year rose", "staff numbers grew");

        Block block = await RunAiQueryAsync(services, document, withFilterExpressions: true);

        Assert.Equal(ExecutionStatus.Skipped, block.ExecutionState?.Status);
        Assert.Contains(NEEDS_THE_KEY, block.ExecutionState?.Error, StringComparison.Ordinal);
        Assert.Equal(0, cohere.PathHits(EMBED_PATH));
    }

    [Fact]
    public async Task ARankingRule_RanksOnlyThePagesThatHaveAnEmbedding()
    {
        var embeddings = new WordHashEmbeddingsConnector();
        IServiceProvider services = ServicesWith(embeddings);
        Document document = await DocumentAsync(embeddings, null, null, "revenue for the year rose", "staff numbers grew");
        document.Pages.First(static page => page.PageNumber == 1).EmbeddingVector = new(FAILED_EMBEDDING_MODEL, []);
        document.Pages.First(static page => page.PageNumber == 2).EmbeddingVector = null;
        RagSettings settings = Settings(RagTypes.TopPage);
        settings.SmartFilterExpressions = [FILTER_EXPRESSION];

        IReadOnlyList<Page> selected = await SelectAsync(services, settings, document);

        Assert.Equal([3], selected.Select(static page => page.PageNumber));
    }

    [Fact]
    public async Task ARankingRuleOverPagesWithNoEmbedding_SaysSo_WhenTheConnectorNeedsNoSetting()
    {
        IServiceProvider services = ServicesWith(new WordHashEmbeddingsConnector());
        RagSettings settings = Settings(RagTypes.TopPage);
        settings.SmartFilterExpressions = [FILTER_EXPRESSION];

        EmbeddingsUnavailableException unavailable = await Assert.ThrowsAsync<EmbeddingsUnavailableException>(
            () => SelectAsync(services, settings, DocumentWithoutEmbeddings()));

        Assert.DoesNotContain(NEEDS_THE_KEY, unavailable.Message, StringComparison.Ordinal);
        Assert.Contains("overview prompt", unavailable.Message, StringComparison.Ordinal);
    }

    private static RagSettings Settings(RagTypes type)
    {
        return new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow()))) { Type = type };
    }

    private static async Task<IReadOnlyList<Page>> SelectAsync(IServiceProvider services, RagSettings settings, Document document)
    {
        Dictionary<string, Dictionary<int, Page>> selected = await services.GetRequiredService<RagService>()
            .GetFilteredPagesAsync(settings, [document], WorkflowType.GENERIC, TestContext.Current.CancellationToken);

        return [.. selected.Values.SelectMany(static pages => pages.Values)];
    }

    private static async Task<Block> RunAiQueryAsync(IServiceProvider services, Document document, bool withFilterExpressions)
    {
        var block = new Block(BlockTypes.AiQuery, new Section("rag", new Workflow("rag")))
        {
            Name = "Revenue",
            Properties = new AiQueryProperties { UserPromptTemplate = "What was the revenue?", MaxTokens = 10 },
        };
        block.Documents.Add(document);
        _ = new RagSettings(block)
        {
            Type = RagTypes.TopNPagesPerFilterWithTwoNeighbors,
            SmartFilterExpressions = withFilterExpressions ? [FILTER_EXPRESSION] : [],
        };

        _ = await new AiQueryStrategy().ExecuteBlockAsync(
            block,
            services.GetRequiredService<BlockExecutionContext>(),
            TestContext.Current.CancellationToken);

        return block;
    }

    private IEmbeddingsConnector KeylessCohere(string? key, LocalTestHttpServer cohere)
    {
        var settings = new PlatformSettings
        {
            Cohere = new CohereSettings { ApiKey = key, EmbeddingsApiUrl = $"{cohere.BaseUrl}{EMBED_PATH}" },
        };
        return new CohereEmbeddingsConnector(TestsLogger.CreateLogger<IEmbeddingsConnector>(_outputHelper), Options.Create(settings));
    }

    private IServiceProvider ServicesWith(IEmbeddingsConnector embeddings)
    {
        return _fixture.BuildTestServiceProvider(
            _outputHelper,
            services => services
                .AddSingleton(embeddings)
                .AddSingleton<IChatConnector>(ScriptedChatConnector.Answering("<answer>12</answer>")));
    }

    private static async Task<Document> DocumentAsync(IEmbeddingsConnector embeddings, params string?[] texts)
    {
        Document document = NewDocument();
        for (int index = 0; index < texts.Length; index++)
        {
            string text = texts[index] ?? $"Page {index + 1}";
            List<double[]> vectors = (await embeddings.EmbedAsync([text], TestContext.Current.CancellationToken)).Vectors;
            _ = new Page(document, index + 1, index + 1)
            {
                RawText = text,
                Text = text,
                Overview = text,
                EmbeddingVector = new(embeddings.ModelName, vectors[0]),
            };
        }

        return document;
    }

    private static Document DocumentWithoutEmbeddings(int pages = 3)
    {
        Document document = NewDocument();
        for (int number = 1; number <= pages; number++)
        {
            _ = new Page(document, number, number)
            {
                RawText = $"Page {number}",
                Text = $"Page {number}",
                EmbeddingVector = new(FAILED_EMBEDDING_MODEL, []),
            };
        }

        return document;
    }

    private static Document NewDocument()
    {
        return new Document(
            new FileMetadata("IniaAi", "Annual report.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()),
            MediaType.ApplicationPdf);
    }
}
