using System.Text;
using ErrorOr;
using IniaAi.Application.Queries.ExecutedWorkflows.DownloadExecutedWorkflow;
using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Citations.Traces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Enums;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses.ExecutedWorkflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Queries.ExecutedWorkflows.DownloadExecutedWorkflow;

public class DownloadExecutedWorkflowQueryHandlerTests : IDisposable
{
    private const string ORG_ID = "org-test";
    private const string OTHER_ORG_ID = "org-other";
    private const string USER_ID = "user-test";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly DownloadExecutedWorkflowQueryHandler _handler;

    public DownloadExecutedWorkflowQueryHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();

        IUserDataUnitOfWork unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        _handler = new DownloadExecutedWorkflowQueryHandler(
            unitOfWork,
            NullLogger<DownloadExecutedWorkflowQueryHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WithWorkflowId_ReturnsSerializedWorkflow()
    {
        Workflow workflow = await SeedWorkflowAsync(ORG_ID, "My Executed Workflow");

        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId: ORG_ID,
            UserId: USER_ID,
            ExecutionId: null,
            WorkflowId: workflow.Id,
            JobId: null) { CallerReadsEveryRun = true };

        ErrorOr<DownloadExecutedWorkflowResult> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal("application/json", result.Value.ContentType);
        Assert.Equal($"{workflow.Name}.workflow", result.Value.FileName);
        Assert.NotEmpty(result.Value.Content);

        string json = Encoding.UTF8.GetString(result.Value.Content);
        Assert.Contains(workflow.Name, json);

        var restored = Workflow.FromJson(json);
        Assert.NotNull(restored);
        Assert.Equal(Workflow.CURRENT_FORMAT_VERSION, restored.FormatVersion);
    }

    [Fact]
    public async Task Handle_WithExecutionId_ReturnsSerializedWorkflow()
    {
        Workflow workflow = await SeedWorkflowAsync(ORG_ID, "Execution Lookup Workflow");
        WorkflowExecution execution = await SeedExecutionAsync(ORG_ID, workflow.Id, jobId: "job-1");

        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId: ORG_ID,
            UserId: USER_ID,
            ExecutionId: execution.Id,
            WorkflowId: null,
            JobId: null) { CallerReadsEveryRun = true };

