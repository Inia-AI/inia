using ErrorOr;
using IniaAi.Application.Queries.AgentRuns.ExecutionAgentRunFiles;
using IniaAi.Application.Queries.ExecutedWorkflows.DownloadExecutedWorkflow;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses.AgentRuns;
using IniaAi.WebApi.Contracts.Responses.ExecutedWorkflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using static IniaAi.Application.UnitTests.Queries.AgentRuns.ExecutionAgentRunFiles.RunEvidenceTestData;

namespace IniaAi.Application.UnitTests.Queries.AgentRuns.ExecutionAgentRunFiles;

public class ExecutionAgentRunFilesQueryHandlerTests : IDisposable
{
    private static readonly DateTime _earlier = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime _later = new(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly IUserDataUnitOfWork _unitOfWork;

    public ExecutionAgentRunFilesQueryHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();
        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();
        _unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
    }

    private Task<ErrorOr<List<AgentRunFileDto>>> ListAsync(string organizationId, string userId, string executionId, bool staff = false)
    {
        return new ListExecutionAgentRunFilesQueryHandler(_unitOfWork).Handle(
            new ListExecutionAgentRunFilesQuery(organizationId, userId, executionId) { CallerReadsEveryRun = staff },
            TestContext.Current.CancellationToken);
    }

    private Task<ErrorOr<AgentRunFileContent>> GetAsync(
        string userId, string executionId, string blockId, string path, string? runExecutionId = null, int? attempt = null, string organizationId = ORG_ID)
    {
        return new GetExecutionAgentRunFileQueryHandler(_unitOfWork).Handle(
            new GetExecutionAgentRunFileQuery(organizationId, userId, executionId, blockId, path, runExecutionId, attempt),
            TestContext.Current.CancellationToken);
    }

    private async Task<SeededRun> SeedTracedRunAsync(string startedBy = SERVICE_USER_ID, string organizationId = ORG_ID)
    {
        await SeedUsersAsync(_context);
        SeededRun run = await SeedRunAsync(_context, organizationId, startedBy);
        await SeedTraceAsync(_context, organizationId, run.Agent.Id, "run-1", 1, TRACE_PATH, "{\"pass\":1}", _earlier);
        await SeedTraceAsync(_context, organizationId, run.Agent.Id, "run-2", 1, TRACE_PATH, "{\"pass\":2}", _later);
        await SeedTraceAsync(_context, organizationId, run.Agent.Id, "run-2", 1, ARTIFACT_PATH, "{\"answer\":42}", _later);
        return run;
    }

    [Fact]
    public async Task List_ByTheServiceUserThatStartedTheRun_ReturnsItsFilesWithoutBytes()
    {
        SeededRun run = await SeedTracedRunAsync();
        SeededRun otherRun = await SeedRunAsync(_context, ORG_ID, SERVICE_USER_ID);
        await SeedTraceAsync(_context, ORG_ID, otherRun.Agent.Id, "run-x", 1, TRACE_PATH, "other", _later);

        ErrorOr<List<AgentRunFileDto>> result = await ListAsync(ORG_ID, SERVICE_USER_ID, run.Execution.Id);

        Assert.False(result.IsError);
        Assert.Equal(3, result.Value.Count);
        Assert.All(result.Value, file => Assert.Equal(run.Agent.Id, file.BlockId));
        Assert.All(result.Value, file => Assert.Equal("agent_tag", file.ReplacementTag));
        Assert.DoesNotContain(result.Value, file => file.RunExecutionId == "run-x");
    }

    [Fact]
    public async Task List_ByAServiceUserThatDidNotStartTheRun_IsForbidden()
    {
        SeededRun run = await SeedTracedRunAsync(startedBy: ANALYST_ID);

        ErrorOr<List<AgentRunFileDto>> result = await ListAsync(ORG_ID, SERVICE_USER_ID, run.Execution.Id);

        Assert.Equal(ErrorType.Forbidden, result.FirstError.Type);
    }

