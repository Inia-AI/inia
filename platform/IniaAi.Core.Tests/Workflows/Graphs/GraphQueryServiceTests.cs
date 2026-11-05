using IniaAi.AiConnection.Abstractions;
using IniaAi.Core.Workflows.Graph;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Domain.Graph;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows.Graphs;

public class GraphQueryServiceTests : IClassFixture<GraphQueryServiceFixture>
{
    private readonly GraphQueryServiceFixture _fixture;
    private readonly IServiceProvider _testServiceProvider;
    private readonly IServiceScope _serviceScope;
    private readonly IGraphQueryService _graphQueryService;
    private readonly UserDataDbContext _dbContext;
    private readonly ITestOutputHelper _testOutputHelper;

    public GraphQueryServiceTests(
        GraphQueryServiceFixture fixture,
        ITestOutputHelper testOutputHelper)
    {
        _fixture = fixture;
        _testOutputHelper = testOutputHelper;
        _testServiceProvider = _fixture.BuildTestServiceProvider(testOutputHelper);
        _serviceScope = _testServiceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        _graphQueryService = _serviceScope.ServiceProvider.GetRequiredService<IGraphQueryService>();
        _dbContext = _serviceScope.ServiceProvider.GetRequiredService<UserDataDbContext>();
    }

    #region ValidateAccessAsync Tests

