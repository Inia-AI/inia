using ErrorOr;
using IniaAi.Application.Commands.Files.UploadFile;
using IniaAi.Application.Commands.WorkflowExecutions.StartWorkflowExecution;
using IniaAi.Application.Queries.Jobs.GetJob;
using IniaAi.Application.Queries.WorkflowReports.ListReportSections;
using IniaAi.Application.Queries.Workflows.ListWorkflowCategories;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Specifications.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Services.Models.Jobs.Enums;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Dto;
using IniaAi.WebApi.Contracts.Requests;
using IniaAi.WebApi.Contracts.Responses.Jobs;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using IniaAi.WebApi.Contracts.Responses.WorkflowReports;
using IniaAi.WebApi.IntegrationTests;
using IniaAi.WebApi.IntegrationTests.Builders;
using IniaAi.WebApi.IntegrationTests.Fixtures;
using IniaAi.WebApi.Models.Dto;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.WebApi.FunctionalTests.WorkflowExecutions;

/// <summary>
/// Functional tests for the Counterparty CRUD API using AAA structure.
/// </summary>
public class WorkflowExecutionsTests : IClassFixture<ApiTestFixture>, IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;
    private readonly ClientBuilder _apiBuilder;
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Initializes the test class using the shared <see cref="ApiTestFixture"/>.
    /// </summary>
    /// <param name="fixture">Shared test fixture.</param>
    /// <param name="output">Test output helper for writing diagnostic information.</param>
    public WorkflowExecutionsTests(ApiTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _apiBuilder = fixture.ClientBuilder;
        _output = output;
    }

    #region Helpers

    /// <summary>
    /// Creates a counterparty this class owns; the fixture deletes it after the class. A borrowed
    /// counterparty of the shared organization can be deleted by another class mid-test.
    /// </summary>
    private async Task<string> CreateCounterpartyAsync(string? organizationId = null)
    {
        IUserDataUnitOfWork userUnitOfWork = _apiBuilder.GetNewScope<IUserDataUnitOfWork>().Service;
        var counterparty = new Counterparty(organizationId ?? "IniaAI", "Test Counterparty");
        await userUnitOfWork.Repository<Counterparty>().AddAsync(counterparty);
        _ = await userUnitOfWork.CompleteAsync();
        _fixture.RegisterCreatedCounterparty(counterparty.Id);

        return counterparty.Id;
    }

    private async Task<List<WorkflowCategoryDto>> GetValidWorkflowCategoriesAsync(string? organizationId = null, string? userId = null)
    {
        ErrorOr<List<WorkflowCategoryDto>> workflows = await _apiBuilder.SendAsync(new ListWorkflowCategoriesQuery(organizationId ?? "IniaAI", userId ?? "test-user-id", IncludeWorkflows: true));
        Assert.NotEmpty(workflows.Value);
        return workflows.Value;
    }

    /// <summary>
    /// Starts and persists an execution of the given workflow type.
    /// </summary>
    private async Task<(StartWorkflowExecutionCommand command, string id, string? jobId)> CreateAndPersistWorkflowExecutionAsync(string workflowTypeId, string? organizationId = null, string? userId = null, bool documentsProvided = false)
    {
        string counterpartyId = await CreateCounterpartyAsync();

        List<WorkflowCategoryDto> workflowCategories = await GetValidWorkflowCategoriesAsync();
        WorkflowDto? workflow = workflowCategories.Where(x => x.Workflows is not null).SelectMany(c => c.Workflows!).FirstOrDefault(w => w.WorkflowTypeId == workflowTypeId);
        Assert.True(workflow is not null, $"Workflow with type ID '{workflowTypeId}' not found in the provided categories.");

        StartWorkflowExecutionCommand command = TestDataFactory.BuildValidStartWorkflowExecution(workflowDto: workflow, counterpartyId: counterpartyId, userId ?? "User123", organizationId ?? "IniaAI");//, fileIds: new List<string>() { uploadFileCommandResult.Value.Id });

        ErrorOr<WorkflowExecutionDto> result = await _apiBuilder.SendAsync(command);
        Assert.False(result.IsError, string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}")));

        return (command, result.Value.Id, result.Value.JobId);
    }

    #endregion

    #region Create

    [Fact]
    public async Task ShouldFail_When_Invalid_WorkflowId()
    {
        // Arrange: Use invalid workflow ID
        string counterpartyId = await CreateCounterpartyAsync();
        StartWorkflowExecutionCommand command = TestDataFactory.BuildValidStartWorkflowExecution(
            workflowDto: new WorkflowDto { Id = Guid.NewGuid().ToString(), WorkflowTypeId = Guid.NewGuid().ToString() },
            userId: "User123",
            organizationId: "IniaAi",
            counterpartyId: counterpartyId,
            fileIds: ["some-valid-file-id"]);

        // Act
        ErrorOr<WorkflowExecutionDto> result = await _apiBuilder.SendAsync(command);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task ShouldStartWorkflowExecution()
    {
        // Arrange
        (StartWorkflowExecutionCommand command, string id, string? jobId) result = await CreateAndPersistWorkflowExecutionAsync("TestWorkflowType");

        // Act
        IUserDataUnitOfWork unitOfWork = _apiBuilder.GetNewScope<IUserDataUnitOfWork>().Service;
        var spec = new WorkflowExecutionByIdSpec(result.id);
        WorkflowExecution? entity = await unitOfWork.WorkflowExecutions.GetFirstOrDefaultAsync(spec, TestContext.Current.CancellationToken);

        // ASSERT: basic shape
        Assert.NotNull(entity);
        Assert.Equal(result.command.OrganizationId, entity.OrganizationId);
        Assert.Equal(result.command.UserId, entity.UserId);

        // ASSERT: specific DB properties

        Assert.Equal(result.id, entity.Id);
        // WorkflowId is the user's copy, not the original template
        Assert.False(string.IsNullOrWhiteSpace(entity.WorkflowId));
        Assert.False(string.IsNullOrWhiteSpace(entity.WorkflowTypeId));
        Assert.False(string.IsNullOrWhiteSpace(entity.Title));
        Assert.Equal("IniaAI", entity.OrganizationId);
        Assert.Equal("User123", entity.UserId);
        Assert.False(string.IsNullOrWhiteSpace(entity.JobId));
        Assert.Equal(result.command.Request.CounterpartyId, entity.CounterpartyId);
        Assert.NotNull(entity.WorkflowExecutionsCounterparties);
        Assert.Equal(result.command.Request.PeerCompanyIds?.Count ?? 0, entity.WorkflowExecutionsCounterparties.Count);

        Assert.Equal(
            (result.command.Request.PeerCompanyIds ?? []).Order(),
            entity.WorkflowExecutionsCounterparties.Select(x => x.CounterpartyId).Order());

        // Audit fields
        Assert.Equal(DateTime.UtcNow, entity.CreatedAt, TimeSpan.FromSeconds(10));
        Assert.NotNull(entity.UpdatedAt);
        Assert.Equal(DateTime.UtcNow, entity.UpdatedAt.Value, TimeSpan.FromSeconds(10));

        // Soft delete
        Assert.False(entity.IsDeleted);
        Assert.Null(entity.DeletedAt);

        // Enum state
        Assert.NotEqual(WorkflowState.Undefined, entity.WorkflowState);

        // Variables
        if (result.command.Request.Variables is not null)
        {
            Assert.NotNull(entity.Variables);
        }

        // Related collections
        Assert.NotNull(entity.Logs);
    }

    [Theory]
    [InlineData("TestWorkflowType", true)]
    [InlineData("TestWorkflowType", false)]
    public async Task ShouldStartWorkflowExecutionWithOrWithoutDocumentsWithFinishedJob(string workflowTypeId, bool documentsProvided)
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();

        // Arrange
        (StartWorkflowExecutionCommand command, string id, string? jobId) result = await CreateAndPersistWorkflowExecutionAsync(workflowTypeId: workflowTypeId, organizationId: handle.User.OrganizationId, userId: handle.User.Id, documentsProvided: documentsProvided);

        // Act:
        //Retrieve the associated Job ID(assuming you store it on the entity)
        string? jobId = result.jobId;
        Assert.False(string.IsNullOrWhiteSpace(jobId));

        // Wait for the Job to reach Finished or Error
        JobDto job = await PollJobUntilFinishedOrError(jobId, organizationId: handle.User.OrganizationId, userId: handle.User.Id);

        // Assert: Ensure job ended in Finished or Error, not stuck
        Assert.Contains(job.State, new[] { JobState.Done.ToString(), JobState.Error.ToString() });

        // Optionally: Validate output
        Assert.NotNull(job);
        Assert.Equal(jobId, job.Id);

        // Load the original workflow
        ICoreUnitOfWork coreUnitOfWork = _apiBuilder.GetNewScope<ICoreUnitOfWork>().Service;
        IUserDataUnitOfWork userUnitOfWork = _apiBuilder.GetNewScope<IUserDataUnitOfWork>().Service;

        Workflow? sourceWorkflow = await coreUnitOfWork.Workflows.GetByIdAsync(result.command.Request.WorkflowId);
        Assert.NotNull(sourceWorkflow);
        Assert.NotEmpty(sourceWorkflow.Sections);

        // Load the user workflow
        Workflow? userWorkflow = await userUnitOfWork.Workflows.GetByIdAsync(result.id, TestContext.Current.CancellationToken);
        Assert.NotNull(userWorkflow);
        Assert.NotEmpty(userWorkflow.Sections);

        // Load the WorkflowExecution entity
        var spec = new WorkflowExecutionWithDetailsSpec(result.id);
        WorkflowExecution? workflowExecution = await userUnitOfWork.WorkflowExecutions.GetFirstOrDefaultAsync(spec, TestContext.Current.CancellationToken);

        Assert.NotNull(workflowExecution);

        // Assert: the report tree carries one section per source workflow section
        var reportSpec = new WorkflowReportForExportSpec(result.id);
        WorkflowReport? report = await userUnitOfWork.Repository<WorkflowReport>()
            .GetFirstOrDefaultAsync(reportSpec, TestContext.Current.CancellationToken);
        Assert.NotNull(report);
        Assert.Equal(sourceWorkflow.Sections.Count(), report.Sections.Count);

        // Validate execution logs exist
        var workflowOutputExecutionLogsSpec = new WorkflowExecutionLogsForWorkflowExecutionSpec(result.id);
        IList<WorkflowExecutionLog> workflowOutputExecutionLogs = await userUnitOfWork.Repository<WorkflowExecutionLog>().GetAll(workflowOutputExecutionLogsSpec).OrderBy(x => x.CreatedAt).ToListAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(workflowOutputExecutionLogs);

        var query = new ListReportSectionsQuery(result.id, OrganizationId: handle.User.OrganizationId);
        ErrorOr<List<ReportSectionDto>> blocksResult = await _apiBuilder.SendAsync(query);
        Assert.False(blocksResult.IsError);
        List<ReportSectionDto> workflowExecutionSections = blocksResult.Value;
        Assert.NotEmpty(workflowExecutionSections);

        // Assert: returned section count matches the report's section count
        Assert.Equal(report.Sections.Count, workflowExecutionSections.Count);

        //// Optional: assert expected fields on final job result if you expect them
    }
    private async Task<JobDto> PollJobUntilFinishedOrError(string jobId, string? organizationId = null, string? userId = null)
    {
        var query = new GetJobQuery(jobId, organizationId, userId);

        var timeout = TimeSpan.FromMinutes(15);
        var delay = TimeSpan.FromSeconds(2);
        DateTime start = DateTime.UtcNow;

        while (true)
        {
            ErrorOr<JobDto> result = await _apiBuilder.SendAsync(query);
            Assert.False(result.IsError);
            JobDto job = result.Value;

            if (job.State == JobState.Done.ToString() || job.State == JobState.Error.ToString())
            {
                return job;
            }

            if (DateTime.UtcNow - start > timeout)
            {
                throw new TimeoutException($"Job {jobId} did not reach Finished or Error state within {timeout.TotalSeconds} seconds.");
            }

            await Task.Delay(delay);
        }
    }

    [Fact]
    public async Task ShouldStartWorkflowStandardizeFinancialMetricsExecutionWithFinishedJob()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();

        // Arrange
        (StartWorkflowExecutionCommand command, string id, string? jobId) result = await CreateAndPersistWorkflowExecutionAsync(workflowTypeId: "TestWorkflowType", organizationId: handle.User.OrganizationId, userId: handle.User.Id, documentsProvided: false);

        // Act:
        //Retrieve the associated Job ID(assuming you store it on the entity)
        string? jobId = result.jobId;
        Assert.False(string.IsNullOrWhiteSpace(jobId));

        // Wait for the Job to reach Finished or Error
        JobDto job = await PollJobUntilFinishedOrError(jobId, organizationId: handle.User.OrganizationId, userId: handle.User.Id);

        // Assert: Ensure job ended in Finished or Error, not stuck
        Assert.Contains(job.State, new[] { JobState.Done.ToString(), JobState.Error.ToString() });

        // Optionally: Validate output
        Assert.NotNull(job);
        Assert.Equal(jobId, job.Id);
    }

    #endregion Create

    #region TestWorkflowType Completion Tests

    /// <summary>
    /// Full functional test for TestWorkflowType workflow.
    /// This workflow uses LLM blocks that should generate token usage.
    /// </summary>
    [Fact]
    public async Task TestWorkflowType_ShouldCompleteSuccessfully_AndGenerateTokenUsage()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();

        // Arrange - Start workflow execution
        (StartWorkflowExecutionCommand command, string id, string? jobId) result = await CreateAndPersistWorkflowExecutionAsync(
            workflowTypeId: "TestWorkflowType",
            organizationId: handle.User.OrganizationId,
            userId: handle.User.Id,
            documentsProvided: false);

        // Verify job was created
        string? jobId = result.jobId;
        Assert.False(string.IsNullOrWhiteSpace(jobId), "Workflow execution should create a job");

        // Act - Wait for job completion
        JobDto job = await PollJobUntilFinishedOrError(jobId!, organizationId: handle.User.OrganizationId, userId: handle.User.Id);

        // Assert - Job completed
        Assert.Contains(job.State, new[] { JobState.Done.ToString(), JobState.Error.ToString() });
        Assert.Equal(jobId, job.Id);

        // Load and validate workflow execution
        IUserDataUnitOfWork userUnitOfWork = _apiBuilder.GetNewScope<IUserDataUnitOfWork>().Service;
        ICoreUnitOfWork coreUnitOfWork = _apiBuilder.GetNewScope<ICoreUnitOfWork>().Service;

        // Validate source workflow structure
        Workflow? sourceWorkflow = await coreUnitOfWork.Workflows.GetByIdAsync(result.command.Request.WorkflowId);
        Assert.NotNull(sourceWorkflow);
        Assert.NotEmpty(sourceWorkflow.Sections);

        // Validate user workflow was created
        Workflow? userWorkflow = await userUnitOfWork.Workflows.GetByIdAsync(result.id, TestContext.Current.CancellationToken);
        Assert.NotNull(userWorkflow);
        Assert.NotEmpty(userWorkflow.Sections);

        // Validate workflow execution entity
        var spec = new WorkflowExecutionWithDetailsSpec(result.id);
        WorkflowExecution? workflowExecution = await userUnitOfWork.WorkflowExecutions.GetFirstOrDefaultAsync(spec, TestContext.Current.CancellationToken);
        Assert.NotNull(workflowExecution);

        var reportSpec = new WorkflowReportForExportSpec(result.id);
        WorkflowReport? report = await userUnitOfWork.Repository<WorkflowReport>()
            .GetFirstOrDefaultAsync(reportSpec, TestContext.Current.CancellationToken);
        Assert.NotNull(report);
        Assert.NotEmpty(report.Sections);

        // Validate token usage was recorded (if job completed successfully)
        if (job.State == JobState.Done.ToString())
        {
            (Microsoft.Extensions.DependencyInjection.IServiceScope? scope, UserDataDbContext? dbContext) = _apiBuilder.GetNewScope<UserDataDbContext>();
            using (scope)
            {
                List<Domain.Entities.TokenUsage.TokenUsage> tokenUsageRecords = await dbContext.TokenUsages
                    .Where(t => t.OrganizationId == handle.User.OrganizationId
                        && t.WorkflowExecutionId == result.id)
                    .ToListAsync(TestContext.Current.CancellationToken);

                // Token usage should be recorded for LLM blocks
                Assert.NotEmpty(tokenUsageRecords);

                // Verify token usage has correct structure
                foreach (Domain.Entities.TokenUsage.TokenUsage? record in tokenUsageRecords)
                {
                    Assert.Equal(handle.User.OrganizationId, record.OrganizationId);
                    Assert.Equal(result.id, record.WorkflowExecutionId);
                    Assert.NotEmpty(record.Provider);
                    Assert.NotEmpty(record.Family);
                    Assert.NotEmpty(record.ModelId);
                    Assert.True(record.Components.Where(c => c.ComponentType is PricingComponentType.Input).Sum(c => c.Units) >= 0);
                    Assert.True(record.Components.Where(c => c.ComponentType is PricingComponentType.Output).Sum(c => c.Units) >= 0);
                }
            }
        }
    }

    /// <summary>
    /// Verifies that the TestWorkflowType workflow produces output elements
    /// that match the expected section structure.
    /// </summary>
    [Fact]
    public async Task TestWorkflowType_ShouldProduceCorrectOutputElements()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();

        // Arrange
        (StartWorkflowExecutionCommand command, string id, string? jobId) result = await CreateAndPersistWorkflowExecutionAsync(
            workflowTypeId: "TestWorkflowType",
            organizationId: handle.User.OrganizationId,
            userId: handle.User.Id,
            documentsProvided: false);

        string? jobId = result.jobId;
        Assert.False(string.IsNullOrWhiteSpace(jobId));

        // Act - Wait for completion
        JobDto job = await PollJobUntilFinishedOrError(jobId!, organizationId: handle.User.OrganizationId, userId: handle.User.Id);

        // Only validate output structure if job completed successfully
        if (job.State == JobState.Done.ToString())
        {
            // Load workflow structures
            ICoreUnitOfWork coreUnitOfWork = _apiBuilder.GetNewScope<ICoreUnitOfWork>().Service;
            IUserDataUnitOfWork userUnitOfWork = _apiBuilder.GetNewScope<IUserDataUnitOfWork>().Service;

            Workflow? sourceWorkflow = await coreUnitOfWork.Workflows.GetByIdAsync(result.command.Request.WorkflowId);
            var spec = new WorkflowExecutionWithDetailsSpec(result.id);
            WorkflowExecution? workflowExecution = await userUnitOfWork.WorkflowExecutions.GetFirstOrDefaultAsync(spec, TestContext.Current.CancellationToken);

            // Validate the report tree structure
            Assert.NotNull(workflowExecution);

            var reportSpec = new WorkflowReportForExportSpec(result.id);
            WorkflowReport? report = await userUnitOfWork.Repository<WorkflowReport>()
                .GetFirstOrDefaultAsync(reportSpec, TestContext.Current.CancellationToken);
            Assert.NotNull(report);
            Assert.NotEmpty(report.Sections);

            // Section count should match source workflow
            Assert.Equal(sourceWorkflow!.Sections.Count, report.Sections.Count);

            // Query report elements API
            var query = new ListReportSectionsQuery(result.id, OrganizationId: handle.User.OrganizationId);
            ErrorOr<List<ReportSectionDto>> blocksResult = await _apiBuilder.SendAsync(query);
            Assert.False(blocksResult.IsError, "ListReportSections should succeed");
            Assert.NotEmpty(blocksResult.Value);
        }
    }

    /// <summary>
    /// Verifies that token usage includes normalized model IDs.
    /// </summary>
    [Fact]
    public async Task TestWorkflowType_TokenUsage_ShouldHaveNormalizedModelIds()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();

        // Arrange
        (StartWorkflowExecutionCommand command, string id, string? jobId) result = await CreateAndPersistWorkflowExecutionAsync(
            workflowTypeId: "TestWorkflowType",
            organizationId: handle.User.OrganizationId,
            userId: handle.User.Id,
            documentsProvided: false);

        string? jobId = result.jobId;
        Assert.False(string.IsNullOrWhiteSpace(jobId));

        // Act - Wait for completion
        JobDto job = await PollJobUntilFinishedOrError(jobId!, organizationId: handle.User.OrganizationId, userId: handle.User.Id);

        // Assert - Check token usage model ID format
        (Microsoft.Extensions.DependencyInjection.IServiceScope? scope, UserDataDbContext? dbContext) = _apiBuilder.GetNewScope<UserDataDbContext>();
        using (scope)
        {
            List<Domain.Entities.TokenUsage.TokenUsage> tokenUsageRecords = await dbContext.TokenUsages
                .Where(t => t.OrganizationId == handle.User.OrganizationId
                    && t.WorkflowExecutionId == result.id
                    && t.UsageType == UsageType.LlmBlockExecution)
                .ToListAsync(TestContext.Current.CancellationToken);

            if (tokenUsageRecords.Count != 0)
            {
                foreach (Domain.Entities.TokenUsage.TokenUsage? record in tokenUsageRecords)
                {
                    // ModelId should be normalized (no ARN prefix, no path separators)
                    Assert.DoesNotContain("arn:aws", record.ModelId);
                    Assert.DoesNotContain("/", record.ModelId);

                    // ModelId should be clean lowercase with hyphens/dots only
                    Assert.Matches(@"^[a-z0-9\-\.]+$", record.ModelId);

                    // Provider should be a known gateway
                    Assert.Matches("^(aws-bedrock|anthropic-api|cohere-api|unknown)$", record.Provider);

                    // Family should be a known model family
                    Assert.Matches("^(anthropic|cohere|unknown)$", record.Family);
                }
            }
        }
    }

    #endregion TestWorkflowType Completion Tests

    #region WorkflowHandlerFunctionalityTest Tests

    private const string WORKFLOW_HANDLER_FUNCTIONALITY_TEST_TYPE_ID = "WorkflowHandlerFunctionalityTest";
    private const string WORKFLOW_HANDLER_FUNCTIONALITY_TEST_JSON_PATH = "TestResources/WorkflowHandlerFunctionalityTest.workflow.json";

    /// <summary>
    /// Document class mappings for the test workflow:
    /// - WebPage -> page.pdf
    /// - Image -> elephant.pdf
    /// - test_iterations -> Topics_20_pages.pdf
    /// - OcrTest -> OcrTestDocument.pdf (scanned document that triggers LLM OCR)
    /// </summary>
    private static readonly Dictionary<string, string> DocumentClassToFile = new()
    {
        { "WebPage", "TestResources/page.pdf" },
        { "Image", "TestResources/elephant.pdf" },
        { "test_iterations", "TestResources/Topics_20_pages.pdf" },
        { "OcrTest", "TestResources/OcrTestDocument.pdf" }
    };

    /// <summary>
    /// Ensures the WorkflowHandlerFunctionalityTest workflow type and workflow template exist in the database.
    /// </summary>
    private async Task EnsureWorkflowHandlerFunctionalityTestSeededAsync(string organizationId)
    {
        (Microsoft.Extensions.DependencyInjection.IServiceScope? scope, ICoreUnitOfWork? coreUnitOfWork) = _apiBuilder.GetNewScope<ICoreUnitOfWork>();
        using (scope)
        {
            bool workflowTypeExists = await coreUnitOfWork.WorkflowTypes.ExistsAsync(WORKFLOW_HANDLER_FUNCTIONALITY_TEST_TYPE_ID);

            if (!workflowTypeExists)
            {
                // Get or create a category
                string categoryId = await GetOrCreateTestCategoryAsync(coreUnitOfWork);

                // Create the workflow type
                var workflowType = new WorkflowType(WORKFLOW_HANDLER_FUNCTIONALITY_TEST_TYPE_ID, "Workflow Handler Functionality Test")
                {
                    Description = "Comprehensive workflow for testing all block types and workflow features",
                    CategoryId = categoryId
                };
                await coreUnitOfWork.WorkflowTypes.AddAsync(workflowType);

                // Create organization access for the test user's organization
                var access = new WorkflowTypeOrganizationAccess
                {
                    Id = Guid.NewGuid().ToString(),
                    WorkflowTypeId = WORKFLOW_HANDLER_FUNCTIONALITY_TEST_TYPE_ID,
                    OrganizationId = organizationId
                };
                await coreUnitOfWork.Repository<WorkflowTypeOrganizationAccess>().AddAsync(access);
                _ = await coreUnitOfWork.CompleteAsync();
            }
            else
            {
                // Ensure it has organization access for this organization
                WorkflowTypeOrganizationAccess? existingAccess = await coreUnitOfWork.Repository<WorkflowTypeOrganizationAccess>()
                    .GetAll()
                    .Where(a => a.WorkflowTypeId == WORKFLOW_HANDLER_FUNCTIONALITY_TEST_TYPE_ID && a.OrganizationId == organizationId)
                    .FirstOrDefaultAsync();

                if (existingAccess is null)
                {
                    var access = new WorkflowTypeOrganizationAccess
                    {
                        Id = Guid.NewGuid().ToString(),
                        WorkflowTypeId = WORKFLOW_HANDLER_FUNCTIONALITY_TEST_TYPE_ID,
                        OrganizationId = organizationId
                    };
                    await coreUnitOfWork.Repository<WorkflowTypeOrganizationAccess>().AddAsync(access);
                    _ = await coreUnitOfWork.CompleteAsync();
                }
            }

            // Always replace with JSON version to ensure fresh state
            Workflow? existingWorkflow = await coreUnitOfWork.Workflows.GetByTypeIdAsync(WORKFLOW_HANDLER_FUNCTIONALITY_TEST_TYPE_ID);
            if (existingWorkflow is not null)
            {
                coreUnitOfWork.Workflows.Remove(existingWorkflow);
                _ = await coreUnitOfWork.CompleteAsync();
            }

            // Load workflow from JSON file
            Workflow? workflow = LoadWorkflowHandlerTestFromJson(organizationId);
            if (workflow is not null)
            {
                await coreUnitOfWork.Workflows.AddAsync(workflow);
                _ = await coreUnitOfWork.CompleteAsync();
            }
        }
    }

    /// <summary>
    /// Gets or creates a test category for the workflow type.
    /// </summary>
    private static async Task<string> GetOrCreateTestCategoryAsync(ICoreUnitOfWork coreUnitOfWork)
    {
        const string TestCategoryId = "test-category";

        WorkflowTypeCategory? existingCategory = await coreUnitOfWork.Repository<WorkflowTypeCategory>()
            .GetByIdAsync(TestCategoryId);

        if (existingCategory is not null)
        {
            return existingCategory.Id;
        }

        WorkflowTypeCategory? anyCategory = await coreUnitOfWork.Repository<WorkflowTypeCategory>()
            .GetAll()
            .FirstOrDefaultAsync();

        if (anyCategory is not null)
        {
            return anyCategory.Id;
        }

        var category = new WorkflowTypeCategory
        {
            Id = TestCategoryId,
            Name = "Test Workflows",
            Description = "Category for test workflows",
            DisplayOrder = 999
        };
        await coreUnitOfWork.Repository<WorkflowTypeCategory>().AddAsync(category);
        _ = await coreUnitOfWork.CompleteAsync();

        return TestCategoryId;
    }

    /// <summary>
    /// Loads the WorkflowHandlerFunctionalityTest workflow from the JSON file.
    /// </summary>
    private Workflow? LoadWorkflowHandlerTestFromJson(string organizationId)
    {
        string jsonFilePath = Path.Combine(AppContext.BaseDirectory, WORKFLOW_HANDLER_FUNCTIONALITY_TEST_JSON_PATH);

        if (!System.IO.File.Exists(jsonFilePath))
        {
            throw new FileNotFoundException(
                $"Workflow JSON file not found at: {jsonFilePath}. " +
                "Ensure WorkflowHandlerFunctionalityTest.workflow.json is in TestResources folder.");
        }

        string json = System.IO.File.ReadAllText(jsonFilePath);
        var workflow = Workflow.FromJson(json);

        if (workflow is not null)
        {
            workflow.Id = Guid.NewGuid().ToString();
            workflow.WorkflowTypeId = WORKFLOW_HANDLER_FUNCTIONALITY_TEST_TYPE_ID;
            workflow.OrganizationId = organizationId;

            RegenerateWorkflowIds(workflow);
        }

        return workflow;
    }

    /// <summary>
    /// Regenerates IDs for the workflow's sections and blocks.
    /// </summary>
    private static void RegenerateWorkflowIds(Workflow workflow)
    {
        string newWorkflowId = workflow.Id;

        // Regenerate document class IDs
        foreach (DocumentClass documentClass in workflow.DocumentClasses)
        {
            documentClass.Id = Guid.NewGuid().ToString();
            documentClass.WorkflowId = newWorkflowId;
        }

        // Update variable workflow IDs (note: Id is read-only on WorkflowVariable)
        foreach (WorkflowVariable variable in workflow.Variables)
        {
            variable.WorkflowId = newWorkflowId;
        }

        foreach (Section child in workflow.Sections)
        {
            child.Id = Guid.NewGuid().ToString();
            child.WorkflowId = newWorkflowId;

            foreach (Block block in child.Blocks)
            {
                block.Id = Guid.NewGuid().ToString();
                block.SectionId = child.Id;

                foreach (TextInput textInput in block.TextInputs)
                {
                    textInput.Id = Guid.NewGuid().ToString();
                }

                // Clear RagSettings IDs
                foreach (RagSettings ragSetting in block.RagSettings)
                {
                    ragSetting.Id = Guid.NewGuid().ToString();
                }

                block.ExecutionState = null;
                block.BlockResponse = null;
            }
        }
    }

    /// <summary>
    /// Soft-deletes existing test documents and their pages to force fresh document creation.
    /// This ensures embedding and OCR token usage is recorded on new document uploads.
    /// Documents are soft-deleted so GetByIdAsync (which filters by !IsDeleted) won't find them.
    /// </summary>
    private async Task CleanupExistingTestDocumentsAsync(string organizationId)
    {
        (Microsoft.Extensions.DependencyInjection.IServiceScope? scope, UserDataDbContext? dbContext) = _apiBuilder.GetNewScope<UserDataDbContext>();
        using (scope)
        {
            // Find existing test documents by category and soft-delete them
            var testDocumentCategories = DocumentClassToFile.Keys.ToList();
            List<Document> existingDocuments = await dbContext.Documents
                .Include(d => d.Pages)
                .Where(d => d.OrganizationId == organizationId
                    && !d.IsDeleted
                    && testDocumentCategories.Contains(d.DocumentCategoryId ?? ""))
                .ToListAsync();

            if (existingDocuments.Count != 0)
            {
                _output.WriteLine($"Soft-deleting {existingDocuments.Count} existing test documents for fresh creation");
                foreach (Document? doc in existingDocuments)
                {
                    _output.WriteLine($"  Soft-deleting document {doc.Name} with {doc.Pages.Count} pages");

                    // Delete pages first
                    if (doc.Pages.Count != 0)
                    {
                        dbContext.Set<Page>().RemoveRange(doc.Pages);
                    }

                    // Soft-delete document - GetByIdAsync filters by !IsDeleted so it won't be found
                    doc.IsDeleted = true;
                    doc.DeletedAt = DateTime.UtcNow;
                }
            }

            _ = await dbContext.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Uploads test documents with their corresponding document category IDs.
    /// Returns a list of tuples containing (FileId, DocumentCategoryId) for building the workflow command.
    /// </summary>
    private async Task<List<(string FileId, string DocumentClass)>> UploadTestDocumentsAsync(string organizationId, string userId)
    {
        var fileInfos = new List<(string FileId, string DocumentClass)>();

        foreach ((string? documentCategoryId, string? filePath) in DocumentClassToFile)
        {
            string fullPath = Path.Combine(AppContext.BaseDirectory, filePath);
            if (!System.IO.File.Exists(fullPath))
            {
                throw new FileNotFoundException($"Test file not found: {fullPath}");
            }

            IFormFile file = FormFileTestHelper.CreateFromFile(fullPath, "application/pdf");
            var uploadCommand = new UploadFileCommand(
                File: file,
                OrganizationId: organizationId,
                UserId: userId,
                DocumentCategoryId: documentCategoryId
            );

            ErrorOr<FileDto> result = await _apiBuilder.SendAsync(uploadCommand);
            Assert.False(result.IsError, $"Failed to upload {filePath}: {result.FirstError}");
            fileInfos.Add((result.Value.Id, documentCategoryId));
        }

        return fileInfos;
    }

    /// <summary>
    /// Full functional test for WorkflowHandlerFunctionalityTest workflow with documents.
    /// Tests all block types including AiQuery, ExaSearch, Charts, Loops, etc.
    /// </summary>
    [Fact]
    public async Task WorkflowHandlerFunctionalityTest_ShouldCompleteSuccessfully_WithDocuments()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();

        // Arrange - Seed workflow and upload documents
        await EnsureWorkflowHandlerFunctionalityTestSeededAsync(handle.User.OrganizationId);

        // Cleanup existing test documents to force fresh processing (generates embedding token usage)
        await CleanupExistingTestDocumentsAsync(handle.User.OrganizationId);

        List<(string FileId, string DocumentClass)> uploadedFiles = await UploadTestDocumentsAsync(handle.User.OrganizationId, handle.User.Id);
        Assert.Equal(4, uploadedFiles.Count);

        // Get workflow from categories
        List<WorkflowCategoryDto> workflowCategories = await GetValidWorkflowCategoriesAsync(handle.User.OrganizationId, handle.User.Id);
        WorkflowDto? workflow = workflowCategories
            .Where(x => x.Workflows is not null)
            .SelectMany(c => c.Workflows!)
            .FirstOrDefault(w => w.WorkflowTypeId == WORKFLOW_HANDLER_FUNCTIONALITY_TEST_TYPE_ID);

        Assert.True(workflow is not null, $"Workflow {WORKFLOW_HANDLER_FUNCTIONALITY_TEST_TYPE_ID} should be seeded");

        // Get counterparty
        string counterpartyId = await CreateCounterpartyAsync(handle.User.OrganizationId);

        // Build files list with ShouldUseOcr=true for Image/OcrTest class documents
        var files = uploadedFiles.Select(f => new PostDocumentInfoRequest
        {
            FileId = f.FileId,
            DocumentClass = f.DocumentClass,
            ShouldUseOcr = f.DocumentClass is "Image" or "OcrTest" // Enable OCR for image-only and scanned PDFs
        }).ToList();

        // Start workflow execution with documents
        StartWorkflowExecutionCommand command = TestDataFactory.BuildValidStartWorkflowExecution(
            workflowDto: workflow!,
            counterpartyId: counterpartyId,
            userId: handle.User.Id,
            organizationId: handle.User.OrganizationId,
            files: files);

        ErrorOr<WorkflowExecutionDto> startResult = await _apiBuilder.SendAsync(command);
        Assert.False(startResult.IsError, "Starting workflow should succeed");

        string? jobId = startResult.Value.JobId;
        string executionId = startResult.Value.Id;

        Assert.False(string.IsNullOrWhiteSpace(jobId), "Workflow execution should create a job");

        // Act - Wait for job completion
        JobDto job = await PollJobUntilFinishedOrError(jobId!, organizationId: handle.User.OrganizationId, userId: handle.User.Id);

        // Assert - Job completed
        Assert.True(job.State == JobState.Done.ToString(),
            $"Job should complete successfully. State: {job.State}. Error: {job.Error}");
        Assert.Equal(jobId, job.Id);

        // Load and validate workflow execution
        IUserDataUnitOfWork userUnitOfWork = _apiBuilder.GetNewScope<IUserDataUnitOfWork>().Service;
        ICoreUnitOfWork coreUnitOfWork = _apiBuilder.GetNewScope<ICoreUnitOfWork>().Service;

        // Validate source workflow structure
        Workflow? sourceWorkflow = await coreUnitOfWork.Workflows.GetByIdAsync(command.Request.WorkflowId);
        Assert.NotNull(sourceWorkflow);
        Assert.NotEmpty(sourceWorkflow.Sections);

        // Validate user workflow was created
        Workflow? userWorkflow = await userUnitOfWork.Workflows.GetByIdAsync(executionId, TestContext.Current.CancellationToken);
        Assert.NotNull(userWorkflow);
        Assert.NotEmpty(userWorkflow.Sections);

        // Validate workflow execution entity
        var spec = new WorkflowExecutionWithDetailsSpec(executionId);
        WorkflowExecution? workflowExecution = await userUnitOfWork.WorkflowExecutions.GetFirstOrDefaultAsync(spec, TestContext.Current.CancellationToken);
        Assert.NotNull(workflowExecution);

        // Validate the report tree structure
        var reportSpec = new WorkflowReportForExportSpec(executionId);
        WorkflowReport? report = await userUnitOfWork.Repository<WorkflowReport>()
            .GetFirstOrDefaultAsync(reportSpec, TestContext.Current.CancellationToken);
        Assert.NotNull(report);
        Assert.NotEmpty(report.Sections);

        // Output sections expand 1:1 from the source workflow except for parameterized
        // sections, which fan out into one output per parameter value — strictly increasing
        // the count. Asserting `>` (not `!=`) catches both the original "missed expansion"
        // regression AND the "lost a section entirely" regression that a `NotBe` would miss.
        Assert.True(report.Sections.Count > sourceWorkflow.Sections.Count,
            "Output sections should exceed source sections due to parameterized expansion");

        // Validate execution logs exist
        var workflowOutputExecutionLogsSpec = new WorkflowExecutionLogsForWorkflowExecutionSpec(executionId);
        List<WorkflowExecutionLog> workflowOutputExecutionLogs = await userUnitOfWork.Repository<WorkflowExecutionLog>()
            .GetAll(workflowOutputExecutionLogsSpec)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(workflowOutputExecutionLogs);

        // Validate token usage was recorded
        (Microsoft.Extensions.DependencyInjection.IServiceScope? scope, UserDataDbContext? dbContext) = _apiBuilder.GetNewScope<UserDataDbContext>();
        using (scope)
        {
            // Wait a moment for fire-and-forget token usage recording to complete
            await Task.Delay(3000, TestContext.Current.CancellationToken);

            // First check all records (without Family filter) to see what was recorded
            List<Domain.Entities.TokenUsage.TokenUsage> allTokenUsageRecords = await dbContext.TokenUsages
                .Where(t => t.OrganizationId == handle.User.OrganizationId
                    && t.WorkflowExecutionId == executionId)
                .ToListAsync(TestContext.Current.CancellationToken);

            // Output all token usage records for debugging
            _output.WriteLine($"\n=== Token Usage Summary for ExecutionId {executionId} ===");
            _output.WriteLine($"  Organization: {handle.User.OrganizationId}");
            _output.WriteLine($"  Total records (all): {allTokenUsageRecords.Count}");

            foreach (Domain.Entities.TokenUsage.TokenUsage? record in allTokenUsageRecords)
            {
                int inputTokens = record.Components.Where(c => c.ComponentType is PricingComponentType.Input).Sum(c => c.Units);
                int outputTokens = record.Components.Where(c => c.ComponentType is PricingComponentType.Output).Sum(c => c.Units);
                _output.WriteLine($"  - UsageType: {record.UsageType} ({(int)record.UsageType}), Provider: {record.Provider ?? "NULL"}, Family: {record.Family ?? "NULL"}, ModelId: {record.ModelId ?? "NULL"}, InputTokens: {inputTokens}, OutputTokens: {outputTokens}, SectionId: {record.SectionId ?? "NULL"}, BlockType: {record.BlockType}, DocumentId: {record.DocumentId ?? "NULL"}, PageId: {record.PageId ?? "NULL"}");
            }

            // Now filter for records with Family populated
            var tokenUsageRecords = allTokenUsageRecords.Where(t => t.Family is not null).ToList();
            _output.WriteLine($"  Records with Family: {tokenUsageRecords.Count}");
            _output.WriteLine("=== End Token Usage Summary ===\n");

            // If no records with family, but we have records, the issue is in family population
            if (tokenUsageRecords.Count == 0 && allTokenUsageRecords.Count > 0)
            {
                _output.WriteLine("WARNING: Token records exist but Family is null - check ModelIdNormalizer.GetFamily()");
            }

            Assert.True(tokenUsageRecords.Count != 0,
                $"Workflow with LLM blocks should generate token usage records. " +
                $"All records: {allTokenUsageRecords.Count}, Records with Family: {tokenUsageRecords.Count}");

            foreach (Domain.Entities.TokenUsage.TokenUsage? record in tokenUsageRecords)
            {
                Assert.Equal(handle.User.OrganizationId, record.OrganizationId);
                Assert.Equal(executionId, record.WorkflowExecutionId);
                // SectionId is only set for block execution records, not document processing records
                // Document processing (embedding, OCR, overview generation) happens at workflow level
                if (record.BlockType.HasValue)
                {
                    Assert.False(string.IsNullOrEmpty(record.SectionId));
                }

                Assert.NotEmpty(record.Provider);
                Assert.NotEmpty(record.Family);
                Assert.NotEmpty(record.ModelId);
            }
        }
    }

    /// <summary>
    /// Validates that blocks with stop terms are correctly skipped.
    /// </summary>
    [Fact]
    public async Task WorkflowHandlerFunctionalityTest_ShouldSkipStopTermBlocks()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();

        // Arrange
        await EnsureWorkflowHandlerFunctionalityTestSeededAsync(handle.User.OrganizationId);
        List<(string FileId, string DocumentClass)> uploadedFiles = await UploadTestDocumentsAsync(handle.User.OrganizationId, handle.User.Id);

        // Build files list with ShouldUseOcr=true for Image/OcrTest class documents
        var files = uploadedFiles.Select(f => new PostDocumentInfoRequest
        {
            FileId = f.FileId,
            DocumentClass = f.DocumentClass,
            ShouldUseOcr = f.DocumentClass is "Image" or "OcrTest" // Enable OCR for image-only and scanned PDFs
        }).ToList();

        List<WorkflowCategoryDto> workflowCategories = await GetValidWorkflowCategoriesAsync(handle.User.OrganizationId, handle.User.Id);
        WorkflowDto? workflow = workflowCategories
            .Where(x => x.Workflows is not null)
            .SelectMany(c => c.Workflows!)
            .FirstOrDefault(w => w.WorkflowTypeId == WORKFLOW_HANDLER_FUNCTIONALITY_TEST_TYPE_ID);

        string counterpartyId = await CreateCounterpartyAsync(handle.User.OrganizationId);

        StartWorkflowExecutionCommand command = TestDataFactory.BuildValidStartWorkflowExecution(
            workflowDto: workflow!,
            counterpartyId: counterpartyId,
            userId: handle.User.Id,
            organizationId: handle.User.OrganizationId,
            files: files);

        ErrorOr<WorkflowExecutionDto> startResult = await _apiBuilder.SendAsync(command);
        Assert.False(startResult.IsError);

        // Act
        JobDto job = await PollJobUntilFinishedOrError(
            startResult.Value.JobId!,
            organizationId: handle.User.OrganizationId,
            userId: handle.User.Id);

        // Assert - Check logs for skipped blocks
        if (job.State == JobState.Done.ToString())
        {
            IUserDataUnitOfWork userUnitOfWork = _apiBuilder.GetNewScope<IUserDataUnitOfWork>().Service;
            var logsSpec = new WorkflowExecutionLogsForWorkflowExecutionSpec(startResult.Value.Id);
            List<WorkflowExecutionLog> logs = await userUnitOfWork.Repository<WorkflowExecutionLog>()
                .GetAll(logsSpec)
                .ToListAsync(TestContext.Current.CancellationToken);

            // Blocks with "THIS BLOCK SHOULD NOT APPEAR" should be skipped
            var skippedLogs = logs.Where(l => l.BlockName?.Contains("SHOULD NOT") == true).ToList();
            foreach (WorkflowExecutionLog? skippedLog in skippedLogs)
            {
                Assert.Contains(skippedLog.Status, new[] { BlockExecutionStatus.Skipped, BlockExecutionStatus.Failed });
            }
        }
    }

    /// <summary>
    /// Validates execution logs have correct structure and timing.
    /// </summary>
    [Fact]
    public async Task WorkflowHandlerFunctionalityTest_ExecutionLogs_ShouldHaveCorrectStructure()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();

        // Arrange
        await EnsureWorkflowHandlerFunctionalityTestSeededAsync(handle.User.OrganizationId);
        List<(string FileId, string DocumentClass)> uploadedFiles = await UploadTestDocumentsAsync(handle.User.OrganizationId, handle.User.Id);

        // Build files list with ShouldUseOcr=true for Image/OcrTest class documents
        var files = uploadedFiles.Select(f => new PostDocumentInfoRequest
        {
            FileId = f.FileId,
            DocumentClass = f.DocumentClass,
            ShouldUseOcr = f.DocumentClass is "Image" or "OcrTest" // Enable OCR for image-only and scanned PDFs
        }).ToList();

        List<WorkflowCategoryDto> workflowCategories = await GetValidWorkflowCategoriesAsync(handle.User.OrganizationId, handle.User.Id);
        WorkflowDto? workflow = workflowCategories
            .Where(x => x.Workflows is not null)
            .SelectMany(c => c.Workflows!)
            .FirstOrDefault(w => w.WorkflowTypeId == WORKFLOW_HANDLER_FUNCTIONALITY_TEST_TYPE_ID);

        string counterpartyId = await CreateCounterpartyAsync(handle.User.OrganizationId);

        StartWorkflowExecutionCommand command = TestDataFactory.BuildValidStartWorkflowExecution(
            workflowDto: workflow!,
            counterpartyId: counterpartyId,
            userId: handle.User.Id,
            organizationId: handle.User.OrganizationId,
            files: files);

        ErrorOr<WorkflowExecutionDto> startResult = await _apiBuilder.SendAsync(command);
        Assert.False(startResult.IsError);

        // Act
        JobDto job = await PollJobUntilFinishedOrError(
            startResult.Value.JobId!,
            organizationId: handle.User.OrganizationId,
            userId: handle.User.Id);

        // Assert
        if (job.State == JobState.Done.ToString())
        {
            IUserDataUnitOfWork userUnitOfWork = _apiBuilder.GetNewScope<IUserDataUnitOfWork>().Service;
            var logsSpec = new WorkflowExecutionLogsForWorkflowExecutionSpec(startResult.Value.Id);
            List<WorkflowExecutionLog> logs = await userUnitOfWork.Repository<WorkflowExecutionLog>()
                .GetAll(logsSpec)
                .OrderBy(x => x.CreatedAt)
                .ToListAsync(TestContext.Current.CancellationToken);

            Assert.NotEmpty(logs);

            foreach (WorkflowExecutionLog? log in logs)
            {
                Assert.Equal(startResult.Value.Id, log.WorkflowExecutionId);
                Assert.Equal(DateTime.UtcNow, log.CreatedAt, TimeSpan.FromMinutes(30));
                Assert.NotEqual(BlockExecutionStatus.NotExecuted, log.Status);
            }

            // Verify logs are in chronological order
            for (int i = 1; i < logs.Count; i++)
            {
                Assert.True(logs[i].CreatedAt >= logs[i - 1].CreatedAt, "Execution logs should be in chronological order");
            }
        }
    }

    #endregion WorkflowHandlerFunctionalityTest Tests

    #region Token Usage Diagnostic Tests

    /// <summary>
    /// Diagnostic test to query and display all recent token usage records.
    /// This helps verify token usage is being recorded correctly for both LLM and embedding operations.
    /// </summary>
    [Fact]
    public async Task TokenUsage_Diagnostic_ShouldDisplayAllRecentRecords()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();

        // First run a workflow to generate token usage
        await EnsureWorkflowHandlerFunctionalityTestSeededAsync(handle.User.OrganizationId);
        await CleanupExistingTestDocumentsAsync(handle.User.OrganizationId);
        List<(string FileId, string DocumentClass)> uploadedFiles = await UploadTestDocumentsAsync(handle.User.OrganizationId, handle.User.Id);

        List<WorkflowCategoryDto> workflowCategories = await GetValidWorkflowCategoriesAsync(handle.User.OrganizationId, handle.User.Id);
        WorkflowDto? workflow = workflowCategories
            .Where(x => x.Workflows is not null)
            .SelectMany(c => c.Workflows!)
            .FirstOrDefault(w => w.WorkflowTypeId == WORKFLOW_HANDLER_FUNCTIONALITY_TEST_TYPE_ID);

        string counterpartyId = await CreateCounterpartyAsync(handle.User.OrganizationId);

        // Build files list with ShouldUseOcr=true for Image/OcrTest class documents
        var files = uploadedFiles.Select(f => new PostDocumentInfoRequest
        {
            FileId = f.FileId,
            DocumentClass = f.DocumentClass,
            ShouldUseOcr = f.DocumentClass is "Image" or "OcrTest" // Enable OCR for image-only and scanned PDFs
        }).ToList();

        StartWorkflowExecutionCommand command = TestDataFactory.BuildValidStartWorkflowExecution(
            workflowDto: workflow!,
            counterpartyId: counterpartyId,
            userId: handle.User.Id,
            organizationId: handle.User.OrganizationId,
            files: files);

        ErrorOr<WorkflowExecutionDto> startResult = await _apiBuilder.SendAsync(command);
        Assert.False(startResult.IsError, $"Workflow start failed: {startResult.FirstError}");

        JobDto job = await PollJobUntilFinishedOrError(
            startResult.Value.JobId!,
            organizationId: handle.User.OrganizationId,
            userId: handle.User.Id);

        _output.WriteLine($"Job completed with state: {job.State}");
        if (job.State == "Error")
        {
            _output.WriteLine($"Job error: {job.Error}");
        }

        // Wait for fire-and-forget token usage recording
        await Task.Delay(5000, TestContext.Current.CancellationToken);

        // Query all token usage for this organization in the last hour
        (Microsoft.Extensions.DependencyInjection.IServiceScope? scope, UserDataDbContext? dbContext) = _apiBuilder.GetNewScope<UserDataDbContext>();
        using (scope)
        {
            DateTime cutoffTime = DateTime.UtcNow.AddHours(-1);
            List<Domain.Entities.TokenUsage.TokenUsage> allTokenUsage = await dbContext.TokenUsages
                .Where(t => t.OrganizationId == handle.User.OrganizationId && t.CreatedAt > cutoffTime)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync(TestContext.Current.CancellationToken);

            _output.WriteLine($"\n{'=',-80}");
            _output.WriteLine($"TOKEN USAGE RECORDS (Last Hour) - Organization: {handle.User.OrganizationId}");
            _output.WriteLine($"{'=',-80}");
            _output.WriteLine($"Total Records: {allTokenUsage.Count}");
            _output.WriteLine($"{'=',-80}\n");

            // Group by UsageType for summary
            var byUsageType = allTokenUsage.GroupBy(t => t.UsageType).ToList();
            _output.WriteLine("SUMMARY BY USAGE TYPE:");
            foreach (IGrouping<UsageType, Domain.Entities.TokenUsage.TokenUsage>? group in byUsageType)
            {
                int inputTotal = group.Sum(t => t.Components.Where(c => c.ComponentType is PricingComponentType.Input).Sum(c => c.Units));
                int outputTotal = group.Sum(t => t.Components.Where(c => c.ComponentType is PricingComponentType.Output).Sum(c => c.Units));
                _output.WriteLine($"  {group.Key}: {group.Count()} records, Input: {inputTotal}, Output: {outputTotal}");
            }

            _output.WriteLine("");

            // Group by Provider/Family/ModelId
            var byModel = allTokenUsage.GroupBy(t => new { t.Provider, t.Family, t.ModelId }).ToList();
            _output.WriteLine("SUMMARY BY MODEL:");
            foreach (var group in byModel)
            {
                int inputTotal = group.Sum(t => t.Components.Where(c => c.ComponentType is PricingComponentType.Input).Sum(c => c.Units));
                int outputTotal = group.Sum(t => t.Components.Where(c => c.ComponentType is PricingComponentType.Output).Sum(c => c.Units));
                _output.WriteLine($"  Provider: {group.Key.Provider ?? "NULL"}, Family: {group.Key.Family ?? "NULL"}, Model: {group.Key.ModelId ?? "NULL"}");
                _output.WriteLine($"    Count: {group.Count()}, Input: {inputTotal}, Output: {outputTotal}");
            }

            _output.WriteLine("");

            // Detail each record
            _output.WriteLine("DETAILED RECORDS:");
            _output.WriteLine($"{"UsageType",-25} {"Provider",-15} {"Family",-12} {"ModelId",-35} {"Input",-10} {"Output",-10} {"HasSection",-10} {"HasWfExec",-10} {"BlockType",-20}");
            _output.WriteLine(new string('-', 160));

            foreach (Domain.Entities.TokenUsage.TokenUsage? record in allTokenUsage)
            {
                int inputTokens = record.Components.Where(c => c.ComponentType is PricingComponentType.Input).Sum(c => c.Units);
                int outputTokens = record.Components.Where(c => c.ComponentType is PricingComponentType.Output).Sum(c => c.Units);
                _output.WriteLine($"{record.UsageType,-25} {record.Provider ?? "NULL",-15} {record.Family ?? "NULL",-12} {record.ModelId ?? "NULL",-35} {inputTokens,-10} {outputTokens,-10} {(record.SectionId is not null ? "Yes" : "No"),-10} {(record.WorkflowExecutionId is not null ? "Yes" : "No"),-10} {record.BlockType,-20}");
            }

            _output.WriteLine($"\n{'=',-80}");
            _output.WriteLine("END TOKEN USAGE REPORT");
            _output.WriteLine($"{'=',-80}\n");

            // Assertions
            Assert.NotEmpty(allTokenUsage);

            // Check for LLM Block Execution usage (AI blocks)
            var llmUsage = allTokenUsage.Where(t => t.UsageType is UsageType.LlmBlockExecution).ToList();
            _output.WriteLine($"LLM Block Execution records: {llmUsage.Count}");
            Assert.NotEmpty(llmUsage);

            // Check for embedding usage (from document processing)
            var embeddingUsage = allTokenUsage.Where(t =>
                t.UsageType is UsageType.EmbeddingPage or
                UsageType.EmbeddingChunk).ToList();
            _output.WriteLine($"Document Embedding records (Page/Chunk): {embeddingUsage.Count}");

            // Check for Document Overview Generation (from document processing)
            var overviewUsage = allTokenUsage.Where(t => t.UsageType is UsageType.LlmOverviewGeneration).ToList();
            _output.WriteLine($"LLM Overview Generation records: {overviewUsage.Count}");
            Assert.NotEmpty(overviewUsage);

            // Check for OCR usage (from OcrTestDocument.pdf)
            var ocrTranscriptionUsage = allTokenUsage.Where(t => t.UsageType is UsageType.LlmOcrTranscription).ToList();
            var ocrJudgeUsage = allTokenUsage.Where(t => t.UsageType is UsageType.LlmOcrFidelityJudge).ToList();
            _output.WriteLine($"LLM OCR Transcription records: {ocrTranscriptionUsage.Count}");
            _output.WriteLine($"LLM OCR Fidelity Judge records: {ocrJudgeUsage.Count}");
            Assert.NotEmpty(ocrTranscriptionUsage);
            // Judge records appear only while OcrTranscriptionService re-enables the fidelity judge

            // Check for RAG filtering usage (from AiQuery block with RagSettings Type 9 UseTopics)
            var ragFilteringUsage = allTokenUsage.Where(t => t.UsageType is UsageType.LlmRagFiltering).ToList();
            var embeddingQueryUsage = allTokenUsage.Where(t => t.UsageType is UsageType.EmbeddingQuery).ToList();
            _output.WriteLine($"LLM RAG Filtering records: {ragFilteringUsage.Count}");
            _output.WriteLine($"Embedding Query records: {embeddingQueryUsage.Count}");
            // RAG filtering may or may not be generated depending on workflow configuration and document state

            // Verify LLM records have proper structure
            foreach (Domain.Entities.TokenUsage.TokenUsage? record in llmUsage.Take(5))
            {
                Assert.NotEmpty(record.Provider);
                Assert.NotEmpty(record.Family);
                Assert.NotEmpty(record.ModelId);
                Assert.False(string.IsNullOrEmpty(record.SectionId));
                Assert.False(string.IsNullOrEmpty(record.WorkflowExecutionId));
            }

            // Verify overview generation records have proper structure (no SectionId as it's from doc processing)
            foreach (Domain.Entities.TokenUsage.TokenUsage? record in overviewUsage.Take(3))
            {
                Assert.NotEmpty(record.Provider);
                Assert.NotEmpty(record.Family);
                Assert.NotEmpty(record.ModelId);
                Assert.False(string.IsNullOrEmpty(record.DocumentId));
            }

            // Verify OCR records have proper structure
            foreach (Domain.Entities.TokenUsage.TokenUsage? record in ocrTranscriptionUsage.Take(3))
            {
                Assert.NotEmpty(record.Provider);
                Assert.NotEmpty(record.Family);
                Assert.NotEmpty(record.ModelId);
                Assert.False(string.IsNullOrEmpty(record.PageId));
            }

            // Verify RAG filtering records have proper structure
            foreach (Domain.Entities.TokenUsage.TokenUsage? record in ragFilteringUsage.Take(3))
            {
                Assert.NotEmpty(record.Provider);
                Assert.NotEmpty(record.Family);
                Assert.NotEmpty(record.ModelId);
                Assert.False(string.IsNullOrEmpty(record.BlockId));
            }

            // Verify embedding query records have proper structure (if any exist)
            foreach (Domain.Entities.TokenUsage.TokenUsage? record in embeddingQueryUsage.Take(3))
            {
                Assert.NotEmpty(record.Provider);
                Assert.NotEmpty(record.Family);
                Assert.NotEmpty(record.ModelId);
            }

            // Final summary of all usage types covered
            _output.WriteLine("\n=== USAGE TYPE COVERAGE ===");
            _output.WriteLine($"LlmBlockExecution: {llmUsage.Count} records");
            _output.WriteLine($"LlmOverviewGeneration: {overviewUsage.Count} records");
            _output.WriteLine($"LlmOcrTranscription: {ocrTranscriptionUsage.Count} records");
            _output.WriteLine($"LlmOcrFidelityJudge: {ocrJudgeUsage.Count} records");
            _output.WriteLine($"LlmRagFiltering: {ragFilteringUsage.Count} records");
            _output.WriteLine($"EmbeddingPage: {embeddingUsage.Count} records");
            _output.WriteLine($"EmbeddingQuery: {embeddingQueryUsage.Count} records");
        }
    }

    #endregion Token Usage Diagnostic Tests

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
