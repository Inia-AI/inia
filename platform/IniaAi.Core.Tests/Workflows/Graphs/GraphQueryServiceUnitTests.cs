using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.AwsBedrock;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Core.Workflows.Graph;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Domain.Graph;
using IniaAi.Domain.Persistence.Core.Repositories;
using IniaAi.Domain.Persistence.UserData.Repositories;
using IniaAi.Domain.Tools.GraphRag;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Common.Enums;
using Microsoft.Extensions.Logging;
using NSubstitute;
using IniaAi.Common.Interfaces.Services;

namespace IniaAi.Core.Tests.Workflows.Graphs;

/// <summary>
/// Unit tests for GraphQueryService using NSubstitute mocks.
/// These tests verify BFS traversal, agent loop behavior, and edge cases
/// without requiring a real database or LLM.
/// </summary>
public class GraphQueryServiceUnitTests
{
    private const string RunId = "run-1";
    private const string WorkflowId = "wf-1";
    private const string OrgId = "org-1";
    private const string JobId = "job-1";

    private readonly IGraphRepository _graphRepository;
    private readonly IGraphBuilderService _graphBuilderService;
    private readonly IUserDataUnitOfWork _uow;
    private readonly IWorkflowExecutionRepository _executionRepo;
    private readonly IUserDataWorkflowRepository _workflowRepo;
    private readonly IChatConnector _chatConnector;
    private readonly IToolsSettingsRepository _toolsSettings;
    private readonly GraphQueryService _sut;

    public GraphQueryServiceUnitTests()
    {
        _graphRepository = Substitute.For<IGraphRepository>();
        _graphBuilderService = Substitute.For<IGraphBuilderService>();
        _uow = Substitute.For<IUserDataUnitOfWork>();
        _executionRepo = Substitute.For<IWorkflowExecutionRepository>();
        _workflowRepo = Substitute.For<IUserDataWorkflowRepository>();
        _chatConnector = Substitute.For<IChatConnector>();
        _toolsSettings = Substitute.For<IToolsSettingsRepository>();

        _ = _uow.WorkflowExecutions.Returns(_executionRepo);
        _ = _uow.Workflows.Returns(_workflowRepo);
        _ = _toolsSettings.GetGraphRagPromptsAsync().Returns(new GraphRagPrompts());

        // Default: no documents uploaded
        IRepository<DocumentInfo> docRepo = Substitute.For<IRepository<DocumentInfo>>();
        _ = docRepo.FindAsync(Arg.Any<System.Linq.Expressions.Expression<Func<DocumentInfo, bool>>>())
            .Returns([]);
        _ = _uow.Repository<DocumentInfo>().Returns(docRepo);

        // Default: no block responses
        IRepository<BlockResponse> blockResponseRepo = Substitute.For<IRepository<BlockResponse>>();
        _ = blockResponseRepo
            .GetByIdAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns((BlockResponse?)null);
        _ = _uow.Repository<BlockResponse>().Returns(blockResponseRepo);

        _sut = new GraphQueryService(
            _graphRepository,
            _graphBuilderService,
            _uow,
            _chatConnector,
            _toolsSettings,
            Substitute.For<ILogger<GraphQueryService>>(),
            new RequestModelUse());
    }

    #region Helpers

    private static WorkflowExecution MakeExecution()
    {
        return new()
        {
            Id = RunId,
            OrganizationId = OrgId,
            WorkflowId = WorkflowId,
            JobId = JobId,
            Title = "Unit Test Execution"
        };
    }

    private static GraphNode MakeNode(string key, string kind = "block", int? order = null)
    {
        return new() { RunId = RunId, Key = key, Kind = kind, Label = key, Order = order };
    }

    private static GraphEdge MakeEdge(string source, string target)
    {
        return new() { RunId = RunId, SourceId = source, TargetId = target, Label = source };
    }

    private static ResponseContentSequence MakeLlmResponse(string action, string answer, string? reason = null)
    {
        string reasonPart = reason is not null ? $""","reason":"{reason}" """ : "";
        string json = $$"""{"action":"{{action}}","answer":"{{answer}}"{{reasonPart}}}""";
        var response = new ResponseContentSequence();
        response.Add(new ResponseCommonContent($"<answer>\n{json}\n</answer>"));
        return response;
    }

