using IniaAi.Core.Workflows.Graph;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Persistence.UserData.Repositories;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace IniaAi.Core.Tests.Workflows.Graphs;

public class GraphBuilderServiceTests
{
    private readonly IGraphRepository _graphRepository;
    private readonly IWorkflowGraphBuilder _graphBuilder;
    private readonly IUserDataUnitOfWork _uow;
    private readonly IWorkflowExecutionRepository _executionRepo;
    private readonly IUserDataWorkflowRepository _workflowRepo;
    private readonly GraphBuilderService _sut;

    public GraphBuilderServiceTests()
    {
        _graphRepository = Substitute.For<IGraphRepository>();
        _graphBuilder = Substitute.For<IWorkflowGraphBuilder>();
        _uow = Substitute.For<IUserDataUnitOfWork>();
        _executionRepo = Substitute.For<IWorkflowExecutionRepository>();
        _workflowRepo = Substitute.For<IUserDataWorkflowRepository>();

        _ = _uow.WorkflowExecutions.Returns(_executionRepo);
        _ = _uow.Workflows.Returns(_workflowRepo);

        _sut = new GraphBuilderService(
            _graphRepository,
            _graphBuilder,
            _uow,
            Substitute.For<ILogger<GraphBuilderService>>()
        );
    }

    [Fact]
    public async Task GetOrBuildGraphAsync_WhenGraphExists_ReturnsCachedWithoutBuilding()
    {
        // Arrange
        var existingGraph = new WorkflowGraph();
        existingGraph.Nodes.Add(new WorkflowGraphNode { Id = "A", Kind = "block" });

        _ = _graphRepository.GetGraphAsync("run-1").Returns(existingGraph);

        // Act
        WorkflowGraph result = await _sut.GetOrBuildGraphAsync("run-1", TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(existingGraph, result);
        _ = _graphBuilder
            .DidNotReceive()
            .Build(Arg.Any<Dictionary<int, List<Block>>>(), Arg.Any<List<WorkflowVariable>>());
    }

    [Fact]
    public async Task GetOrBuildGraphAsync_WhenExecutionNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        _ = _graphRepository.GetGraphAsync(Arg.Any<string>()).Returns((WorkflowGraph?)null);
        _ = _executionRepo
            .GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((WorkflowExecution?)null);

        // Act & Assert
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.GetOrBuildGraphAsync("run-missing", TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task GetOrBuildGraphAsync_WhenWorkflowIdIsNull_ThrowsInvalidOperationException()
    {
        // Arrange
        var execution = new WorkflowExecution
        {
            Id = "run-1",
            OrganizationId = "org-1",
            WorkflowId = null,
            Title = "Test",
        };

        _ = _graphRepository.GetGraphAsync(Arg.Any<string>()).Returns((WorkflowGraph?)null);
        _ = _executionRepo
            .GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(execution);

        // Act & Assert
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.GetOrBuildGraphAsync("run-1", TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task GetOrBuildGraphAsync_WhenDuplicateKeyOnSave_RetrievesExistingGraph()
    {
        // Arrange
        var savedGraph = new WorkflowGraph();
        savedGraph.Nodes.Add(new WorkflowGraphNode { Id = "A", Kind = "block" });

        var execution = new WorkflowExecution
        {
            Id = "run-1",
            OrganizationId = "org-1",
            WorkflowId = "wf-1",
            Title = "Test",
        };

        // First call returns null (not cached), second returns the saved graph
        _ = _graphRepository
            .GetGraphAsync("run-1")
            .Returns(null, null, savedGraph);

        _ = _executionRepo
            .GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(execution);

        _ = _workflowRepo
            .GetAllBlocksByWorkflowIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([]);

        _ = _workflowRepo
            .GetAllVariablesByWorkflowIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([]);

        _ = _graphBuilder
            .Build(Arg.Any<Dictionary<int, List<Block>>>(), Arg.Any<List<WorkflowVariable>>())
            .Returns(new WorkflowGraph());

        // SaveGraphAsync throws duplicate key exception (simulating race condition)
        _ = _graphRepository
            .SaveGraphAsync(Arg.Any<string>(), Arg.Any<WorkflowGraph>())
            .ThrowsAsync(new Exception("duplicate key value violates unique constraint (23505)"));

        // Act
        WorkflowGraph result = await _sut.GetOrBuildGraphAsync("run-1", TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(savedGraph, result);
    }
}
