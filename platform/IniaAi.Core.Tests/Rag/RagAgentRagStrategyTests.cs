using IniaAi.Common.Enums;
using IniaAi.Core.Rag;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using IniaAi.Testing.DependencyInjection.Mocks;
using FileMetadata = IniaAi.Domain.Entities.Documents.FileMetadata;

namespace IniaAi.Core.Tests.Rag;

public class RagAgentRagStrategyTests : IClassFixture<RagFixture<RagService>>
{
    private const int CALLS_PER_ROUND = 2;

    private readonly RagService _ragService;
    private readonly ITestOutputHelper _output;
    private readonly ScriptedChatConnector _model;
    private string _sufficiency = "SUFFICIENT";

    public RagAgentRagStrategyTests(RagFixture<RagService> fixture, ITestOutputHelper output)
    {
        _output = output;
        _model = new ScriptedChatConnector(_ => $"<answer>5</answer><pages_to_keep>5</pages_to_keep><sufficiency>{_sufficiency}</sufficiency>");
        _ragService = fixture.GetRagService(output, _model);
    }

    [Fact]
    public async Task ExecuteFilteringAsync_WithMissingQueryTemplate_ShouldThrowAsync()
    {
        // Arrange
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.RagAgent,
            QueryTemplate = null,
            IterationLimit = "3",
            ContextLimit = "20"
        };
        List<Document> documents = CreateTestDocumentsWithOverviews();

        // Act & Assert
        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _ragService.GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteFilteringAsync_WithEmptyQueryTemplate_ShouldThrowAsync()
    {
        // Arrange
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.RagAgent,
            QueryTemplate = "   ",
            IterationLimit = "3",
            ContextLimit = "20"
        };
        List<Document> documents = CreateTestDocumentsWithOverviews();

        // Act & Assert
        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _ragService.GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteFilteringAsync_WithMissingOverviews_ShouldThrowAsync()
    {
        // Arrange
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.RagAgent,
            QueryTemplate = "Find pages about financial data",
            IterationLimit = "3",
            ContextLimit = "20"
        };
        var doc1 = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        _ = new Page(doc1, 1, 1) { Text = "Page 1 Text", EmbeddingVector = new("", [0.1, 0.2, 0.3]) };
        List<Document> documents = [doc1];