    /// <summary>
    /// Sets up the common mocks needed for QueryAsync to reach the BFS + LLM stage.
    /// </summary>
    private void SetupGraphAndExecution(List<GraphNode> nodes, List<GraphEdge> edges)
    {
        // GraphBuilderService returns a WorkflowGraph with the given nodes
        var graph = new WorkflowGraph();
        foreach (GraphNode node in nodes)
        {
            graph.Nodes.Add(new WorkflowGraphNode { Id = node.Key, Kind = node.Kind });
        }

        _ = _graphBuilderService
            .GetOrBuildGraphAsync(RunId, Arg.Any<CancellationToken>())
            .Returns(graph);

        // GraphRepository returns the raw nodes/edges for BFS
        _ = _graphRepository.GetNodesAsync(RunId).Returns(nodes);
        _ = _graphRepository.GetEdgesAsync(RunId).Returns(edges);

        // Execution lookup
        _ = _executionRepo
            .GetByIdAsync(RunId, Arg.Any<CancellationToken>())
            .Returns(MakeExecution());

        // Empty block lookup (no executed blocks — BFS still visits nodes)
        _ = _workflowRepo
            .GetAllBlocksByWorkflowIdAsync(WorkflowId, Arg.Any<CancellationToken>())
            .Returns([]);
    }

    private void SetupLlmResponse(string action, string answer, string? reason = null)
    {
        _ = _chatConnector
            .GetResponseAsync(
                Arg.Any<RequestContentSequence>(),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<string?>(),
                Arg.Any<ModelFamily?>(),
                Arg.Any<ModelEffort?>(),
                Arg.Any<CancellationToken>())
            .Returns(MakeLlmResponse(action, answer, reason));
    }

    #endregion

    #region Uploaded class filter

    [Fact]
    public async Task QueryAsync_ReadsTheUploadedClassesFromTheRunsInputOnly()
    {
        System.Linq.Expressions.Expression<Func<DocumentInfo, bool>>? predicate = null;
        IRepository<DocumentInfo> docRepo = Substitute.For<IRepository<DocumentInfo>>();
        _ = docRepo.FindAsync(Arg.Do<System.Linq.Expressions.Expression<Func<DocumentInfo, bool>>>(p => predicate = p))
            .Returns([]);
        _ = _uow.Repository<DocumentInfo>().Returns(docRepo);
        SetupGraphAndExecution([MakeNode("blockA")], []);
        SetupLlmResponse("answer", "No upstream.");
        var input = new DocumentInfo("document-kept") { JobId = JobId, DocumentClass = "Statement" };
        var dropped = new DocumentInfo("document-dropped") { JobId = JobId, DocumentClass = "Photo" };
        dropped.ExcludeFromRun("NotExpectedDocument", DateTime.UtcNow);

        _ = await _sut.QueryAsync(RunId, "blockA", "Where does this come from?", cancellationToken: TestContext.Current.CancellationToken);

        Func<DocumentInfo, bool> readsAsInput = predicate!.Compile();
        Assert.True(readsAsInput(input));
        Assert.False(readsAsInput(dropped));
    }

    #endregion

    #region BFS Traversal Tests

