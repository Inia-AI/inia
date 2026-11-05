using System.Text;
using ErrorOr;
using IniaAi.Application.Queries.ExecutedWorkflows.DownloadExecutedWorkflow;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.WebApi.Contracts.Responses.ExecutedWorkflows;
using IniaAi.WebApi.IntegrationTests.Builders;
using IniaAi.WebApi.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.WebApi.IntegrationTests.FunctionalTests.ExecutedWorkflows;

/// <summary>
/// Integration tests for the SuperAdmin executed-workflow download query.
/// Exercises the real DB path through the dispatch pipeline — covers validator, handler,
/// EF/spec graph load, and serialization end-to-end.
/// </summary>
public class DownloadExecutedWorkflowTests : IClassFixture<ApiTestFixture>, IAsyncLifetime
{
    private const string ORG_ID = "IniaAI";
    private const string OTHER_ORG_ID = "OrgB-DownloadExecutedWorkflow";
    private const string USER_ID = "test-super-admin";

    private readonly ClientBuilder _apiBuilder;
    private readonly List<string> _seededWorkflowIds = [];
    private readonly List<string> _seededExecutionIds = [];
    private readonly List<string> _seededOrganizationIds = [];

    public DownloadExecutedWorkflowTests(ApiTestFixture fixture)
    {
        _apiBuilder = fixture.ClientBuilder;
    }

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_seededExecutionIds.Count == 0
            && _seededWorkflowIds.Count == 0
            && _seededOrganizationIds.Count == 0)
        {
            return;
        }

        using IServiceScope scope = _apiBuilder.CurrentScope.ServiceProvider.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        if (_seededExecutionIds.Count > 0)
        {
            _ = await db.WorkflowExecutions
                .IgnoreQueryFilters()
                .Where(e => _seededExecutionIds.Contains(e.Id))
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        if (_seededWorkflowIds.Count > 0)
        {
            _ = await db.Workflows
                .IgnoreQueryFilters()
                .Where(w => _seededWorkflowIds.Contains(w.Id))
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        await _apiBuilder.CleanOrganizationsByIdsAsync(_seededOrganizationIds);
    }

    [Fact]
    public async Task DownloadByWorkflowId_ReturnsSerializedWorkflow()
    {
        Workflow workflow = await SeedWorkflowAsync($"WF-WorkflowId-{Guid.NewGuid():N}");

        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId: ORG_ID,
            UserId: USER_ID,
            ExecutionId: null,
            WorkflowId: workflow.Id,
            JobId: null) { CallerReadsEveryRun = true };

        ErrorOr<DownloadExecutedWorkflowResult> result = await _apiBuilder.SendAsync(query);

        Assert.False(result.IsError);
        Assert.Equal("application/json", result.Value.ContentType);
        Assert.Equal($"{workflow.Name}.workflow", result.Value.FileName);
        Assert.NotEmpty(result.Value.Content);

        string json = Encoding.UTF8.GetString(result.Value.Content);
        Assert.Contains(workflow.Name, json);
    }

    [Fact]
    public async Task DownloadByExecutionId_ReturnsSerializedWorkflow()
    {
        Workflow workflow = await SeedWorkflowAsync($"WF-ExecutionId-{Guid.NewGuid():N}");
        WorkflowExecution execution = await SeedExecutionAsync(workflow.Id, jobId: $"job-{Guid.NewGuid():N}");

        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId: ORG_ID,
            UserId: USER_ID,
            ExecutionId: execution.Id,
            WorkflowId: null,
            JobId: null) { CallerReadsEveryRun = true };

        ErrorOr<DownloadExecutedWorkflowResult> result = await _apiBuilder.SendAsync(query);

        Assert.False(result.IsError);
        Assert.Equal($"{workflow.Name}.workflow", result.Value.FileName);
    }

    [Fact]
    public async Task DownloadByJobId_ReturnsSerializedWorkflow()
    {
        Workflow workflow = await SeedWorkflowAsync($"WF-JobId-{Guid.NewGuid():N}");
        string jobId = $"job-{Guid.NewGuid():N}";
        _ = await SeedExecutionAsync(workflow.Id, jobId: jobId);

        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId: ORG_ID,
            UserId: USER_ID,
            ExecutionId: null,
            WorkflowId: null,
            JobId: jobId) { CallerReadsEveryRun = true };

        ErrorOr<DownloadExecutedWorkflowResult> result = await _apiBuilder.SendAsync(query);

        Assert.False(result.IsError);
        Assert.Equal($"{workflow.Name}.workflow", result.Value.FileName);
    }

    [Fact]
    public async Task DownloadByUnknownWorkflowId_ReturnsNotFound()
    {
        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId: ORG_ID,
            UserId: USER_ID,
            ExecutionId: null,
            WorkflowId: $"missing-workflow-{Guid.NewGuid():N}",
            JobId: null) { CallerReadsEveryRun = true };

        ErrorOr<DownloadExecutedWorkflowResult> result = await _apiBuilder.SendAsync(query);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task DownloadByUnknownExecutionId_ReturnsNotFound()
    {
        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId: ORG_ID,
            UserId: USER_ID,
            ExecutionId: $"missing-execution-{Guid.NewGuid():N}",
            WorkflowId: null,
            JobId: null) { CallerReadsEveryRun = true };

        ErrorOr<DownloadExecutedWorkflowResult> result = await _apiBuilder.SendAsync(query);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task DownloadByUnknownJobId_ReturnsNotFound()
    {
        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId: ORG_ID,
            UserId: USER_ID,
            ExecutionId: null,
            WorkflowId: null,
            JobId: $"missing-job-{Guid.NewGuid():N}") { CallerReadsEveryRun = true };

        ErrorOr<DownloadExecutedWorkflowResult> result = await _apiBuilder.SendAsync(query);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task DownloadCrossOrgWorkflow_ReturnsNotFound()
    {
        Workflow workflow = await SeedWorkflowAsync(
            $"WF-CrossOrg-{Guid.NewGuid():N}",
            organizationId: OTHER_ORG_ID);

        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId: ORG_ID,
            UserId: USER_ID,
            ExecutionId: null,
            WorkflowId: workflow.Id,
            JobId: null) { CallerReadsEveryRun = true };

        ErrorOr<DownloadExecutedWorkflowResult> result = await _apiBuilder.SendAsync(query);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task DownloadCrossOrgExecution_ReturnsNotFound()
    {
        Workflow workflow = await SeedWorkflowAsync(
            $"WF-CrossOrgExec-{Guid.NewGuid():N}",
            organizationId: OTHER_ORG_ID);
        WorkflowExecution execution = await SeedExecutionAsync(
            workflow.Id,
            jobId: $"job-{Guid.NewGuid():N}",
            organizationId: OTHER_ORG_ID);

        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId: ORG_ID,
            UserId: USER_ID,
            ExecutionId: execution.Id,
            WorkflowId: null,
            JobId: null) { CallerReadsEveryRun = true };

        ErrorOr<DownloadExecutedWorkflowResult> result = await _apiBuilder.SendAsync(query);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task DownloadWithNoIdentifier_FailsValidation()
    {
        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId: ORG_ID,
            UserId: USER_ID,
            ExecutionId: null,
            WorkflowId: null,
            JobId: null) { CallerReadsEveryRun = true };

        ErrorOr<DownloadExecutedWorkflowResult> result = await _apiBuilder.SendAsync(query);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Type == ErrorType.Validation);
    }

    [Fact]
    public async Task DownloadWithMultipleIdentifiers_FailsValidation()
    {
        Workflow workflow = await SeedWorkflowAsync($"WF-MultiId-{Guid.NewGuid():N}");

        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId: ORG_ID,
            UserId: USER_ID,
            ExecutionId: "some-execution-id",
            WorkflowId: workflow.Id,
            JobId: null) { CallerReadsEveryRun = true };

        ErrorOr<DownloadExecutedWorkflowResult> result = await _apiBuilder.SendAsync(query);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Type == ErrorType.Validation);
    }

    private async Task<Workflow> SeedWorkflowAsync(string name, string organizationId = ORG_ID)
    {
        await EnsureOrganizationExistsAsync(organizationId);

        // UserId is intentionally left null — the AspNetUsers FK only triggers when
        // a non-null value is supplied, and the request-side USER_ID does not need
        // to match the seeded entity's owner.
        var workflow = new Workflow
        {
            Name = name,
            Title = name,
            OrganizationId = organizationId,
        };

        using IServiceScope scope = _apiBuilder.CurrentScope.ServiceProvider.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        _ = await db.Workflows.AddAsync(workflow, TestContext.Current.CancellationToken);
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _seededWorkflowIds.Add(workflow.Id);
        return workflow;
    }

    private async Task EnsureOrganizationExistsAsync(string organizationId)
    {
        using IServiceScope scope = _apiBuilder.CurrentScope.ServiceProvider.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        bool exists = await db.Organizations
            .IgnoreQueryFilters()
            .AnyAsync(o => o.Id == organizationId, TestContext.Current.CancellationToken);

        if (exists)
        {
            return;
        }

        var organization = new Organization($"Test Org {organizationId}", customId: organizationId);
        _ = await db.Organizations.AddAsync(organization, TestContext.Current.CancellationToken);
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _seededOrganizationIds.Add(organization.Id);
    }

    private async Task<WorkflowExecution> SeedExecutionAsync(
        string? workflowId,
        string? jobId,
        string organizationId = ORG_ID)
    {
        var execution = new WorkflowExecution
        {
            OrganizationId = organizationId,
            WorkflowId = workflowId,
            JobId = jobId,
            Title = "Integration Test Execution",
        };

        using IServiceScope scope = _apiBuilder.CurrentScope.ServiceProvider.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        _ = await db.WorkflowExecutions.AddAsync(execution, TestContext.Current.CancellationToken);
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _seededExecutionIds.Add(execution.Id);
        return execution;
    }
}