        // Act & Assert
        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _ragService.GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteFilteringAsync_WithEmptyPages_ShouldReturnEmptyDictionaryAsync()
    {
        // Arrange
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.RagAgent,
            QueryTemplate = "Find pages about financial data",
            IterationLimit = "3",
            ContextLimit = "20"
        };
        var doc1 = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        List<Document> documents = [doc1];

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(resultDict);
    }

    [Fact]
    public async Task ExecuteFilteringAsync_WithValidSettings_ShouldReturnPagesAsync()
    {
        // Arrange
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.RagAgent,
            QueryTemplate = "Find pages containing information about topic 5",
            IterationLimit = "2",
            ContextLimit = "10"
        };
        List<Document> documents = CreateTestDocumentsWithOverviews();

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(resultDict);
        Dictionary<int, Page> pages = resultDict.Values.First();
        Assert.NotEmpty(pages);
        _output.WriteLine($"Total pages selected: {pages.Count}");
    }

    [Fact]
    public async Task ExecuteFilteringAsync_WithIterationLimitOne_ShouldExecuteOneRoundAsync()
    {
        // Arrange
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.RagAgent,
            QueryTemplate = "Find pages about page 3",
            IterationLimit = "1",
            ContextLimit = "5"
        };
        List<Document> documents = CreateTestDocumentsWithOverviews();
        _sufficiency = "INSUFFICIENT";

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(resultDict);
        Dictionary<int, Page> pages = resultDict.Values.First();
        Assert.True(pages.Count <= 5, "Should respect context limit of 5");
        Assert.Equal(1 * CALLS_PER_ROUND, _model.Calls.Count);
        _output.WriteLine($"Pages selected in single round: {pages.Count}");
    }

    [Fact]
    public async Task ExecuteFilteringAsync_WithHighContextLimit_ShouldRespectLimitAsync()
    {
        // Arrange
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.RagAgent,
            QueryTemplate = "Find all pages",
            IterationLimit = "3",
            ContextLimit = "100"
        };
        List<Document> documents = CreateTestDocumentsWithOverviews();

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(resultDict);
        Dictionary<int, Page> pages = resultDict.Values.First();
        Assert.True(pages.Count <= 100, "Should respect context limit of 100");
        _output.WriteLine($"Pages selected: {pages.Count}");
    }

    [Fact]
    public async Task ExecuteFilteringAsync_WithInvalidIterationLimit_ShouldUseDefaultAsync()
    {
        // Arrange
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.RagAgent,
            QueryTemplate = "Find pages about financial data",
            IterationLimit = "invalid",
            ContextLimit = "20"
        };
        List<Document> documents = CreateTestDocumentsWithOverviews();
        _sufficiency = "INSUFFICIENT";

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert - Should not throw, will use default value of 3
        Assert.NotNull(resultDict);
        Assert.Equal(3 * CALLS_PER_ROUND, _model.Calls.Count);
    }

    [Fact]
    public async Task ExecuteFilteringAsync_WithInvalidContextLimit_ShouldUseDefaultAsync()
    {
        // Arrange
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.RagAgent,
            QueryTemplate = "Find pages about financial data",
            IterationLimit = "3",
            ContextLimit = "invalid"
        };
        List<Document> documents = CreateTestDocumentsWithOverviews();

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert - Should not throw, will use default value of 20
        Assert.NotNull(resultDict);
    }

    [Fact]
    public async Task ExecuteFilteringAsync_WithIterationLimitOutOfRange_ShouldClampToValidRangeAsync()
    {
        // Arrange - test upper bound clamping
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.RagAgent,
            QueryTemplate = "Find pages about financial data",
            IterationLimit = "100",  // Should be clamped to 10
            ContextLimit = "20"
        };
        List<Document> documents = CreateTestDocumentsWithOverviews();
        _sufficiency = "INSUFFICIENT";

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert - Should not throw, will clamp to max value of 10
        Assert.NotNull(resultDict);
        Assert.Equal(10 * CALLS_PER_ROUND, _model.Calls.Count);
    }

    [Fact]
    public async Task ExecuteFilteringAsync_WithContextLimitOutOfRange_ShouldClampToValidRangeAsync()
    {
        // Arrange - test upper bound clamping
        var ragSettings = new RagSettings(new Block(BlockTypes.SimpleRag, new Section("rag", new Workflow())))
        {
            Type = RagTypes.RagAgent,
            QueryTemplate = "Find pages about financial data",
            IterationLimit = "3",
            ContextLimit = "500"  // Should be clamped to 100
        };
        List<Document> documents = CreateTestDocumentsWithOverviews();

        // Act
        Dictionary<string, Dictionary<int, Page>> resultDict = await _ragService
            .GetFilteredPagesAsync(
                ragSettings,
                documents,
                WorkflowType.GENERIC,
                TestContext.Current.CancellationToken);

        // Assert - Should not throw, will clamp to max value of 100
        Assert.NotNull(resultDict);
    }

    private static List<Document> CreateTestDocumentsWithOverviews()
    {
        var doc1 = new Document(new FileMetadata("1234", "Document 1", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);
        var doc2 = new Document(new FileMetadata("1234", "Document 2", 1, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.Unknown);

        for (int i = 1; i <= 10; i++)
        {
            _ = new Page(doc1, i, i)
            {
                Text = $"Page {i} contains information about topic {i}. This is detailed content for page {i}.",
                Overview = $"Page {i}: Summary of topic {i}",
                EmbeddingVector = new("test-model", [.. Enumerable.Range(0, 1536).Select(_ => Random.Shared.NextDouble())])
            };
        }

        for (int i = 1; i <= 10; i++)
        {
            _ = new Page(doc2, i, i)
            {
                Text = $"Page {i} of document 2 discusses subject {i}. More details about subject {i} here.",
                Overview = $"Page {i}: Overview of subject {i}",
                EmbeddingVector = new("test-model", [.. Enumerable.Range(0, 1536).Select(_ => Random.Shared.NextDouble())])
            };
        }

        return [doc1, doc2];
    }
}