    [Fact]
    public async Task QueryAsync_SingleNode_NoEdges_ReturnsAnswer()
    {
        // Arrange — single node, no incoming edges
        string targetTag = "blockA";
        var nodes = new List<GraphNode> { MakeNode(targetTag) };
        var edges = new List<GraphEdge>();

        SetupGraphAndExecution(nodes, edges);
        SetupLlmResponse("answer", "BlockA has no upstream dependencies.");

        // Act
        ProvenanceResult result = await _sut.QueryAsync(RunId, targetTag, "Where does this come from?", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("BlockA has no upstream dependencies.", result.Answer);
    }

    [Fact]
    public async Task QueryAsync_LinearChain_TraversesAllNodes()
    {
        // Arrange — chain: A → B → C, query C at depth 3
        string targetTag = "C";
        var nodes = new List<GraphNode>
        {
            MakeNode("A", order: 1),
            MakeNode("B", order: 2),
            MakeNode("C", order: 3),
        };
        var edges = new List<GraphEdge>
        {
            MakeEdge("A", "B"),
            MakeEdge("B", "C"),
        };

        SetupGraphAndExecution(nodes, edges);
        SetupLlmResponse("answer", "Data flows from A through B to C.");

        // Act
        ProvenanceResult result = await _sut.QueryAsync(RunId, targetTag, "Trace the data flow", startingDepth: 3, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Data flows from A through B to C.", result.Answer);
    }

    [Fact]
    public async Task QueryAsync_DiamondGraph_TraversesBothPaths()
    {
        // Arrange — diamond: A→B, A→C, B→D, C→D
        string targetTag = "D";
        var nodes = new List<GraphNode>
        {
            MakeNode("A", order: 1),
            MakeNode("B", order: 2),
            MakeNode("C", order: 3),
            MakeNode("D", order: 4),
        };
        var edges = new List<GraphEdge>
        {
            MakeEdge("A", "B"),
            MakeEdge("A", "C"),
            MakeEdge("B", "D"),
            MakeEdge("C", "D"),
        };

        SetupGraphAndExecution(nodes, edges);
        SetupLlmResponse("answer", "D receives data from both B and C paths.");

        // Act
        ProvenanceResult result = await _sut.QueryAsync(RunId, targetTag, "What are the sources?", startingDepth: 3, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("D receives data from both B and C paths.", result.Answer);
    }

    [Fact]
    public async Task QueryAsync_DepthLimiting_DoesNotTraverseBeyondDepth()
    {
        // Arrange — chain A→B→C→target, query target at depth 1
        // At depth 1, BFS only reaches C (target=0, C=1, stops)
        string targetTag = "target";
        var nodes = new List<GraphNode>
        {
            MakeNode("A", order: 1),
            MakeNode("B", order: 2),
            MakeNode("C", order: 3),
            MakeNode("target", order: 4),
        };
        var edges = new List<GraphEdge>
        {
            MakeEdge("A", "B"),
            MakeEdge("B", "C"),
            MakeEdge("C", "target"),
        };

        SetupGraphAndExecution(nodes, edges);
        SetupLlmResponse("answer", "Only C is visible at this depth.");

        // Act — depth 1 should only reach the immediate parent
        ProvenanceResult result = await _sut.QueryAsync(RunId, targetTag, "What is the source?", startingDepth: 1, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Only C is visible at this depth.", result.Answer);

        // Verify LLM was called exactly once (no expand loop)
        _ = await _chatConnector.Received(1).GetResponseAsync(
            Arg.Any<RequestContentSequence>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<bool>(),
            Arg.Any<string?>(),
            Arg.Any<ModelFamily?>(),
            Arg.Any<ModelEffort?>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Agent Loop Tests

    [Fact]
    public async Task QueryAsync_LlmReturnsExpandThenAnswer_MakesTwoCalls()
    {
        // Arrange
        string targetTag = "blockA";
        SetupGraphAndExecution(
            [MakeNode(targetTag)],
            []);

        int callCount = 0;
        _ = _chatConnector
            .GetResponseAsync(
                Arg.Any<RequestContentSequence>(),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<string?>(),
                Arg.Any<ModelFamily?>(),
                Arg.Any<ModelEffort?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                return callCount == 1
                    ? MakeLlmResponse("expand", "", "Need more context")
                    : MakeLlmResponse("answer", "Found the source after expanding.");
            });

        // Act
        ProvenanceResult result = await _sut.QueryAsync(RunId, targetTag, "Where?", startingDepth: 1, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, callCount);
        Assert.Equal("Found the source after expanding.", result.Answer);
    }

    [Fact]
    public async Task QueryAsync_UnknownAction_TreatedAsAnswer_SingleLlmCall()
    {
        // Arrange
        string targetTag = "blockA";
        SetupGraphAndExecution(
            [MakeNode(targetTag)],
            []);

        SetupLlmResponse("summarize", "Here is a summary.");

        // Act
        ProvenanceResult result = await _sut.QueryAsync(RunId, targetTag, "Where?", cancellationToken: TestContext.Current.CancellationToken);

        // Assert — only one LLM call, no extra fallthrough call
        Assert.Equal("Here is a summary.", result.Answer);
        _ = await _chatConnector.Received(1).GetResponseAsync(
            Arg.Any<RequestContentSequence>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<bool>(),
            Arg.Any<string?>(),
            Arg.Any<ModelFamily?>(),
            Arg.Any<ModelEffort?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueryAsync_UnknownActionWithEmptyAnswer_ReturnsFallbackMessage()
    {
        // Arrange
        string targetTag = "blockA";
        SetupGraphAndExecution(
            [MakeNode(targetTag)],
            []);

        SetupLlmResponse("summarize", "");

        // Act
        ProvenanceResult result = await _sut.QueryAsync(RunId, targetTag, "Where?", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "Unable to determine the answer. Please try rephrasing your question.",
            result.Answer);
    }

    [Fact]
    public async Task QueryAsync_MaxDepth_ForcesAnswerOnFirstIteration()
    {
        // Arrange — start at max depth (6), LLM returns expand but isMaxDepth is true
        string targetTag = "blockA";
        SetupGraphAndExecution(
            [MakeNode(targetTag)],
            []);

        // Even though the LLM says expand, isMaxDepth=true forces returning the answer
        SetupLlmResponse("expand", "Partial answer at max depth.", "Always need more");

        // Act
        ProvenanceResult result = await _sut.QueryAsync(RunId, targetTag, "Where?", startingDepth: 6, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — only one call because isMaxDepth is true immediately
        // The real answer is preserved (not overwritten with "Expanding search: ...")
        Assert.Equal("Partial answer at max depth.", result.Answer);
        _ = await _chatConnector.Received(1).GetResponseAsync(
            Arg.Any<RequestContentSequence>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<bool>(),
            Arg.Any<string?>(),
            Arg.Any<ModelFamily?>(),
            Arg.Any<ModelEffort?>(),
            Arg.Any<CancellationToken>());
    }

    #endregion
}