        ErrorOr<DownloadExecutedWorkflowResult> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal($"{workflow.Name}.workflow", result.Value.FileName);
    }

    [Fact]
    public async Task Handle_WithJobId_ReturnsSerializedWorkflow()
    {
        Workflow workflow = await SeedWorkflowAsync(ORG_ID, "Job Lookup Workflow");
        _ = await SeedExecutionAsync(ORG_ID, workflow.Id, jobId: "hangfire-job-42");

        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId: ORG_ID,
            UserId: USER_ID,
            ExecutionId: null,
            WorkflowId: null,
            JobId: "hangfire-job-42") { CallerReadsEveryRun = true };

        ErrorOr<DownloadExecutedWorkflowResult> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal($"{workflow.Name}.workflow", result.Value.FileName);
    }

    [Fact]
    public async Task Handle_WithUnknownWorkflowId_ReturnsNotFound()
    {
        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId: ORG_ID,
            UserId: USER_ID,
            ExecutionId: null,
            WorkflowId: "missing-workflow-id",
            JobId: null) { CallerReadsEveryRun = true };

        ErrorOr<DownloadExecutedWorkflowResult> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Handle_WithUnknownExecutionId_ReturnsNotFound()
    {
        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId: ORG_ID,
            UserId: USER_ID,
            ExecutionId: "missing-execution-id",
            WorkflowId: null,
            JobId: null) { CallerReadsEveryRun = true };

        ErrorOr<DownloadExecutedWorkflowResult> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Handle_WithUnknownJobId_ReturnsNotFound()
    {
        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId: ORG_ID,
            UserId: USER_ID,
            ExecutionId: null,
            WorkflowId: null,
            JobId: "missing-job-id") { CallerReadsEveryRun = true };

        ErrorOr<DownloadExecutedWorkflowResult> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Handle_WithCrossOrgWorkflowId_ReturnsNotFound()
    {
        Workflow workflow = await SeedWorkflowAsync(OTHER_ORG_ID, "Other Org Workflow");

        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId: ORG_ID,
            UserId: USER_ID,
            ExecutionId: null,
            WorkflowId: workflow.Id,
            JobId: null) { CallerReadsEveryRun = true };

        ErrorOr<DownloadExecutedWorkflowResult> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Handle_WithCrossOrgExecutionId_ReturnsNotFound()
    {
        Workflow workflow = await SeedWorkflowAsync(OTHER_ORG_ID, "Other Org Workflow");
        WorkflowExecution execution = await SeedExecutionAsync(OTHER_ORG_ID, workflow.Id, jobId: "job-other");

        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId: ORG_ID,
            UserId: USER_ID,
            ExecutionId: execution.Id,
            WorkflowId: null,
            JobId: null) { CallerReadsEveryRun = true };

        ErrorOr<DownloadExecutedWorkflowResult> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Handle_WithCrossOrgJobId_ReturnsNotFound()
    {
        Workflow workflow = await SeedWorkflowAsync(OTHER_ORG_ID, "Other Org Workflow");
        _ = await SeedExecutionAsync(OTHER_ORG_ID, workflow.Id, jobId: "job-cross-org");

        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId: ORG_ID,
            UserId: USER_ID,
            ExecutionId: null,
            WorkflowId: null,
            JobId: "job-cross-org") { CallerReadsEveryRun = true };

        ErrorOr<DownloadExecutedWorkflowResult> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Handle_WithJobIdCollisionAcrossOrgs_ResolvesToCallersOrg()
    {
        const string SHARED_JOB_ID = "shared-job-id";
        Workflow otherWorkflow = await SeedWorkflowAsync(OTHER_ORG_ID, "Other Org Workflow");
        _ = await SeedExecutionAsync(OTHER_ORG_ID, otherWorkflow.Id, jobId: SHARED_JOB_ID);

        Workflow myWorkflow = await SeedWorkflowAsync(ORG_ID, "My Workflow");
        _ = await SeedExecutionAsync(ORG_ID, myWorkflow.Id, jobId: SHARED_JOB_ID);

        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId: ORG_ID,
            UserId: USER_ID,
            ExecutionId: null,
            WorkflowId: null,
            JobId: SHARED_JOB_ID) { CallerReadsEveryRun = true };

        ErrorOr<DownloadExecutedWorkflowResult> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal($"{myWorkflow.Name}.workflow", result.Value.FileName);
    }

    [Fact]
    public async Task Handle_WithExecutionMissingWorkflowId_ReturnsNotFound()
    {
        WorkflowExecution execution = await SeedExecutionAsync(ORG_ID, workflowId: null, jobId: "job-orphan");

        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId: ORG_ID,
            UserId: USER_ID,
            ExecutionId: execution.Id,
            WorkflowId: null,
            JobId: null) { CallerReadsEveryRun = true };

        ErrorOr<DownloadExecutedWorkflowResult> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Handle_AgentBlockWithTraces_CarriesThemInTheDownloadedJson()
    {
        var workflow = new Workflow
        {
            Name = "Traced Workflow",
            Title = "Traced Workflow",
            OrganizationId = ORG_ID,
        };
        var section = new Section("Analysis", workflow);
        var tracedBlock = new Block(BlockTypes.Agent, section, "AgentOut");
        _ = new Block(BlockTypes.AiQuery, section, "PlainOut");
        _ = await _context.Workflows.AddAsync(workflow, TestContext.Current.CancellationToken);
        byte[] traceBytes = "trace-line"u8.ToArray();
        _ = await _context.Set<AgentRunTraceFile>().AddAsync(
            new AgentRunTraceFile
            {
                OrganizationId = ORG_ID,
                BlockId = tracedBlock.Id,
                RunExecutionId = "wfexec1_block1_w_1",
                Attempt = 1,
                Path = "trace/trace.ndjson",
                Sha256 = new string('a', 64),
                SizeBytes = traceBytes.Length,
                Content = traceBytes,
            },
            TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId: ORG_ID,
            UserId: USER_ID,
            ExecutionId: null,
            WorkflowId: workflow.Id,
            JobId: null) { CallerReadsEveryRun = true };

        ErrorOr<DownloadExecutedWorkflowResult> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        string json = Encoding.UTF8.GetString(result.Value.Content);
        Assert.Contains("trace/trace.ndjson", json);
        Assert.Contains(Convert.ToBase64String(traceBytes), json);
        Assert.Contains("wfexec1_block1_w_1", json);
        int propertyOccurrences = json.Split(nameof(Block.AgentRunTraceFiles)).Length - 1;
        Assert.Equal(1, propertyOccurrences);
    }

    [Fact]
    public async Task Handle_BlockWithCitationTrace_CarriesItAndTheTextOfTheOrganizationsCitedPages()
    {
        Page ownPage = await SeedPageAsync(ORG_ID, "Group revenue was £27.0m in 2025.");
        Page otherPage = await SeedPageAsync(OTHER_ORG_ID, "Another organization's page.");
        var workflow = new Workflow
        {
            Name = "Cited Workflow",
            Title = "Cited Workflow",
            OrganizationId = ORG_ID,
        };
        var section = new Section("Analysis", workflow);
        var citedBlock = new Block(BlockTypes.AiQuery, section, "CitedOut");
        _ = await _context.Workflows.AddAsync(workflow, TestContext.Current.CancellationToken);
        string trace = new CitationTraceContent
        {
            Sources =
            [
                new TracedSource { PageId = ownPage.Id, Start = 18, Length = 6 },
                new TracedSource { PageId = otherPage.Id, Start = 0, Length = 7 },
            ],
        }.ToJson();
        _ = await _context.BlockCitationTraces.AddAsync(
            new BlockCitationTrace { OrganizationId = ORG_ID, BlockId = citedBlock.Id, Content = trace },
            TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        ErrorOr<DownloadExecutedWorkflowResult> result = await _handler.Handle(
            new DownloadExecutedWorkflowQuery(ORG_ID, USER_ID, ExecutionId: null, WorkflowId: workflow.Id, JobId: null) { CallerReadsEveryRun = true },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Workflow? restored = Workflow.FromExportJson(Encoding.UTF8.GetString(result.Value.Content));
        Assert.NotNull(restored);
        Block restoredBlock = Assert.Single(restored.Sections.SelectMany(s => s.Blocks), b => b.CitationTrace is not null);
        Assert.Equal(trace, CitationTraceContent.Parse(restoredBlock.CitationTrace!.Content)!.ToJson());
        CitationTracePage page = Assert.Single(restored.CitationTracePages);
        Assert.Equal(ownPage.Id, page.Id);
        Assert.Equal("Group revenue was £27.0m in 2025.", page.Text);
    }

    private async Task<Page> SeedPageAsync(string organizationId, string text)
    {
        var fileMetadata = new FileMetadata(organizationId, "annual-report.pdf", text.Length, StorageProvider.Database, null);
        var document = new Document(fileMetadata, MediaType.ApplicationPdf);
        var page = new Page(document, 1, 1) { Text = text };
        _ = await _context.FileMetadata.AddAsync(fileMetadata, TestContext.Current.CancellationToken);
        _ = await _context.Documents.AddAsync(document, TestContext.Current.CancellationToken);
        _ = await _context.Pages.AddAsync(page, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();
        return page;
    }

    private async Task<Workflow> SeedWorkflowAsync(string organizationId, string name)
    {
        var workflow = new Workflow
        {
            Name = name,
            Title = name,
            OrganizationId = organizationId,
        };
        _ = await _context.Workflows.AddAsync(workflow, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();
        return workflow;
    }

    private async Task<WorkflowExecution> SeedExecutionAsync(
        string organizationId,
        string? workflowId,
        string? jobId)
    {
        var execution = new WorkflowExecution
        {
            OrganizationId = organizationId,
            WorkflowId = workflowId,
            JobId = jobId,
            Title = "Execution",
        };
        _ = await _context.WorkflowExecutions.AddAsync(execution, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();
        return execution;
    }

    public void Dispose()
    {
        _context?.Dispose();
        _serviceProvider?.Dispose();
    }
}
