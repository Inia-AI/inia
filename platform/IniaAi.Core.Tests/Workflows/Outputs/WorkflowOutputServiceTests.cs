using IniaAi.Core.Services;
using IniaAi.Core.Services.Abstraction;
using IniaAi.Core.Workflows.Annotations;
using IniaAi.Core.Workflows.Outputs;
using IniaAi.Domain.Constants;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Domain.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Core.Tests.Workflows.Outputs;

public class WorkflowOutputServiceTests : IDisposable
{
    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly IServiceScope _scope;
    private readonly IUserDataUnitOfWork _uow;
    private readonly WorkflowOutputService _sut;

    public WorkflowOutputServiceTests()
    {
        var services = new ServiceCollection();
        string databaseName = $"WorkflowOutputServiceTests-{Guid.NewGuid()}";

        _ = services.AddDbContext<UserDataDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));

        _ = services.AddDbContextFactory<UserDataDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));

        _ = services.AddTestDatabaseServicesWithoutDbContext();

        _serviceProvider = services.BuildServiceProvider();
        _scope = _serviceProvider.CreateScope();
        _uow = _scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();

        IExchangeRateService exchangeRateService = new MockExchangeRateService();
        var builder = new WorkflowOutputBuilder();
        var validator = new WorkflowOutputValidator();

        _sut = new WorkflowOutputService(
            builder,
            validator,
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WorkflowOutputService>.Instance);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _scope.Dispose();
        _serviceProvider.Dispose();
    }

    #region CreateExecutionFromWorkflowAsync

    [Fact]
    public async Task CreateExecutionFromWorkflowAsync_SetsWorkflowIdToSelfReference()
    {
        // Arrange
        var workflow = new Workflow { Title = "Test Workflow" };

        // Act
        WorkflowExecution result = await _sut.CreateExecutionFromWorkflowAsync(
            workflow, null, _uow);

        // Assert
        Assert.Equal(result.Id, result.WorkflowId);
    }

    [Fact]
    public async Task CreateExecutionFromWorkflowAsync_PopulatesFromContext()
    {
        // Arrange
        var workflow = new Workflow { Title = "Test" };
        var context = new WorkflowOutputContext
        {
            CurrentUserId = "user-1",
            CurrentOrganizationId = "org-1",
            CurrentCounterparty = new Counterparty { Id = "cp-1" },
        };

        // Act
        WorkflowExecution result = await _sut.CreateExecutionFromWorkflowAsync(
            workflow, context, _uow, jobId: "job-1", workflowState: WorkflowState.Running);

        // Assert
        Assert.Equal("user-1", result.UserId);
        Assert.Equal("org-1", result.OrganizationId);
        Assert.Equal("cp-1", result.CounterpartyId);
        Assert.Equal("job-1", result.JobId);
        Assert.Equal(WorkflowState.Running, result.WorkflowState);
    }

    [Fact]
    public async Task CreateExecutionFromWorkflowAsync_DefaultsStateToUndefined()
    {
        // Arrange
        var workflow = new Workflow();

        // Act
        WorkflowExecution result = await _sut.CreateExecutionFromWorkflowAsync(
            workflow, null, _uow);

        // Assert
        Assert.Equal(WorkflowState.Undefined, result.WorkflowState);
    }

    [Fact]
    public async Task CreateExecutionFromWorkflowAsync_SetsRootElementIdOnContext()
    {
        // Arrange
        var workflow = new Workflow();
        var context = new WorkflowOutputContext();

        // Act
        WorkflowExecution result = await _sut.CreateExecutionFromWorkflowAsync(
            workflow, context, _uow);

        // Assert
        Assert.Equal(result.Id, context.RootElementId);
        Assert.Same(result, context.CurrentWorkflowOutputRoot);
    }

    [Fact]
    public async Task CreateExecutionFromWorkflowAsync_PersistsToDatabase()
    {
        // Arrange
        var workflow = new Workflow { Title = "Persisted Workflow" };

        // Act
        WorkflowExecution result = await _sut.CreateExecutionFromWorkflowAsync(
            workflow, null, _uow);

        // Assert — verify entity was actually persisted
        WorkflowExecution? persisted = await _uow.WorkflowExecutions
            .SingleOrDefaultAsync(predicate: x => x.Id == result.Id,
                cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(persisted);
        Assert.Equal("Persisted Workflow", persisted.Title);
    }

    [Fact]
    public async Task CreateExecutionFromWorkflowAsync_MapsPeerCompanies()
    {
        // Arrange
        var workflow = new Workflow();
        var context = new WorkflowOutputContext
        {
            CurrentPeerCompanies =
            [
                new Counterparty { Id = "peer-1" },
                new Counterparty { Id = "peer-2" },
            ],
        };

        // Act
        WorkflowExecution result = await _sut.CreateExecutionFromWorkflowAsync(
            workflow, context, _uow);

        // Assert
        Assert.Equal(2, result.WorkflowExecutionsCounterparties.Count);
        Assert.Contains(result.WorkflowExecutionsCounterparties,
            c => c.CounterpartyId == "peer-1");
        Assert.Contains(result.WorkflowExecutionsCounterparties,
            c => c.CounterpartyId == "peer-2");
    }

    #endregion

    #region BuildReportParagraphFromBlock

    [Fact]
    public async Task BuildReportParagraphFromBlock_WithStopTerm_ReturnsNullParagraphWithReason()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow);
        var textInput = new TextInput(block, "tag", "value");
        textInput.ExecutionState = new TextInputExecutionState(textInput)
        {
            Value = $"contains {StopMarker.VALUE} here"
        };

        // Act
        (ReportParagraph? paragraph, string? reason, bool isValidForOutput) =
            await _sut.BuildReportParagraphFromBlock(block, null, null);

        // Assert
        Assert.Null(paragraph);
        Assert.False(isValidForOutput);
        Assert.Contains("Stop term", reason);
    }

    [Fact]
    public async Task BuildReportParagraphFromBlock_WithStopTermInsideAnId_KeepsTheParagraph()
    {
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow) { IsOutputSource = true };
        block.BlockResponse = new BlockResponse(block) { RawResponse = "AI output content" };
        var textInput = new TextInput(block, "tag", "value");
        textInput.ExecutionState = new TextInputExecutionState(textInput)
        {
            Value = "Workflow id ff99747a-eb9b-40eb-af11-be19f117a74c"
        };

        (ReportParagraph? paragraph, string? reason, bool isValidForOutput) =
            await _sut.BuildReportParagraphFromBlock(block, null, null);

        Assert.True(isValidForOutput);
        Assert.NotNull(paragraph);
        Assert.Null(reason);
    }

    [Fact]
    public async Task BuildReportParagraphFromBlock_InvalidBlock_ReturnsNullParagraphNoReason()
    {
        // Arrange — block with IsOutputSource=false (default) is invalid
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow);

        // Act
        (ReportParagraph? paragraph, string? reason, bool isValidForOutput) =
            await _sut.BuildReportParagraphFromBlock(block, null, null);

        // Assert
        Assert.Null(paragraph);
        Assert.False(isValidForOutput);
        Assert.Null(reason);
    }

    [Fact]
    public async Task BuildReportParagraphFromBlock_ValidBlock_ReturnsBuiltParagraph()
    {
        // Arrange — block must have IsOutputSource=true and a non-empty BlockResponse
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow) { IsOutputSource = true };
        block.BlockResponse = new BlockResponse(block) { RawResponse = "AI output content" };

        // Act
        (ReportParagraph? paragraph, string? reason, bool isValidForOutput) =
            await _sut.BuildReportParagraphFromBlock(block, null, null);

        // Assert
        Assert.True(isValidForOutput);
        Assert.NotNull(paragraph);
        Assert.Null(reason);
    }

    #endregion

    #region CreateReportParagraphAsync

    [Fact]
    public async Task CreateReportParagraphAsync_PersistsAndReturnsParagraph()
    {
        // Arrange
        var paragraph = new ReportParagraph { SectionId = "sec-1", Title = "Test" };

        // Act
        ReportParagraph result = await _sut.CreateReportParagraphAsync(paragraph, _uow);

        // Assert — verify the paragraph was persisted
        Assert.Same(paragraph, result);
        ReportParagraph? persisted = await _uow.Repository<ReportParagraph>()
            .GetByIdAsync(paragraph.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(persisted);
    }

    #endregion

    #region CreateErrorOutputAsync

    [Fact]
    public async Task CreateErrorOutputAsync_CreatesReportSectionAndParagraph()
    {
        // Arrange
        string executionId = Guid.NewGuid().ToString();
        string errorMessage = "Fund manager not found";

        // Act
        await _sut.CreateErrorOutputAsync(executionId, errorMessage, _uow);

        // Assert — the report tree carries the error section + paragraph
        IEnumerable<ReportSection> sections = await _uow.Repository<ReportSection>()
            .FindAsync(s => s.WorkflowReportId == executionId);
        ReportSection section = Assert.Single(sections);
        Assert.Equal("ERROR", section.Title);

        IEnumerable<ReportParagraph> paragraphs = await _uow.Repository<ReportParagraph>()
            .FindAsync(p => p.SectionId == section.Id);
        ReportParagraph paragraph = Assert.Single(paragraphs);
        Assert.Equal("ERROR", paragraph.Title);
        Assert.Equal(errorMessage, paragraph.Content);
        Assert.Equal(ReportParagraphKind.Paragraph, paragraph.Kind);
    }

    #endregion

    #region GetWorkflowExecution

    [Fact]
    public async Task GetWorkflowExecution_ReturnsResult()
    {
        // Arrange — seed a real execution
        var workflow = new Workflow { Title = "Get Test" };
        WorkflowExecution seeded = await _sut.CreateExecutionFromWorkflowAsync(
            workflow, null, _uow);

        // Act
        WorkflowExecution? result = await _sut.GetWorkflowExecution(seeded.Id, _uow);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(seeded.Id, result.Id);
    }

    [Fact]
    public async Task GetWorkflowExecution_ReturnsNullWhenNotFound()
    {
        // Act
        WorkflowExecution? result = await _sut.GetWorkflowExecution("missing-id", _uow);

        // Assert
        Assert.Null(result);
    }

    #endregion
}