    [Fact]
    public async Task List_ByAnAnalystWhoStartedTheRun_IsForbidden()
    {
        SeededRun run = await SeedTracedRunAsync(startedBy: ANALYST_ID);

        ErrorOr<List<AgentRunFileDto>> result = await ListAsync(ORG_ID, ANALYST_ID, run.Execution.Id);

        Assert.Equal(ErrorType.Forbidden, result.FirstError.Type);
    }

    [Fact]
    public async Task List_ByAStaffRole_ReadsAnyRunInTheOrganization()
    {
        SeededRun run = await SeedTracedRunAsync(startedBy: ANALYST_ID);

        ErrorOr<List<AgentRunFileDto>> result = await ListAsync(ORG_ID, "operator-user", run.Execution.Id, staff: true);

        Assert.False(result.IsError);
        Assert.Equal(3, result.Value.Count);
    }

    [Fact]
    public async Task List_FromAnotherOrganization_IsNotFound_EvenForAStaffRole()
    {
        SeededRun run = await SeedTracedRunAsync();

        ErrorOr<List<AgentRunFileDto>> asServiceUser = await ListAsync(OTHER_ORG_ID, OTHER_SERVICE_USER_ID, run.Execution.Id);
        ErrorOr<List<AgentRunFileDto>> asStaff = await ListAsync(OTHER_ORG_ID, "operator-user", run.Execution.Id, staff: true);

        Assert.Equal(ErrorType.NotFound, asServiceUser.FirstError.Type);
        Assert.Equal(ErrorType.NotFound, asStaff.FirstError.Type);
    }

    [Fact]
    public async Task List_TraceRowsStampedWithAnotherOrganization_AreNeverServed()
    {
        SeededRun run = await SeedTracedRunAsync();
        await SeedTraceAsync(_context, OTHER_ORG_ID, run.Agent.Id, "foreign", 1, TRACE_PATH, "foreign", _later);

        ErrorOr<List<AgentRunFileDto>> result = await ListAsync(ORG_ID, SERVICE_USER_ID, run.Execution.Id);

        Assert.DoesNotContain(result.Value, file => file.RunExecutionId == "foreign");
    }

    [Fact]
    public async Task Get_WithoutARunId_ServesTheMostRecentlyCollectedRun()
    {
        SeededRun run = await SeedTracedRunAsync();

        ErrorOr<AgentRunFileContent> result = await GetAsync(SERVICE_USER_ID, run.Execution.Id, run.Agent.Id, TRACE_PATH);

        Assert.False(result.IsError);
        Assert.Equal("{\"pass\":2}", System.Text.Encoding.UTF8.GetString(result.Value.Content));
        Assert.Equal("application/x-ndjson", result.Value.ContentType);
        Assert.Equal("run-2_1_trace_trace.ndjson", result.Value.FileName);
    }

    [Fact]
    public async Task Get_WithARunId_ServesThatRun()
    {
        SeededRun run = await SeedTracedRunAsync();

        ErrorOr<AgentRunFileContent> result = await GetAsync(SERVICE_USER_ID, run.Execution.Id, run.Agent.Id, TRACE_PATH, runExecutionId: "run-1");

        Assert.Equal("{\"pass\":1}", System.Text.Encoding.UTF8.GetString(result.Value.Content));
    }

    [Fact]
    public async Task Get_AnArtifactPath_ServesItAsJson()
    {
        SeededRun run = await SeedTracedRunAsync();

        ErrorOr<AgentRunFileContent> result = await GetAsync(SERVICE_USER_ID, run.Execution.Id, run.Agent.Id, ARTIFACT_PATH);

        Assert.Equal("application/json", result.Value.ContentType);
    }

