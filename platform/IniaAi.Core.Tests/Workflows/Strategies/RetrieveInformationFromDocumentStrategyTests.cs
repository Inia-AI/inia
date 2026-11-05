using IniaAi.Common.Enums;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Annotations;
using IniaAi.Core.Workflows.Outputs.TableOfContents;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using FileMetadata = IniaAi.Domain.Entities.Documents.FileMetadata;

namespace IniaAi.Core.Tests.Workflows.Strategies;

public class RetrieveInformationFromDocumentStrategyTests : IClassFixture<WorkflowsFixture<RetrieveInformationFromDocumentStrategyTests>>
{
    private readonly IServiceProvider _testServiceProvider;
    private readonly WorkflowsFixture<RetrieveInformationFromDocumentStrategyTests> _fixture;
    private readonly ITestOutputHelper _outputHelper;

    public RetrieveInformationFromDocumentStrategyTests(WorkflowsFixture<RetrieveInformationFromDocumentStrategyTests> blocksFixture, ITestOutputHelper outputHelper)
    {
        _fixture = blocksFixture;
        _outputHelper = outputHelper;
        _testServiceProvider = _fixture.BuildTestServiceProvider(_outputHelper);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ValidBlockAndContext_CallsRetrieveInformationFromDocumentMethodsAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.SimpleRag, workflow)
        {
            Name = "Simple Rag Block",
            SupportedDocumentClasses = ["Any"],
        };
        var doc1 = new Document(new FileMetadata("IniaAi", "Doc1.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        _ = new Page(doc1, 1, 1) { RawText = "This is a page about rockets" };
        block.Documents.Add(doc1);
        _ = new RagSettings(block) { Type = RagTypes.WholeDocument };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new RetrieveInformationFromDocumentStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Contains("rockets", block.BlockResponse.RawResponse, StringComparison.InvariantCultureIgnoreCase);
    }

    [Fact]
    public async Task ExecuteBlockAsync_NoFilteredPages_ReturnsEmptyResponseAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.SimpleRag, workflow)
        {
            Name = "Simple Rag Block",
            SupportedDocumentClasses = ["Any"],
        };
        var doc1 = new Document(new FileMetadata("IniaAi", "Doc1.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        _ = new Page(doc1, 1, 1) { RawText = "This is a page about rockets" };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new RetrieveInformationFromDocumentStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Equal(string.Empty, block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ValidBlockAndContextWithToc_CallsRetrieveInformationFromDocumentMethodsAsync()
    {
        // Arrange
        var toc = new TableOfContentsList();
        toc.Sections.Add(new TableOfContentsEntry() { Title = "Transport", StartPage = 1, EndPage = 3 });
        toc.Sections.Add(new TableOfContentsEntry() { Title = "Wild animals", StartPage = 7, EndPage = 9 });
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.SimpleRag, workflow)
        {
            Name = "Simple Rag Block",
        };
        _ = new RagSettings(block) { Type = RagTypes.UseTableOfContents, TableOfContentsInput = toc.ToJson(pretty: true) };
        var doc1 = new Document(new FileMetadata("IniaAi", "Doc1.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        _ = new Page(doc1, 1, 1) { RawText = "This is a page about rockets", EmbeddingVector = new("", [0]) };
        _ = new Page(doc1, 2, 2) { RawText = "This is a page about cars", EmbeddingVector = new("", [0]) };
        _ = new Page(doc1, 3, 3) { RawText = "This is a page about planes", EmbeddingVector = new("", [0]) };
        _ = new Page(doc1, 4, 4) { RawText = "This is a page about dogs", EmbeddingVector = new("", [0]) };
        _ = new Page(doc1, 5, 5) { RawText = "This is a page about cats", EmbeddingVector = new("", [0]) };
        _ = new Page(doc1, 6, 6) { RawText = "This is a page about cows", EmbeddingVector = new("", [0]) };
        _ = new Page(doc1, 7, 7) { RawText = "This is a page about tigers", EmbeddingVector = new("", [0]) };
        _ = new Page(doc1, 8, 8) { RawText = "This is a page about lions", EmbeddingVector = new("", [0]) };
        _ = new Page(doc1, 9, 9) { RawText = "This is a page about elephants", EmbeddingVector = new("", [0]) };
        block.Documents.Add(doc1);
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new RetrieveInformationFromDocumentStrategy();
        string expectedOutput =
        """
        <page1(Doc1.pdf)>
        This is a page about rockets
        </page1(Doc1.pdf)>

        <page2(Doc1.pdf)>
        This is a page about cars
        </page2(Doc1.pdf)>

        <page3(Doc1.pdf)>
        This is a page about planes
        </page3(Doc1.pdf)>

        <page7(Doc1.pdf)>
        This is a page about tigers
        </page7(Doc1.pdf)>

        <page8(Doc1.pdf)>
        This is a page about lions
        </page8(Doc1.pdf)>

        <page9(Doc1.pdf)>
        This is a page about elephants
        </page9(Doc1.pdf)>
        """;

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Equal(expectedOutput, block.BlockResponse.RawResponse, ignoreCase: true, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public async Task ExecuteBlockAsync_MultipleDocumentsWithDuplicatePageNumbers_ShouldNotThrowDuplicateKeyException()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.SimpleRag, workflow)
        {
            Name = "Multi-Doc Rag Block",
            SupportedDocumentClasses = ["Any"],
        };

        // Create first document with pages 1, 2, 3
        var doc1 = new Document(new FileMetadata("IniaAi", "Doc1.pdf", []), MediaType.ApplicationPdf);
        _ = new Page(doc1, 1, 1) { RawText = "Doc1 Page 1 content", EmbeddingVector = new("", [0]) };
        _ = new Page(doc1, 2, 2) { RawText = "Doc1 Page 2 content", EmbeddingVector = new("", [0]) };
        _ = new Page(doc1, 3, 3) { RawText = "Doc1 Page 3 content", EmbeddingVector = new("", [0]) };
        block.Documents.Add(doc1);

        // Create second document with pages 1, 2, 3 (same page numbers as doc1)
        var doc2 = new Document(new FileMetadata("IniaAi", "Doc2.pdf", []), MediaType.ApplicationPdf);
        _ = new Page(doc2, 1, 1) { RawText = "Doc2 Page 1 content", EmbeddingVector = new("", [0]) };
        _ = new Page(doc2, 2, 2) { RawText = "Doc2 Page 2 content", EmbeddingVector = new("", [0]) };
        _ = new Page(doc2, 3, 3) { RawText = "Doc2 Page 3 content", EmbeddingVector = new("", [0]) };
        block.Documents.Add(doc2);

        // Use WholeDocument RAG type to ensure all pages are filtered
        _ = new RagSettings(block) { Type = RagTypes.WholeDocument };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new RetrieveInformationFromDocumentStrategy();

        // Act & Assert - This should not throw ArgumentException about duplicate keys
        Exception? exception = await Record.ExceptionAsync(async () => await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Null(exception);
        Assert.NotNull(block.BlockResponse);
        // Verify we get content from both documents
        Assert.Contains("Doc1", block.BlockResponse.RawResponse);
        Assert.Contains("Doc2", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_MultipleRagSettingsWithOverlappingPages_ShouldNotThrowDuplicateKeyException()
    {
        // Arrange - Create scenario where multiple RagSettings filter overlapping pages
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.SimpleRag, workflow)
        {
            Name = "Multi-RAG Block",
            SupportedDocumentClasses = ["Any"],
        };

        // Create documents with overlapping page numbers
        var doc1 = new Document(new FileMetadata("IniaAi", "Doc1.pdf", []), MediaType.ApplicationPdf);
        _ = new Page(doc1, 1, 1) { RawText = "Doc1 Page 1 content", EmbeddingVector = new("", [0]) };
        _ = new Page(doc1, 2, 2) { RawText = "Doc1 Page 2 content", EmbeddingVector = new("", [0]) };
        _ = new Page(doc1, 3, 3) { RawText = "Doc1 Page 3 content", EmbeddingVector = new("", [0]) };
        block.Documents.Add(doc1);

        var doc2 = new Document(new FileMetadata("IniaAi", "Doc2.pdf", []), MediaType.ApplicationPdf);
        _ = new Page(doc2, 1, 1) { RawText = "Doc2 Page 1 content", EmbeddingVector = new("", [0]) };
        _ = new Page(doc2, 2, 2) { RawText = "Doc2 Page 2 content", EmbeddingVector = new("", [0]) };
        _ = new Page(doc2, 3, 3) { RawText = "Doc2 Page 3 content", EmbeddingVector = new("", [0]) };
        block.Documents.Add(doc2);

        // Create multiple RagSettings that will return overlapping pages
        _ = new RagSettings(block) { Type = RagTypes.WholeDocument }; // Will return all pages from both docs
        _ = new RagSettings(block) { Type = RagTypes.ExactPages, ExactIndexesInput = "1,2" }; // Will return pages 1,2 from both docs

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new RetrieveInformationFromDocumentStrategy();

        // Act & Assert - With the fix, this should NOT throw ArgumentException about duplicate keys
        Exception? exception = await Record.ExceptionAsync(async () => await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        // With the fix applied, no exception should be thrown
        Assert.Null(exception);
        Assert.NotNull(block.BlockResponse);
        // Verify we get content from both documents
        Assert.Contains("Doc1", block.BlockResponse.RawResponse);
        Assert.Contains("Doc2", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_WithOverviewsTemplate_RetrievesPageOverviewsAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.SimpleRag, workflow)
        {
            Name = "Simple Rag Block with Overviews",
            SupportedDocumentClasses = ["Any"],
            Properties = new RetrieveDataFromDocumentsProperties
            {
                ShouldUseOverviews = true,
            },
        };
        var doc1 = new Document(new FileMetadata("IniaAi", "Doc1.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        _ = new Page(doc1, 1, 1)
        {
            RawText = "This is a page about rockets",
            Overview = "This page is about rocket science and space exploration.",
        };
        block.Documents.Add(doc1);
        _ = new RagSettings(block) { Type = RagTypes.WholeDocument };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new RetrieveInformationFromDocumentStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        // Should contain overview instead of page content
        Assert.Contains("rocket science", block.BlockResponse.RawResponse, StringComparison.InvariantCultureIgnoreCase);
        Assert.Contains("space exploration", block.BlockResponse.RawResponse, StringComparison.InvariantCultureIgnoreCase);
    }

    [Fact]
    public async Task ExecuteBlockAsync_WithoutOverviewsTemplate_RetrievesPageTextAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.SimpleRag, workflow)
        {
            Name = "Simple Rag Block without Overviews",
            SupportedDocumentClasses = ["Any"],
            Properties = new RetrieveDataFromDocumentsProperties
            {
                ShouldUseOverviews = false,
            },
        };
        var doc1 = new Document(new FileMetadata("IniaAi", "Doc1.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        _ = new Page(doc1, 1, 1)
        {
            RawText = "This is a page about rockets and their propulsion systems",
            Overview = "This page is about rocket science and space exploration.",
        };
        block.Documents.Add(doc1);
        _ = new RagSettings(block) { Type = RagTypes.WholeDocument };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new RetrieveInformationFromDocumentStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        // Should contain page content, not overview
        Assert.Contains("propulsion systems", block.BlockResponse.RawResponse, StringComparison.InvariantCultureIgnoreCase);
        Assert.DoesNotContain("space exploration", block.BlockResponse.RawResponse);
    }

    #region Fact Creation Tests

    [Fact]
    public async Task ExecuteBlockAsync_WithPages_CreatesFactsInBlockAnnotationsAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.SimpleRag, workflow)
        {
            Name = "Simple Rag Block",
            SupportedDocumentClasses = ["Any"],
        };
        var doc1 = new Document(new FileMetadata("IniaAi", "Doc1.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        _ = new Page(doc1, 1, 1) { RawText = "Revenue was $5M in Q4" };
        _ = new Page(doc1, 2, 2) { RawText = "Expenses were $3M" };
        block.Documents.Add(doc1);
        _ = new RagSettings(block) { Type = RagTypes.WholeDocument };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new RetrieveInformationFromDocumentStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert — 2 broad facts + numerical sub-facts ($5M, $3M)
        Assert.True(context.BlockAnnotations.Facts.Count >= 2,
            $"Expected at least 2 facts, got {context.BlockAnnotations.Facts.Count}");
        Assert.Contains(context.BlockAnnotations.Facts, f => f.Value.Contains("Revenue"));
        Assert.Contains(context.BlockAnnotations.Facts, f => f.Value.Contains("Expenses"));
    }

    [Fact]
    public async Task ExecuteBlockAsync_WithPages_CreatesFactsWithDocumentSourceAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.SimpleRag, workflow)
        {
            Name = "Simple Rag Block",
            SupportedDocumentClasses = ["Any"],
        };
        var doc1 = new Document(new FileMetadata("IniaAi", "Report.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        var page = new Page(doc1, 3, 5) { RawText = "Important financial data" };
        block.Documents.Add(doc1);
        _ = new RagSettings(block) { Type = RagTypes.WholeDocument };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new RetrieveInformationFromDocumentStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(context.BlockAnnotations.Facts);
        Fact fact = context.BlockAnnotations.Facts.First();
        _ = Assert.Single(fact.Sources);

        DocumentSource docSource = Assert.IsType<DocumentSource>(fact.Sources.First());
        Assert.Equal(page.DocumentId, docSource.DocumentId);
        Assert.Equal(page.Id, docSource.PageId);
        Assert.Equal(3, docSource.PageNumber);
        Assert.Equal(5, docSource.FilePageNumber);
        Assert.Equal("Report.pdf", docSource.DocumentName);
    }

    [Fact]
    public async Task ExecuteBlockAsync_WithPageWithSourceUrl_CreatesFactWithUrlSourceAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.SimpleRag, workflow)
        {
            Name = "Simple Rag Block",
            SupportedDocumentClasses = ["Any"],
        };
        var doc1 = new Document(new FileMetadata("IniaAi", "WebContent.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        _ = new Page(doc1, 1, 1)
        {
            RawText = "Web content about AI",
            SourceUrl = "https://example.com/article"
        };
        block.Documents.Add(doc1);
        _ = new RagSettings(block) { Type = RagTypes.WholeDocument };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new RetrieveInformationFromDocumentStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        _ = Assert.Single(context.BlockAnnotations.Facts);
        Fact fact = context.BlockAnnotations.Facts.First();
        _ = Assert.Single(fact.Sources);

        UrlSource urlSource = Assert.IsType<UrlSource>(fact.Sources.First());
        Assert.Equal("https://example.com/article", urlSource.Url);
    }

    [Fact]
    public async Task ExecuteBlockAsync_BlockThatReachesNoParagraph_DoesNotAddFactsAsync()
    {
        var block = new Block(BlockTypes.SimpleRag, new Section("1234", new Workflow("1234")), "load_AR")
        {
            Name = "Annual Report - Load Document",
            SupportedDocumentClasses = ["Any"],
        };
        var document = new Document(new FileMetadata("IniaAi", "Doc1.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        _ = new Page(document, 1, 1) { RawText = "Revenue was $5M in Q4" };
        block.Documents.Add(document);
        _ = new RagSettings(block) { Type = RagTypes.WholeDocument };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        context.IsCitationSource = false;

        _ = await new RetrieveInformationFromDocumentStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Empty(context.BlockAnnotations.Facts);
    }

    [Fact]
    public async Task ExecuteBlockAsync_WithOverviews_DoesNotAddFactsAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.SimpleRag, workflow)
        {
            Name = "Simple Rag Block with Overviews",
            SupportedDocumentClasses = ["Any"],
            Properties = new RetrieveDataFromDocumentsProperties
            {
                ShouldUseOverviews = true,
            },
        };
        var doc1 = new Document(new FileMetadata("IniaAi", "Doc1.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        _ = new Page(doc1, 1, 1)
        {
            RawText = "Detailed page content here",
            Overview = "Summary: Revenue highlights"
        };
        block.Documents.Add(doc1);
        _ = new RagSettings(block) { Type = RagTypes.WholeDocument };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new RetrieveInformationFromDocumentStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(context.BlockAnnotations.Facts);
    }

    [Fact]
    public async Task ExecuteBlockAsync_NoFilteredPages_DoesNotAddFactsAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.SimpleRag, workflow)
        {
            Name = "Simple Rag Block",
            SupportedDocumentClasses = ["Any"],
        };
        // Document with page but document not added to block -> no filtered pages
        var doc1 = new Document(new FileMetadata("IniaAi", "Doc1.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        _ = new Page(doc1, 1, 1) { RawText = "Some content" };
        // Note: NOT adding doc1 to block.Documents
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new RetrieveInformationFromDocumentStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(context.BlockAnnotations.Facts);
    }

    #endregion
}
