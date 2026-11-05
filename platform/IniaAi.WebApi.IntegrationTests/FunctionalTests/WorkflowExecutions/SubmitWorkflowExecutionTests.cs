using System.Text.Json;
using ErrorOr;
using IniaAi.Application.Commands.WorkflowExecutions.SubmitWorkflowExecution;
using IniaAi.Application.Queries.WorkflowExecutions.GetWorkflowExecution;
using IniaAi.Domain.Core.Interfaces;
using File = System.IO.File;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Requests.WorkflowExecutions;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using IniaAi.WebApi.IntegrationTests.Builders;
using IniaAi.WebApi.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IniaAi.WebApi.IntegrationTests.FunctionalTests.WorkflowExecutions;

/// <summary>
/// Integration tests for the submit workflow execution flow.
/// </summary>
public class SubmitWorkflowExecutionTests : IClassFixture<ApiTestFixture>, IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;
    private readonly ClientBuilder _apiBuilder;

    // Each test seeds its own type: replacing one shared template from every test failed when
    // another writer changed its rows between the load and the delete.
    private readonly string _workflowTypeId = $"SubmitWorkflowExecutionTest_{Guid.NewGuid():N}";

    private const string WorkflowJsonPath = "TestResources/WorkflowHandlerFunctionalityTest.workflow.json";

    // PascalCase ReplacementTag whose camelCased response form (roundTripCasingVar) differs from
    // the tag — the shape that reproduces the edit-and-re-run casing bug.
    private const string RoundTripCasingTag = "RoundTripCasingVar";
    private const string RoundTripCasingDefault = "DEFAULT_NOT_SUPPLIED";

    private static readonly string TestResourcesPath = Path.Combine(
        AppContext.BaseDirectory, "TestResources");

    public SubmitWorkflowExecutionTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
        _apiBuilder = fixture.ClientBuilder;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task SeedWorkflowAsync(string organizationId)
    {
        (IServiceScope? scope, ICoreUnitOfWork? coreUnitOfWork) = _apiBuilder.GetNewScope<ICoreUnitOfWork>();
        using (scope)
        {
            WorkflowTypeCategory? category = await coreUnitOfWork.Repository<WorkflowTypeCategory>()
                .GetAll().FirstOrDefaultAsync();

            string categoryId;
            if (category is not null)
            {
                categoryId = category.Id;
            }
            else
            {
                categoryId = "test-category";
                await coreUnitOfWork.Repository<WorkflowTypeCategory>().AddAsync(new WorkflowTypeCategory
                {
                    Id = categoryId,
                    Name = "Test Workflows",
                    Description = "Category for test workflows",
                    DisplayOrder = 999
                });
                _ = await coreUnitOfWork.CompleteAsync();
            }

            await coreUnitOfWork.WorkflowTypes.AddAsync(new WorkflowType(_workflowTypeId, "Submit Workflow Execution Test")
            {
                Description = "Test workflow",
                CategoryId = categoryId
            });
            await coreUnitOfWork.Repository<WorkflowTypeOrganizationAccess>().AddAsync(new WorkflowTypeOrganizationAccess
            {
                Id = Guid.NewGuid().ToString(),
                WorkflowTypeId = _workflowTypeId,
                OrganizationId = organizationId
            });
            _ = await coreUnitOfWork.CompleteAsync();

            string jsonPath = Path.Combine(AppContext.BaseDirectory, WorkflowJsonPath);
            string json = File.ReadAllText(jsonPath);
            var workflow = Workflow.FromJson(json);

            if (workflow is not null)
            {
                workflow.Id = Guid.NewGuid().ToString();
                workflow.WorkflowTypeId = _workflowTypeId;
                workflow.OrganizationId = organizationId;

                // Add a PascalCase-tagged variable so the re-run casing round-trip has a tag whose
                // camelCased response form (roundTripCasingVar) differs from the canonical tag.
                // Optional + defaulted so it never breaks the other tests that don't supply it.
                _ = new WorkflowVariable(workflow, RoundTripCasingTag)
                {
                    DisplayName = "Round Trip Casing Var",
                    IsRequired = false,
                    Type = WorkflowVariableTypes.String,
                    DefaultValue = RoundTripCasingDefault,
                };

                RegenerateIds(workflow);

                await coreUnitOfWork.Workflows.AddAsync(workflow);
                _ = await coreUnitOfWork.CompleteAsync();
            }
        }
    }

    private static void RegenerateIds(Workflow workflow)
    {
        string wfId = workflow.Id;
        foreach (DocumentClass dc in workflow.DocumentClasses)
        {
            dc.Id = Guid.NewGuid().ToString();
            dc.WorkflowId = wfId;
        }

        foreach (WorkflowVariable v in workflow.Variables)
        {
            v.WorkflowId = wfId;
        }

        foreach (Section child in workflow.Sections)
        {
            child.Id = Guid.NewGuid().ToString();
            child.WorkflowId = wfId;

            foreach (Block block in child.Blocks)
            {
                block.Id = Guid.NewGuid().ToString();
                block.SectionId = child.Id;
                foreach (TextInput ti in block.TextInputs)
                {
                    ti.Id = Guid.NewGuid().ToString();
                }

                foreach (RagSettings rs in block.RagSettings)
                {
                    rs.Id = Guid.NewGuid().ToString();
                }

                block.ExecutionState = null;
                block.BlockResponse = null;
            }
        }
    }

    [Fact]
    public async Task Submit_WithValidRequest_CreatesExecution()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        await SeedWorkflowAsync(handle.User.OrganizationId);

        IFormFile file1 = FormFileTestHelper.CreateFromFile(
            Path.Combine(TestResourcesPath, "elephant.pdf"), "application/pdf");
        IFormFile file2 = FormFileTestHelper.CreateFromFile(
            Path.Combine(TestResourcesPath, "page.pdf"), "application/pdf");

        var metadata = new SubmitWorkflowExecutionRequest
        {
            WorkflowTypeId = _workflowTypeId,
            Files =
            [
                new SubmitFileMetadata { DocumentClass = "Image" },
                new SubmitFileMetadata { DocumentClass = "test_iterations" },
            ],
            Variables = new Dictionary<string, JsonElement>
            {
                ["requiredTestVariable"] = JsonSerializer.SerializeToElement("test-value"),
            },
        };

        var command = new SubmitWorkflowExecutionCommand(
            OrganizationId: handle.User.OrganizationId,
            UserId: handle.User.Id,
            Username: handle.User.UserName,
            Email: handle.User.Email,
            Metadata: metadata,
            Files: [file1, file2]);

        ErrorOr<WorkflowExecutionDto> result = await _apiBuilder.SendAsync(command);

        Assert.False(result.IsError, $"Expected success but got error: {(result.IsError ? result.FirstError.Description : "")}");
        Assert.NotNull(result.Value);
        Assert.False(string.IsNullOrEmpty(result.Value.Id));
        Assert.False(string.IsNullOrEmpty(result.Value.JobId));
    }

    [Fact]
    public async Task Submit_WithInvalidWorkflowId_ReturnsNotFound()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();

        IFormFile file = FormFileTestHelper.CreateFromFile(
            Path.Combine(TestResourcesPath, "elephant.pdf"), "application/pdf");

        var command = new SubmitWorkflowExecutionCommand(
            OrganizationId: handle.User.OrganizationId,
            UserId: handle.User.Id,
            Username: handle.User.UserName,
            Email: handle.User.Email,
            Metadata: new SubmitWorkflowExecutionRequest { WorkflowTypeId = "NonExistent_" + Guid.NewGuid() },
            Files: [file]);

        ErrorOr<WorkflowExecutionDto> result = await _apiBuilder.SendAsync(command);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Submit_WithIdempotencyKey_ReturnsSameExecutionOnRetry()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        await SeedWorkflowAsync(handle.User.OrganizationId);

        string idempotencyKey = $"idem-{Guid.NewGuid()}";

        var metadata = new SubmitWorkflowExecutionRequest
        {
            WorkflowTypeId = _workflowTypeId,
            Files =
            [
                new SubmitFileMetadata { DocumentClass = "Image" },
                new SubmitFileMetadata { DocumentClass = "test_iterations" },
            ],
            Variables = new Dictionary<string, JsonElement>
            {
                ["requiredTestVariable"] = JsonSerializer.SerializeToElement("idem-test"),
            },
        };

        var command1 = new SubmitWorkflowExecutionCommand(
            OrganizationId: handle.User.OrganizationId,
            UserId: handle.User.Id,
            Username: handle.User.UserName,
            Email: handle.User.Email,
            Metadata: metadata,
            Files:
            [
                FormFileTestHelper.CreateFromFile(Path.Combine(TestResourcesPath, "elephant.pdf"), "application/pdf"),
                FormFileTestHelper.CreateFromFile(Path.Combine(TestResourcesPath, "Topics_20_pages.pdf"), "application/pdf")
            ],
            IdempotencyKey: idempotencyKey);

        ErrorOr<WorkflowExecutionDto> result1 = await _apiBuilder.SendAsync(command1);

        var command2 = new SubmitWorkflowExecutionCommand(
            OrganizationId: handle.User.OrganizationId,
            UserId: handle.User.Id,
            Username: handle.User.UserName,
            Email: handle.User.Email,
            Metadata: metadata,
            Files:
            [
                FormFileTestHelper.CreateFromFile(Path.Combine(TestResourcesPath, "elephant.pdf"), "application/pdf"),
                FormFileTestHelper.CreateFromFile(Path.Combine(TestResourcesPath, "Topics_20_pages.pdf"), "application/pdf")
            ],
            IdempotencyKey: idempotencyKey);

        ErrorOr<WorkflowExecutionDto> result2 = await _apiBuilder.SendAsync(command2);

        Assert.False(result1.IsError, $"First failed: {(result1.IsError ? result1.FirstError.Description : "")}");
        Assert.False(result2.IsError, $"Second failed: {(result2.IsError ? result2.FirstError.Description : "")}");
        Assert.Equal(result1.Value.Id, result2.Value.Id);
    }

    [Fact]
    public async Task Submit_WithVariableDefaults_MergesFromWorkflow()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        await SeedWorkflowAsync(handle.User.OrganizationId);

        IFormFile file1 = FormFileTestHelper.CreateFromFile(
            Path.Combine(TestResourcesPath, "elephant.pdf"),
            "application/pdf");

        IFormFile file2 = FormFileTestHelper.CreateFromFile(
            Path.Combine(TestResourcesPath, "Topics_20_pages.pdf"),
            "application/pdf");

        // Only supply the required variable without a default
        var metadata = new SubmitWorkflowExecutionRequest
        {
            WorkflowTypeId = _workflowTypeId,
            Files =
            [
                new SubmitFileMetadata { DocumentClass = "Image" },
                new SubmitFileMetadata { DocumentClass = "test_iterations" },
            ],
            Variables = new Dictionary<string, JsonElement>
            {
                ["requiredTestVariable"] = JsonSerializer.SerializeToElement("supplied-value"),
            },
        };

        var command = new SubmitWorkflowExecutionCommand(
            OrganizationId: handle.User.OrganizationId,
            UserId: handle.User.Id,
            Username: handle.User.UserName,
            Email: handle.User.Email,
            Metadata: metadata,
            Files: [file1, file2]);

        ErrorOr<WorkflowExecutionDto> result = await _apiBuilder.SendAsync(command);

        Assert.False(result.IsError, $"Expected success but got error: {(result.IsError ? result.FirstError.Description : "")}");
        Assert.NotNull(result.Value);
    }

    /// <summary>
    /// End-to-end reproduction of the edit-and-re-run casing bug: submit a value under a PascalCase
    /// variable, read the execution back, camelCase its variable keys through the REAL MVC response
    /// serializer (as the API does on a GET), then resubmit those camelCased keys. The re-run must
    /// bind the submitted value to the canonical tag instead of falling back to the template default.
    /// </summary>
    [Fact]
    public async Task Submit_ReRunAfterCamelCasedResponseKeys_BindsValueNotDefault()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        await SeedWorkflowAsync(handle.User.OrganizationId);

        const string suppliedValue = "supplied-round-trip-value";

        // 1. Fresh submit supplying the PascalCase variable exactly, plus the required variable.
        var firstMetadata = new SubmitWorkflowExecutionRequest
        {
            WorkflowTypeId = _workflowTypeId,
            Files =
            [
                new SubmitFileMetadata { DocumentClass = "Image" },
                new SubmitFileMetadata { DocumentClass = "test_iterations" },
            ],
            Variables = new Dictionary<string, JsonElement>
            {
                ["requiredTestVariable"] = JsonSerializer.SerializeToElement("req"),
                [RoundTripCasingTag] = JsonSerializer.SerializeToElement(suppliedValue),
            },
        };

        var firstCommand = new SubmitWorkflowExecutionCommand(
            OrganizationId: handle.User.OrganizationId,
            UserId: handle.User.Id,
            Username: handle.User.UserName,
            Email: handle.User.Email,
            Metadata: firstMetadata,
            Files:
            [
                FormFileTestHelper.CreateFromFile(Path.Combine(TestResourcesPath, "elephant.pdf"), "application/pdf"),
                FormFileTestHelper.CreateFromFile(Path.Combine(TestResourcesPath, "Topics_20_pages.pdf"), "application/pdf"),
            ]);

        ErrorOr<WorkflowExecutionDto> firstResult = await _apiBuilder.SendAsync(firstCommand);
        Assert.False(firstResult.IsError, $"First submit failed: {(firstResult.IsError ? firstResult.FirstError.Description : "")}");

        // 2. Read the execution back (the GET path) and obtain its persisted variables.
        ErrorOr<WorkflowExecutionDto> fetched = await _apiBuilder.SendAsync(new GetWorkflowExecutionQuery(
            handle.User.OrganizationId, handle.User.Id, firstResult.Value.Id));
        Assert.False(fetched.IsError, $"Get failed: {(fetched.IsError ? fetched.FirstError.Description : "")}");
        Assert.NotNull(fetched.Value.Variables);

        // 3. Serialize those variables with the REAL MVC response serializer — this is what
        //    camelCases the dictionary keys (RoundTripCasingVar -> roundTripCasingVar) on the wire.
        (IServiceScope optionsScope, IOptions<JsonOptions> mvcJsonOptions) =
            _apiBuilder.GetNewScope<IOptions<JsonOptions>>();
        string camelCasedJson;
        using (optionsScope)
        {
            camelCasedJson = JsonSerializer.Serialize(
                fetched.Value.Variables, mvcJsonOptions.Value.JsonSerializerOptions);
        }

        Assert.Contains("roundTripCasingVar", camelCasedJson);

        // 4. Deserialize as the submit controller does (case-insensitive; dictionary keys preserved
        //    as-sent, i.e. still camelCase) and resubmit those keys.
        Dictionary<string, JsonElement> reRunVariables = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            camelCasedJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var secondMetadata = new SubmitWorkflowExecutionRequest
        {
            WorkflowTypeId = _workflowTypeId,
            Files =
            [
                new SubmitFileMetadata { DocumentClass = "Image" },
                new SubmitFileMetadata { DocumentClass = "test_iterations" },
            ],
            Variables = reRunVariables,
        };

        var secondCommand = new SubmitWorkflowExecutionCommand(
            OrganizationId: handle.User.OrganizationId,
            UserId: handle.User.Id,
            Username: handle.User.UserName,
            Email: handle.User.Email,
            Metadata: secondMetadata,
            Files:
            [
                FormFileTestHelper.CreateFromFile(Path.Combine(TestResourcesPath, "elephant.pdf"), "application/pdf"),
                FormFileTestHelper.CreateFromFile(Path.Combine(TestResourcesPath, "Topics_20_pages.pdf"), "application/pdf"),
            ]);

        ErrorOr<WorkflowExecutionDto> secondResult = await _apiBuilder.SendAsync(secondCommand);
        Assert.False(secondResult.IsError, $"Re-run submit failed: {(secondResult.IsError ? secondResult.FirstError.Description : "")}");

        // 5. The re-run must have bound the submitted value, NOT injected the template default.
        ErrorOr<WorkflowExecutionDto> reRunFetched = await _apiBuilder.SendAsync(new GetWorkflowExecutionQuery(
            handle.User.OrganizationId, handle.User.Id, secondResult.Value.Id));
        Assert.False(reRunFetched.IsError, $"Re-run get failed: {(reRunFetched.IsError ? reRunFetched.FirstError.Description : "")}");
        Assert.NotNull(reRunFetched.Value.Variables);

        List<string?> reRunValues = [.. reRunFetched.Value.Variables!.Values
            .Where(v => v.ValueKind == JsonValueKind.String)
            .Select(v => v.GetString())];

        Assert.Contains(suppliedValue, reRunValues);
        Assert.DoesNotContain(RoundTripCasingDefault, reRunValues);
    }
}