    [Fact]
    public async Task Get_ABlockOfAnotherRun_IsNotFound()
    {
        SeededRun run = await SeedTracedRunAsync();
        SeededRun otherRun = await SeedRunAsync(_context, ORG_ID, ANALYST_ID);
        await SeedTraceAsync(_context, ORG_ID, otherRun.Agent.Id, "run-x", 1, TRACE_PATH, "other", _later);

        ErrorOr<AgentRunFileContent> result = await GetAsync(SERVICE_USER_ID, run.Execution.Id, otherRun.Agent.Id, TRACE_PATH);

        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Get_ByAnAnalyst_IsForbidden()
    {
        SeededRun run = await SeedTracedRunAsync(startedBy: ANALYST_ID);

        ErrorOr<AgentRunFileContent> result = await GetAsync(ANALYST_ID, run.Execution.Id, run.Agent.Id, TRACE_PATH);

        Assert.Equal(ErrorType.Forbidden, result.FirstError.Type);
    }

    [Fact]
    public async Task Get_FromAnotherOrganization_IsNotFound()
    {
        SeededRun run = await SeedTracedRunAsync();

        ErrorOr<AgentRunFileContent> result = await GetAsync(
            OTHER_SERVICE_USER_ID, run.Execution.Id, run.Agent.Id, TRACE_PATH, organizationId: OTHER_ORG_ID);

        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Download_ByTheServiceUserThatStartedTheRun_Succeeds()
    {
        SeededRun run = await SeedTracedRunAsync();

        ErrorOr<DownloadExecutedWorkflowResult> result = await DownloadAsync(SERVICE_USER_ID, run.Execution.Id, workflowId: null);

        Assert.False(result.IsError);
    }

    [Fact]
    public async Task Download_ByWorkflowId_ByTheServiceUserThatStartedTheRun_Succeeds()
    {
        SeededRun run = await SeedTracedRunAsync();

        ErrorOr<DownloadExecutedWorkflowResult> result = await DownloadAsync(SERVICE_USER_ID, executionId: null, run.Execution.WorkflowId);

        Assert.False(result.IsError);
    }

    [Fact]
    public async Task Download_ByAnAnalystWhoStartedTheRun_IsForbidden()
    {
        SeededRun run = await SeedTracedRunAsync(startedBy: ANALYST_ID);

        ErrorOr<DownloadExecutedWorkflowResult> result = await DownloadAsync(ANALYST_ID, run.Execution.Id, workflowId: null);

        Assert.Equal(ErrorType.Forbidden, result.FirstError.Type);
    }

    [Fact]
    public async Task Download_ByTheServiceUserOfSomeoneElsesRun_IsForbidden()
    {
        SeededRun run = await SeedTracedRunAsync(startedBy: ANALYST_ID);

        ErrorOr<DownloadExecutedWorkflowResult> result = await DownloadAsync(SERVICE_USER_ID, executionId: null, run.Execution.WorkflowId);

        Assert.Equal(ErrorType.Forbidden, result.FirstError.Type);
    }

    [Fact]
    public async Task Download_OfAnotherOrganizationsRun_AnswersAsIfItDidNotExist()
    {
        SeededRun foreign = await SeedTracedRunAsync(organizationId: OTHER_ORG_ID, startedBy: OTHER_SERVICE_USER_ID);

        ErrorOr<DownloadExecutedWorkflowResult> byExecution = await DownloadAsync(ANALYST_ID, foreign.Execution.Id, workflowId: null);
        ErrorOr<DownloadExecutedWorkflowResult> byWorkflow = await DownloadAsync(ANALYST_ID, executionId: null, foreign.Execution.WorkflowId);
        ErrorOr<DownloadExecutedWorkflowResult> missing = await DownloadAsync(ANALYST_ID, "no-such-execution", workflowId: null);

        Assert.Equal(ErrorType.NotFound, byExecution.FirstError.Type);
        Assert.Equal(ErrorType.NotFound, byWorkflow.FirstError.Type);
        Assert.Equal(missing.FirstError.Description, byExecution.FirstError.Description);
    }

    private Task<ErrorOr<DownloadExecutedWorkflowResult>> DownloadAsync(string userId, string? executionId, string? workflowId)
    {
        return new DownloadExecutedWorkflowQueryHandler(_unitOfWork, NullLogger<DownloadExecutedWorkflowQueryHandler>.Instance)
            .Handle(new DownloadExecutedWorkflowQuery(ORG_ID, userId, executionId, workflowId, JobId: null), TestContext.Current.CancellationToken);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}