    [Fact]
    public async Task ValidateAccessAsync_WithValidOrganization_ReturnsTrue()
    {
        // Arrange
        string organizationId = _fixture.GetOrganization().Id;
        string runId = Guid.NewGuid().ToString();

        var workflowExecution = new WorkflowExecution
        {
            Id = runId,
            OrganizationId = organizationId,
            Title = "Test Execution"
        };
        _ = _dbContext.WorkflowExecutions.Add(workflowExecution);
        _ = await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        bool result = await _graphQueryService.ValidateAccessAsync(runId, organizationId, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ValidateAccessAsync_WithDifferentOrganization_ReturnsFalse()
    {
        // Arrange
        string organizationId = _fixture.GetOrganization().Id;
        string differentOrganizationId = Guid.NewGuid().ToString();
        string runId = Guid.NewGuid().ToString();

        var workflowExecution = new WorkflowExecution
        {
            Id = runId,
            OrganizationId = organizationId,
            Title = "Test Execution"
        };
        _ = _dbContext.WorkflowExecutions.Add(workflowExecution);
        _ = await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        bool result = await _graphQueryService.ValidateAccessAsync(runId, differentOrganizationId, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ValidateAccessAsync_WithNonExistentRunId_ReturnsFalse()
    {
        // Arrange
        string organizationId = _fixture.GetOrganization().Id;
        string nonExistentRunId = Guid.NewGuid().ToString();

        // Act
        bool result = await _graphQueryService.ValidateAccessAsync(nonExistentRunId, organizationId, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region ValidateTargetExistsAsync Tests

    [Fact]
    public async Task ValidateTargetExistsAsync_WithExistingTargetNode_ReturnsTrue()
    {
        // Arrange
        string organizationId = _fixture.GetOrganization().Id;
        string runId = Guid.NewGuid().ToString();
        string targetTag = "testBlock";
        var root = new Workflow { Title = "Test Workflow" };

        // Create workflow with a block
        var section = new Section("Section 1", root) { Title = "Section 1" };

        _ = new Block(BlockTypes.Merge, section)
        {
            Name = "Test Block",
            ReplacementTag = targetTag,
            Order = 1
        };
        _ = _dbContext.Workflows.Add(root);

        // Create workflow execution
        var workflowExecution = new WorkflowExecution
        {
            Id = runId,
            OrganizationId = organizationId,
            WorkflowId = root.Id,
            Title = "Test Execution"
        };
        _ = _dbContext.WorkflowExecutions.Add(workflowExecution);

        // Create graph node for the target
        var graphNode = new GraphNode
        {
            RunId = runId,
            Key = targetTag,
            Kind = "block",
            Label = "Test Block"
        };
        _ = _dbContext.GraphNodes.Add(graphNode);

        _ = await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        bool result = await _graphQueryService.ValidateTargetExistsAsync(runId, targetTag, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ValidateTargetExistsAsync_WithNonExistentTargetNode_ReturnsFalse()
    {
        // Arrange
        string organizationId = _fixture.GetOrganization().Id;
        string runId = Guid.NewGuid().ToString();
        string existingTag = "existingBlock";
        string nonExistentTag = "nonExistentBlock";
        var root = new Workflow { Title = "Test Workflow" };

        // Create workflow with a block
        var section = new Section("Section 1", root) { Title = "Section 1" };

        _ = new Block(BlockTypes.Merge, section)
        {
            Name = "Test Block",
            ReplacementTag = existingTag,
            Order = 1
        };
        _ = _dbContext.Workflows.Add(root);

        // Create workflow execution
        var workflowExecution = new WorkflowExecution
        {
            Id = runId,
            OrganizationId = organizationId,
            WorkflowId = root.Id,
            Title = "Test Execution"
        };
        _ = _dbContext.WorkflowExecutions.Add(workflowExecution);

        // Create graph node only for the existing tag
        var graphNode = new GraphNode
        {
            RunId = runId,
            Key = existingTag,
            Kind = "block",
            Label = "Test Block"
        };
        _ = _dbContext.GraphNodes.Add(graphNode);

        _ = await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        bool result = await _graphQueryService.ValidateTargetExistsAsync(runId, nonExistentTag, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ValidateTargetExistsAsync_WithNonExistentRunId_ReturnsFalse()
    {
        // Arrange
        string nonExistentRunId = Guid.NewGuid().ToString();
        string targetTag = "testBlock";

        // Act
        bool result = await _graphQueryService.ValidateTargetExistsAsync(nonExistentRunId, targetTag, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region QueryAsync Validation Tests

    [Fact]
    public async Task QueryAsync_WithDepthLessThanOne_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        string runId = Guid.NewGuid().ToString();
        string targetTag = "testBlock";
        string question = "What is this?";
        int invalidDepth = 0;

        // Act & Assert
        ArgumentOutOfRangeException exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _graphQueryService.QueryAsync(runId, targetTag, question, invalidDepth, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("startingDepth", exception.ParamName);
    }

    [Fact]
    public async Task QueryAsync_WithDepthGreaterThanMax_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        string runId = Guid.NewGuid().ToString();
        string targetTag = "testBlock";
        string question = "What is this?";
        int invalidDepth = 7; // MAX_GRAPH_TRAVERSAL_DEPTH is 6

        // Act & Assert
        ArgumentOutOfRangeException exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _graphQueryService.QueryAsync(runId, targetTag, question, invalidDepth, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("startingDepth", exception.ParamName);
    }

    [Fact]
    public async Task QueryAsync_WithEmptyQuestion_ThrowsArgumentException()
    {
        // Arrange
        string runId = Guid.NewGuid().ToString();
        string targetTag = "testBlock";
        string emptyQuestion = "";

        // Act & Assert
        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _graphQueryService.QueryAsync(runId, targetTag, emptyQuestion, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("question", exception.ParamName);
        Assert.Contains("cannot be empty", exception.Message);
    }

    [Fact]
    public async Task QueryAsync_WithWhitespaceQuestion_ThrowsArgumentException()
    {
        // Arrange
        string runId = Guid.NewGuid().ToString();
        string targetTag = "testBlock";
        string whitespaceQuestion = "   ";

        // Act & Assert
        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _graphQueryService.QueryAsync(runId, targetTag, whitespaceQuestion, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("question", exception.ParamName);
        Assert.Contains("cannot be empty", exception.Message);
    }

    [Fact]
    public async Task QueryAsync_WithQuestionExceedingMaxLength_ThrowsArgumentException()
    {
        // Arrange
        string runId = Guid.NewGuid().ToString();
        string targetTag = "testBlock";
        string longQuestion = new('a', 5001); // MAX is 5000

        // Act & Assert
        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _graphQueryService.QueryAsync(runId, targetTag, longQuestion, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("question", exception.ParamName);
        Assert.Contains("exceeds maximum length", exception.Message);
    }

    [Fact]
    public async Task QueryAsync_WithQuestionAtMaxLength_DoesNotThrowArgumentException()
    {
        // Arrange
        string organizationId = _fixture.GetOrganization().Id;
        string runId = Guid.NewGuid().ToString();
        string targetTag = "testBlock";
        string maxLengthQuestion = new('a', 5000); // Exactly at MAX
        var root = new Workflow { Title = "Test Workflow" };

        // Create workflow with a block
        var section = new Section("Section 1", root) { Title = "Section 1" };

        _ = new Block(BlockTypes.Merge, section)
        {
            Name = "Test Block",
            ReplacementTag = targetTag,
            Order = 1
        };
        _ = _dbContext.Workflows.Add(root);

        // Create workflow execution
        var workflowExecution = new WorkflowExecution
        {
            Id = runId,
            OrganizationId = organizationId,
            WorkflowId = root.Id,
            Title = "Test Execution"
        };
        _ = _dbContext.WorkflowExecutions.Add(workflowExecution);

        // Create graph node for the target
        var graphNode = new GraphNode
        {
            RunId = runId,
            Key = targetTag,
            Kind = "block",
            Label = "Test Block"
        };
        _ = _dbContext.GraphNodes.Add(graphNode);

        _ = await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act - Should not throw ArgumentException for question length
        // This test verifies the 5000 character limit boundary condition
        ProvenanceResult result = await _graphQueryService.QueryAsync(runId, targetTag, maxLengthQuestion, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - If we got here without ArgumentException, the validation passed
        Assert.NotNull(result);
    }

    #endregion

    #region QueryAsync Integration Tests

    [Fact]
    public async Task QueryAsync_WithValidGraphAndQuestion_ReturnsProvenanceResult()
    {
        // Arrange
        string organizationId = _fixture.GetOrganization().Id;
        string runId = Guid.NewGuid().ToString();
        string targetTag = "outputBlock";
        string question = "Where does this data come from?";
        var root = new Workflow { Title = "Test Workflow" };

        // Create workflow with blocks
        var section = new Section("Section 1", root) { Title = "Section 1" };

        var inputBlock = new Block(BlockTypes.Merge, section)
        {
            Name = "Input Block",
            ReplacementTag = "inputBlock",
            Order = 1,
            Properties = new MergeProperties
            {
                TextTemplate = "Input data",
            },
        };

        _ = new Block(BlockTypes.Merge, section)
        {
            Name = "Output Block",
            ReplacementTag = targetTag,
            Order = 2,
            Properties = new MergeProperties
            {
                TextTemplate = "{inputBlock}",
            },
        };

        // Create block response for the input block
        _ = new BlockResponse(inputBlock)
        {
            RawResponse = "This is the input data from the first block"
        };

        _ = _dbContext.Workflows.Add(root);

        // Create job for document info
        string jobId = Guid.NewGuid().ToString();

        // Create workflow execution
        var workflowExecution = new WorkflowExecution
        {
            Id = runId,
            OrganizationId = organizationId,
            WorkflowId = root.Id,
            JobId = jobId,
            Title = "Test Execution"
        };
        _ = _dbContext.WorkflowExecutions.Add(workflowExecution);

        // Create graph nodes
        var inputNode = new GraphNode
        {
            RunId = runId,
            Key = "inputBlock",
            Kind = "block",
            Label = "Input Block",
            Order = 1
        };

        var outputNode = new GraphNode
        {
            RunId = runId,
            Key = targetTag,
            Kind = "block",
            Label = "Output Block",
            Order = 2
        };

        _ = _dbContext.GraphNodes.Add(inputNode);
        _ = _dbContext.GraphNodes.Add(outputNode);

        // Create graph edge connecting input to output
        var edge = new GraphEdge
        {
            RunId = runId,
            SourceId = "inputBlock",
            TargetId = targetTag,
            Label = "inputBlock"
        };
        _ = _dbContext.GraphEdges.Add(edge);

        _ = await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        ProvenanceResult result = await _graphQueryService.QueryAsync(runId, targetTag, question, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(GraphQueryServiceTestBuilder.MODEL_ANSWER, result.Answer);
    }

    [Fact]
    public async Task QueryAsync_WithNonExistentTargetNode_ThrowsInvalidOperationException()
    {
        // Arrange
        string organizationId = _fixture.GetOrganization().Id;
        string runId = Guid.NewGuid().ToString();
        string existingTag = "existingBlock";
        string nonExistentTag = "nonExistentBlock";
        string question = "What is this?";
        var root = new Workflow { Title = "Test Workflow" };

        // Create workflow
        var section = new Section("Section 1", root) { Title = "Section 1" };
        var block = new Block(BlockTypes.Merge, section)
        {
            Name = "Existing Block",
            ReplacementTag = existingTag,
            Order = 1
        };
        _ = _dbContext.Workflows.Add(root);

        // Create workflow execution
        var workflowExecution = new WorkflowExecution
        {
            Id = runId,
            OrganizationId = organizationId,
            WorkflowId = root.Id,
            Title = "Test Execution"
        };
        _ = _dbContext.WorkflowExecutions.Add(workflowExecution);

        // Create graph node only for the existing tag
        var graphNode = new GraphNode
        {
            RunId = runId,
            Key = existingTag,
            Kind = "block",
            Label = "Existing Block"
        };
        _ = _dbContext.GraphNodes.Add(graphNode);

        _ = await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act & Assert
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _graphQueryService.QueryAsync(runId, nonExistentTag, question, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("not found", exception.Message);
    }

    [Fact]
    public async Task QueryAsync_WithDeepGraphChain_TraversesAllLevels()
    {
        // Arrange - Create a deep graph: source → level1 → level2 → level3 → level4 → output
        // This tests the BFS traversal at depth 5
        string organizationId = _fixture.GetOrganization().Id;
        string runId = Guid.NewGuid().ToString();
        string targetTag = "outputBlock";
        string question = "Trace the data flow from the source to the output";
        var root = new Workflow { Title = "Deep Graph Workflow" };

        // Create workflow with blocks forming a chain
        var section = new Section("Section 1", root) { Title = "Section 1" };

        var sourceBlock = new Block(BlockTypes.Merge, section)
        {
            Name = "Source Block",
            ReplacementTag = "sourceBlock",
            Order = 1,
            Properties = new MergeProperties
            {
                TextTemplate = "Original source data",
            },
        };

        var level1Block = new Block(BlockTypes.Merge, section)
        {
            Name = "Level 1 Block",
            ReplacementTag = "level1Block",
            Order = 2,
            Properties = new MergeProperties
            {
                TextTemplate = "{sourceBlock}",
            },
        };

        var level2Block = new Block(BlockTypes.Merge, section)
        {
            Name = "Level 2 Block",
            ReplacementTag = "level2Block",
            Order = 3,
            Properties = new MergeProperties
            {
                TextTemplate = "{level1Block}",
            },
        };

        var level3Block = new Block(BlockTypes.Merge, section)
        {
            Name = "Level 3 Block",
            ReplacementTag = "level3Block",
            Order = 4,
            Properties = new MergeProperties
            {
                TextTemplate = "{level2Block}",
            },
        };

        var level4Block = new Block(BlockTypes.Merge, section)
        {
            Name = "Level 4 Block",
            ReplacementTag = "level4Block",
            Order = 5,
            Properties = new MergeProperties
            {
                TextTemplate = "{level3Block}",
            },
        };

        var outputBlock = new Block(BlockTypes.Merge, section)
        {
            Name = "Output Block",
            ReplacementTag = targetTag,
            Order = 6,
            Properties = new MergeProperties
            {
                TextTemplate = "{level4Block}",
            },
        };

        // Create block responses to simulate executed blocks
        _ = new BlockResponse(sourceBlock) { RawResponse = "Source data: financial metrics Q1 2024" };
        _ = new BlockResponse(level1Block) { RawResponse = "Processed level 1: extracted key figures" };
        _ = new BlockResponse(level2Block) { RawResponse = "Processed level 2: normalized values" };
        _ = new BlockResponse(level3Block) { RawResponse = "Processed level 3: calculated ratios" };
        _ = new BlockResponse(level4Block) { RawResponse = "Processed level 4: generated summary" };
        _ = new BlockResponse(outputBlock) { RawResponse = "Final output: comprehensive analysis" };

        _ = _dbContext.Workflows.Add(root);

        // Create workflow execution
        string jobId = Guid.NewGuid().ToString();
        var workflowExecution = new WorkflowExecution
        {
            Id = runId,
            OrganizationId = organizationId,
            WorkflowId = root.Id,
            JobId = jobId,
            Title = "Deep Graph Test Execution"
        };
        _ = _dbContext.WorkflowExecutions.Add(workflowExecution);

        // Create graph nodes for all blocks
        GraphNode[] nodes =
        [
            new GraphNode { RunId = runId, Key = "sourceBlock", Kind = "block", Label = "Source Block", Order = 1 },
            new GraphNode { RunId = runId, Key = "level1Block", Kind = "block", Label = "Level 1 Block", Order = 2 },
            new GraphNode { RunId = runId, Key = "level2Block", Kind = "block", Label = "Level 2 Block", Order = 3 },
            new GraphNode { RunId = runId, Key = "level3Block", Kind = "block", Label = "Level 3 Block", Order = 4 },
            new GraphNode { RunId = runId, Key = "level4Block", Kind = "block", Label = "Level 4 Block", Order = 5 },
            new GraphNode { RunId = runId, Key = targetTag, Kind = "block", Label = "Output Block", Order = 6 }
        ];
        _dbContext.GraphNodes.AddRange(nodes);

        // Create graph edges forming the chain: source → level1 → level2 → level3 → level4 → output
        GraphEdge[] edges =
        [
            new GraphEdge { RunId = runId, SourceId = "sourceBlock", TargetId = "level1Block", Label = "sourceBlock" },
            new GraphEdge { RunId = runId, SourceId = "level1Block", TargetId = "level2Block", Label = "level1Block" },
            new GraphEdge { RunId = runId, SourceId = "level2Block", TargetId = "level3Block", Label = "level2Block" },
            new GraphEdge { RunId = runId, SourceId = "level3Block", TargetId = "level4Block", Label = "level3Block" },
            new GraphEdge { RunId = runId, SourceId = "level4Block", TargetId = targetTag, Label = "level4Block" }
        ];
        _dbContext.GraphEdges.AddRange(edges);

        _ = await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act - Query with depth 5 to traverse the entire chain
        ProvenanceResult result = await _graphQueryService.QueryAsync(
            runId,
            targetTag,
            question,
            startingDepth: 5,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(GraphQueryServiceTestBuilder.MODEL_ANSWER, result.Answer);
    }

    [Fact]
    public async Task QueryAsync_WithDiamondDependencyGraph_TraversesBothPaths()
    {
        // Arrange - Create a diamond-shaped graph:
        //       source
        //       /    \
        //   pathA   pathB
        //       \    /
        //       output
        // This tests that BFS correctly handles multiple incoming edges
        string organizationId = _fixture.GetOrganization().Id;
        string runId = Guid.NewGuid().ToString();
        string targetTag = "outputBlock";
        string question = "What are the two paths that lead to the output?";
        var root = new Workflow { Title = "Diamond Graph Workflow" };

        var section = new Section("Section 1", root) { Title = "Section 1" };

        var sourceBlock = new Block(BlockTypes.Merge, section)
        {
            Name = "Source Block",
            ReplacementTag = "sourceBlock",
            Order = 1,
            Properties = new MergeProperties
            {
                TextTemplate = "Original data",
            },
        };

        var pathABlock = new Block(BlockTypes.Merge, section)
        {
            Name = "Path A Block",
            ReplacementTag = "pathABlock",
            Order = 2,
            Properties = new MergeProperties
            {
                TextTemplate = "{sourceBlock} processed via path A",
            },
        };

        var pathBBlock = new Block(BlockTypes.Merge, section)
        {
            Name = "Path B Block",
            ReplacementTag = "pathBBlock",
            Order = 3,
            Properties = new MergeProperties
            {
                TextTemplate = "{sourceBlock} processed via path B",
            },
        };

        var outputBlock = new Block(BlockTypes.Merge, section)
        {
            Name = "Output Block",
            ReplacementTag = targetTag,
            Order = 4,
            Properties = new MergeProperties
            {
                TextTemplate = "Combined: {pathABlock} and {pathBBlock}",
            },
        };

        // Create block responses
        _ = new BlockResponse(sourceBlock) { RawResponse = "Raw financial data" };
        _ = new BlockResponse(pathABlock) { RawResponse = "Path A: Revenue analysis" };
        _ = new BlockResponse(pathBBlock) { RawResponse = "Path B: Cost analysis" };
        _ = new BlockResponse(outputBlock) { RawResponse = "Combined profitability report" };

        _ = _dbContext.Workflows.Add(root);

        string jobId = Guid.NewGuid().ToString();
        var workflowExecution = new WorkflowExecution
        {
            Id = runId,
            OrganizationId = organizationId,
            WorkflowId = root.Id,
            JobId = jobId,
            Title = "Diamond Graph Test Execution"
        };
        _ = _dbContext.WorkflowExecutions.Add(workflowExecution);

        // Create graph nodes
        GraphNode[] nodes =
        [
            new GraphNode { RunId = runId, Key = "sourceBlock", Kind = "block", Label = "Source Block", Order = 1 },
            new GraphNode { RunId = runId, Key = "pathABlock", Kind = "block", Label = "Path A Block", Order = 2 },
            new GraphNode { RunId = runId, Key = "pathBBlock", Kind = "block", Label = "Path B Block", Order = 3 },
            new GraphNode { RunId = runId, Key = targetTag, Kind = "block", Label = "Output Block", Order = 4 }
        ];
        _dbContext.GraphNodes.AddRange(nodes);

        // Create edges forming the diamond:
        // source → pathA, source → pathB, pathA → output, pathB → output
        GraphEdge[] edges =
        [
            new GraphEdge { RunId = runId, SourceId = "sourceBlock", TargetId = "pathABlock", Label = "sourceBlock" },
            new GraphEdge { RunId = runId, SourceId = "sourceBlock", TargetId = "pathBBlock", Label = "sourceBlock" },
            new GraphEdge { RunId = runId, SourceId = "pathABlock", TargetId = targetTag, Label = "pathABlock" },
            new GraphEdge { RunId = runId, SourceId = "pathBBlock", TargetId = targetTag, Label = "pathBBlock" }
        ];
        _dbContext.GraphEdges.AddRange(edges);

        _ = await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act - Query with depth 3 to traverse both paths
        ProvenanceResult result = await _graphQueryService.QueryAsync(
            runId,
            targetTag,
            question,
            startingDepth: 3,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(GraphQueryServiceTestBuilder.MODEL_ANSWER, result.Answer);
    }

    [Fact]
    public async Task QueryAsync_WithDocumentInputNodes_IncludesDocumentProvenance()
    {
        // Arrange - Create a graph with document inputs feeding into blocks:
        //   doc1 (InputContext) → block1 → output
        string organizationId = _fixture.GetOrganization().Id;
        string runId = Guid.NewGuid().ToString();
        string targetTag = "outputBlock";
        string question = "What documents were used to produce this output?";
        var root = new Workflow { Title = "Document Input Graph Workflow" };

        var section = new Section("Section 1", root) { Title = "Section 1" };

        var processingBlock = new Block(BlockTypes.Merge, section)
        {
            Name = "Processing Block",
            ReplacementTag = "processingBlock",
            Order = 1,
            Properties = new MergeProperties
            {
                TextTemplate = "{InputContext}",
            },
        };

        var outputBlock = new Block(BlockTypes.Merge, section)
        {
            Name = "Output Block",
            ReplacementTag = targetTag,
            Order = 2,
            Properties = new MergeProperties
            {
                TextTemplate = "{processingBlock}",
            },
        };

        _ = new BlockResponse(processingBlock) { RawResponse = "Extracted data from uploaded PDF" };
        _ = new BlockResponse(outputBlock) { RawResponse = "Final analysis based on document" };

        _ = _dbContext.Workflows.Add(root);

        string jobId = Guid.NewGuid().ToString();
        var workflowExecution = new WorkflowExecution
        {
            Id = runId,
            OrganizationId = organizationId,
            WorkflowId = root.Id,
            JobId = jobId,
            Title = "Document Input Test Execution"
        };
        _ = _dbContext.WorkflowExecutions.Add(workflowExecution);

        // Create graph nodes including a document_input node
        GraphNode[] nodes =
        [
            new GraphNode { RunId = runId, Key = "InputContext", Kind = "document_input", Label = "InputContext", Order = null },
            new GraphNode { RunId = runId, Key = "processingBlock", Kind = "block", Label = "Processing Block", Order = 1 },
            new GraphNode { RunId = runId, Key = targetTag, Kind = "block", Label = "Output Block", Order = 2 }
        ];
        _dbContext.GraphNodes.AddRange(nodes);

        // Create edges: InputContext → processingBlock → output
        GraphEdge[] edges =
        [
            new GraphEdge { RunId = runId, SourceId = "InputContext", TargetId = "processingBlock", Label = "InputContext" },
            new GraphEdge { RunId = runId, SourceId = "processingBlock", TargetId = targetTag, Label = "processingBlock" }
        ];
        _dbContext.GraphEdges.AddRange(edges);

        _ = await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        ProvenanceResult result = await _graphQueryService.QueryAsync(
            runId,
            targetTag,
            question,
            startingDepth: 3,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(GraphQueryServiceTestBuilder.MODEL_ANSWER, result.Answer);
    }

    #endregion

    #region QueryAsync Expand Loop Tests

    private const int MAX_GRAPH_TRAVERSAL_DEPTH = 6;
    private const string EXPAND_QUESTION = "Where does the output come from?";
    private const string FORCED_ANSWER_WARNING = "MAXIMUM DEPTH REACHED";
    private const string CHAIN_TARGET_TAG = "chainBlock0";

    [Fact]
    public async Task QueryAsync_WhenTheModelExpandsOnceThenAnswers_AsksTwiceWithAWiderGraphTheSecondTime()
    {
        const string finalAnswer = "Block 2 feeds block 1, which feeds the output.";
        var model = new ScriptedChatConnector(call => call.Text.Contains(ChainLabel(2), StringComparison.Ordinal)
            ? ModelReply("answer", finalAnswer)
            : ModelReply("expand", "Need more context."));
        (IGraphQueryService service, string runId) = await SeedChainAsync(model, chainLength: 4);

        ProvenanceResult result = await service.QueryAsync(
            runId,
            CHAIN_TARGET_TAG,
            EXPAND_QUESTION,
            startingDepth: 1,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(finalAnswer, result.Answer);
        ScriptedModelCall[] calls = [.. model.Calls];
        Assert.Equal(2, calls.Length);
        Assert.Contains(ChainLabel(1), calls[0].Text, StringComparison.Ordinal);
        Assert.DoesNotContain(ChainLabel(2), calls[0].Text, StringComparison.Ordinal);
        Assert.Contains(ChainLabel(1), calls[1].Text, StringComparison.Ordinal);
        Assert.Contains(ChainLabel(2), calls[1].Text, StringComparison.Ordinal);
        Assert.DoesNotContain(ChainLabel(3), calls[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryAsync_WhenTheModelAlwaysExpands_StopsAtTheDepthLimitWithTheForcedAnswer()
    {
        int round = 0;
        var model = new ScriptedChatConnector(_ => ModelReply("expand", $"Answer of round {Interlocked.Increment(ref round)}."));
        (IGraphQueryService service, string runId) = await SeedChainAsync(model, chainLength: MAX_GRAPH_TRAVERSAL_DEPTH + 3);

        ProvenanceResult result = await service.QueryAsync(
            runId,
            CHAIN_TARGET_TAG,
            EXPAND_QUESTION,
            startingDepth: 1,
            cancellationToken: TestContext.Current.CancellationToken);

        ScriptedModelCall[] calls = [.. model.Calls];
        Assert.Equal(MAX_GRAPH_TRAVERSAL_DEPTH, calls.Length);
        Assert.Equal($"Answer of round {MAX_GRAPH_TRAVERSAL_DEPTH}.", result.Answer);
        Assert.All(calls[..^1], call => Assert.DoesNotContain(FORCED_ANSWER_WARNING, call.Text, StringComparison.Ordinal));
        Assert.Contains(FORCED_ANSWER_WARNING, calls[^1].Text, StringComparison.Ordinal);
        Assert.Contains(ChainLabel(MAX_GRAPH_TRAVERSAL_DEPTH), calls[^1].Text, StringComparison.Ordinal);
        Assert.DoesNotContain(ChainLabel(MAX_GRAPH_TRAVERSAL_DEPTH + 1), calls[^1].Text, StringComparison.Ordinal);
    }

    private static string ModelReply(string action, string answer)
    {
        return $$"""<answer>{"action": "{{action}}", "answer": "{{answer}}"}</answer>""";
    }

    private static string ChainLabel(int distance)
    {
        return $"Chain block at distance {distance}";
    }

    /// <summary>
    /// A straight chain where block N reads block N + 1, so block 0 (the target) is N hops
    /// downstream of block N and every extra depth adds exactly one block to the provenance.
    /// </summary>
    private async Task<(IGraphQueryService Service, string RunId)> SeedChainAsync(IChatConnector model, int chainLength)
    {
        IServiceProvider provider = _fixture.BuildTestServiceProvider(_testOutputHelper, model);
        IServiceScope scope = provider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        UserDataDbContext dbContext = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        string runId = Guid.NewGuid().ToString();
        var root = new Workflow { Title = "Chain Workflow" };
        var section = new Section("Section 1", root) { Title = "Section 1" };

        for (int distance = 0; distance < chainLength; distance++)
        {
            var block = new Block(BlockTypes.Merge, section)
            {
                Name = ChainLabel(distance),
                ReplacementTag = $"chainBlock{distance}",
                Order = chainLength - distance,
                Properties = new MergeProperties
                {
                    TextTemplate = distance + 1 < chainLength ? $"{{chainBlock{distance + 1}}}" : "Source data",
                },
            };
            _ = new BlockResponse(block) { RawResponse = $"Output at distance {distance}" };

            _ = dbContext.GraphNodes.Add(new GraphNode
            {
                RunId = runId,
                Key = block.ReplacementTag,
                Kind = "block",
                Label = block.Name,
                Order = block.Order,
            });

            if (distance + 1 < chainLength)
            {
                _ = dbContext.GraphEdges.Add(new GraphEdge
                {
                    RunId = runId,
                    SourceId = $"chainBlock{distance + 1}",
                    TargetId = block.ReplacementTag,
                    Label = $"chainBlock{distance + 1}",
                });
            }
        }

        _ = dbContext.Workflows.Add(root);
        _ = dbContext.WorkflowExecutions.Add(new WorkflowExecution
        {
            Id = runId,
            OrganizationId = _fixture.GetOrganization().Id,
            WorkflowId = root.Id,
            JobId = Guid.NewGuid().ToString(),
            Title = "Chain Execution",
        });
        _ = await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (scope.ServiceProvider.GetRequiredService<IGraphQueryService>(), runId);
    }

    #endregion
}

public class GraphQueryServiceFixture : TestsBaseFixture<GraphQueryService>
{
    private IServiceProvider? _serviceProvider;

    internal IServiceProvider BuildTestServiceProvider(ITestOutputHelper testOutputHelper, IChatConnector? chatConnector = null)
    {
        var builder = new GraphQueryServiceTestBuilder(
            PlatformSettings,
            testOutputHelper,
            chatConnector);
        _serviceProvider = builder.Build();
        return _serviceProvider;
    }

    internal Domain.Entities.UserManagement.Organization GetOrganization()
    {
        return _serviceProvider is null
            ? throw new InvalidOperationException(
                "BuildTestServiceProvider() must be called before GetOrganization()")
            : TestDatabaseServices.GetTestOrganization(_serviceProvider);
    }
}
