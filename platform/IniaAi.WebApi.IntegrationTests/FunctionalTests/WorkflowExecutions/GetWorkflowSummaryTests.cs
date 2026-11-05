using ErrorOr;
using IniaAi.Application.Queries.Workflows.ListWorkflowCategories;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApi.IntegrationTests.Builders;
using IniaAi.WebApi.IntegrationTests.Fixtures;
using IniaAi.WebApi.Models.Dto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.WebApi.IntegrationTests.FunctionalTests.WorkflowExecutions;

/// <summary>
/// Integration tests for the GetWorkflowSummaryByType query,
/// verifying the IsCounterpartyRequired computed property.
/// </summary>
public class GetWorkflowSummaryTests : IClassFixture<ApiTestFixture>, IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;
    private readonly ClientBuilder _apiBuilder;

    private const string WORKFLOW_TYPE_ID = "GetWorkflowSummaryTest";

    public GetWorkflowSummaryTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
        _apiBuilder = fixture.ClientBuilder;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// A workflow with {Counterparty} placeholders in block Properties
    /// should return IsCounterpartyRequired as true.
    /// </summary>
    [Fact]
    public async Task GetSummary_WorkflowWithCounterparty_ReturnsCounterpartyRequired()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        string typeId = WORKFLOW_TYPE_ID + "_Full";
        await EnsureFullWorkflowSeededAsync(handle.User.OrganizationId, typeId);

        var query = new GetWorkflowSummaryByTypeQuery(handle.User.OrganizationId, typeId);

        ErrorOr<WorkflowDto> result = await _apiBuilder.SendAsync(query);

        Assert.False(result.IsError, $"Expected success but got error: {(result.IsError ? result.FirstError.Description : "")}");
        Assert.True(result.Value.IsCounterpartyRequired);
    }

    /// <summary>
    /// A workflow with no Counterparty placeholders
    /// should return IsCounterpartyRequired as false.
    /// </summary>
    [Fact]
    public async Task GetSummary_WorkflowWithoutCounterparty_ReturnsCounterpartyNotRequired()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        string typeId = WORKFLOW_TYPE_ID + "_Minimal";
        await EnsureMinimalWorkflowSeededAsync(handle.User.OrganizationId, typeId);

        var query = new GetWorkflowSummaryByTypeQuery(handle.User.OrganizationId, typeId);

        ErrorOr<WorkflowDto> result = await _apiBuilder.SendAsync(query);

        Assert.False(result.IsError, $"Expected success but got error: {(result.IsError ? result.FirstError.Description : "")}");
        Assert.False(result.Value.IsCounterpartyRequired);
    }

    /// <summary>
    /// A workflow carrying a Peer Financials block can consume peers, so its input
    /// surface offers the peer picker.
    /// </summary>
    [Fact]
    public async Task GetSummary_WorkflowWithPeerFinancialsBlock_SupportsPeerCompanies()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        string typeId = WORKFLOW_TYPE_ID + "_PeerBlock";
        await EnsurePeerBlockWorkflowSeededAsync(handle.User.OrganizationId, typeId);

        var query = new GetWorkflowSummaryByTypeQuery(handle.User.OrganizationId, typeId);

        ErrorOr<WorkflowDto> result = await _apiBuilder.SendAsync(query);

        Assert.False(result.IsError, $"Expected success but got error: {(result.IsError ? result.FirstError.Description : "")}");
        Assert.True(result.Value.SupportsPeerCompanies);
    }

    /// <summary>
    /// A workflow that names peers only through a PeerCompanies placeholder has no peer
    /// block, but still consumes peers — so it too offers the picker.
    /// </summary>
    [Fact]
    public async Task GetSummary_WorkflowWithPeerCompaniesPlaceholder_SupportsPeerCompanies()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        string typeId = WORKFLOW_TYPE_ID + "_PeerPlaceholder";
        await EnsurePeerPlaceholderWorkflowSeededAsync(handle.User.OrganizationId, typeId);

        var query = new GetWorkflowSummaryByTypeQuery(handle.User.OrganizationId, typeId);

        ErrorOr<WorkflowDto> result = await _apiBuilder.SendAsync(query);

        Assert.False(result.IsError, $"Expected success but got error: {(result.IsError ? result.FirstError.Description : "")}");
        Assert.True(result.Value.SupportsPeerCompanies);
    }

    /// <summary>
    /// A workflow with neither route to peers does not offer the picker.
    /// </summary>
    [Fact]
    public async Task GetSummary_WorkflowWithoutPeerUse_DoesNotSupportPeerCompanies()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        string typeId = WORKFLOW_TYPE_ID + "_Minimal";
        await EnsureMinimalWorkflowSeededAsync(handle.User.OrganizationId, typeId);

        var query = new GetWorkflowSummaryByTypeQuery(handle.User.OrganizationId, typeId);

        ErrorOr<WorkflowDto> result = await _apiBuilder.SendAsync(query);

        Assert.False(result.IsError, $"Expected success but got error: {(result.IsError ? result.FirstError.Description : "")}");
        Assert.False(result.Value.SupportsPeerCompanies);
    }

    /// <summary>
    /// An Agent block keeps its prompt in a property flagged PlaceholderReplacementIgnore, and
    /// authors write agent tags double-braced. Detection must still see the tag there — this is
    /// the shape a reflection-based scan misses, and agent blocks are where peer prose lives.
    /// </summary>
    [Fact]
    public async Task GetSummary_AgentBlockPromptNamingPeers_SupportsPeerCompanies()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        string typeId = WORKFLOW_TYPE_ID + "_AgentPeer";
        await SeedSingleBlockWorkflowAsync(
            handle.User.OrganizationId,
            typeId,
            "Agent Peer Workflow",
            section => new Block(BlockTypes.Agent, section)
            {
                Name = "Peer Analyst",
                ReplacementTag = "peer_analyst",
                Properties = new AgentProperties
                {
                    PromptTemplate = "Compare the target against {{PeerCompanies[1].Name}}",
                },
            });

        var query = new GetWorkflowSummaryByTypeQuery(handle.User.OrganizationId, typeId);

        ErrorOr<WorkflowDto> result = await _apiBuilder.SendAsync(query);

        Assert.False(result.IsError, $"Expected success but got error: {(result.IsError ? result.FirstError.Description : "")}");
        Assert.True(result.Value.SupportsPeerCompanies);
    }

    private async Task EnsurePeerBlockWorkflowSeededAsync(string organizationId, string workflowTypeId)
    {
        await SeedSingleBlockWorkflowAsync(
            organizationId,
            workflowTypeId,
            "Peer Block Workflow",
            section => new Block(BlockTypes.PeerFinancials, section)
            {
                Name = "Peer Financials",
                ReplacementTag = "peer_financials",
                Properties = new PeerFinancialsProperties(),
            });
    }

    private async Task EnsurePeerPlaceholderWorkflowSeededAsync(string organizationId, string workflowTypeId)
    {
        await SeedSingleBlockWorkflowAsync(
            organizationId,
            workflowTypeId,
            "Peer Placeholder Workflow",
            section => new Block(BlockTypes.AiQuery, section)
            {
                Name = "Compare To Peers",
                ReplacementTag = "compare_to_peers",
                Properties = new AiQueryProperties
                {
                    SystemPromptTemplate = "You are an analyst",
                    UserPromptTemplate = "Compare the target against {PeerCompanies[1].Name}",
                },
            });
    }

    private async Task SeedSingleBlockWorkflowAsync(
        string organizationId,
        string workflowTypeId,
        string workflowName,
        Func<Section, Block> addBlock)
    {
        (IServiceScope? scope, ICoreUnitOfWork? coreUnitOfWork) = _apiBuilder.GetNewScope<ICoreUnitOfWork>();
        using (scope)
        {
            await EnsureWorkflowTypeAsync(coreUnitOfWork, workflowTypeId, organizationId);

            Workflow? existingWorkflow = await coreUnitOfWork.Workflows.GetByTypeIdAsync(workflowTypeId);
            if (existingWorkflow is not null)
            {
                coreUnitOfWork.Workflows.Remove(existingWorkflow);
                _ = await coreUnitOfWork.CompleteAsync();
            }

            var workflow = new Workflow(workflowName)
            {
                WorkflowTypeId = workflowTypeId,
                OrganizationId = organizationId,
            };

            var section = new Section("Section 1", workflow);
            _ = addBlock(section);

            await coreUnitOfWork.Workflows.AddAsync(workflow);
            _ = await coreUnitOfWork.CompleteAsync();
        }
    }

    private async Task EnsureFullWorkflowSeededAsync(string organizationId, string workflowTypeId)
    {
        (IServiceScope? scope, ICoreUnitOfWork? coreUnitOfWork) = _apiBuilder.GetNewScope<ICoreUnitOfWork>();
        using (scope)
        {
            await EnsureWorkflowTypeAsync(coreUnitOfWork, workflowTypeId, organizationId);

            Workflow? existingWorkflow = await coreUnitOfWork.Workflows.GetByTypeIdAsync(workflowTypeId);
            if (existingWorkflow is not null)
            {
                coreUnitOfWork.Workflows.Remove(existingWorkflow);
                _ = await coreUnitOfWork.CompleteAsync();
            }

            var workflow = new Workflow("Counterparty Workflow")
            {
                WorkflowTypeId = workflowTypeId,
                OrganizationId = organizationId,
            };

            // Add a section with a block that uses {Counterparty} in Properties
            var section = new Section("Analysis Section", workflow);

            _ = new Block(BlockTypes.Merge, section)
            {
                Name = "Counterparty Merge",
                ReplacementTag = "counterparty_merge",
                Properties = new MergeProperties
                {
                    TextTemplate = "{Counterparty}\n\n{Counterparty.Name}\n\n{Counterparty.Id}",
                },
            };

            await coreUnitOfWork.Workflows.AddAsync(workflow);
            _ = await coreUnitOfWork.CompleteAsync();
        }
    }

    private async Task EnsureMinimalWorkflowSeededAsync(string organizationId, string workflowTypeId)
    {
        (IServiceScope? scope, ICoreUnitOfWork? coreUnitOfWork) = _apiBuilder.GetNewScope<ICoreUnitOfWork>();
        using (scope)
        {
            await EnsureWorkflowTypeAsync(coreUnitOfWork, workflowTypeId, organizationId);

            // Replace workflow with a minimal one (no Counterparty)
            Workflow? existingWorkflow = await coreUnitOfWork.Workflows.GetByTypeIdAsync(workflowTypeId);
            if (existingWorkflow is not null)
            {
                coreUnitOfWork.Workflows.Remove(existingWorkflow);
                _ = await coreUnitOfWork.CompleteAsync();
            }

            var workflow = new Workflow("Minimal Test Workflow")
            {
                WorkflowTypeId = workflowTypeId,
                OrganizationId = organizationId,
            };

            // Add a section with a block that has no Counterparty placeholder
            var section = new Section("Section 1", workflow);

            _ = new Block(BlockTypes.AiQuery, section)
            {
                Name = "Simple Block",
                ReplacementTag = "simple_block",
                Properties = new AiQueryProperties
                {
                    UserPromptTemplate = "Analyze the document",
                    SystemPromptTemplate = "You are an analyst",
                },
            };

            _ = new DocumentClass(workflow, "Report")
            {
                DisplayName = "Report Document",
            };

            await coreUnitOfWork.Workflows.AddAsync(workflow);
            _ = await coreUnitOfWork.CompleteAsync();
        }
    }

    private static async Task EnsureWorkflowTypeAsync(
        ICoreUnitOfWork coreUnitOfWork,
        string workflowTypeId,
        string organizationId)
    {
        bool typeExists = await coreUnitOfWork.WorkflowTypes.ExistsAsync(workflowTypeId);

        if (!typeExists)
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
                    DisplayOrder = 999,
                });
                _ = await coreUnitOfWork.CompleteAsync();
            }

            var workflowType = new WorkflowType(workflowTypeId, $"Test: {workflowTypeId}")
            {
                Description = "Test workflow type",
                CategoryId = categoryId,
            };
            await coreUnitOfWork.WorkflowTypes.AddAsync(workflowType);

            var access = new WorkflowTypeOrganizationAccess
            {
                Id = Guid.NewGuid().ToString(),
                WorkflowTypeId = workflowTypeId,
                OrganizationId = organizationId,
            };
            await coreUnitOfWork.Repository<WorkflowTypeOrganizationAccess>().AddAsync(access);
            _ = await coreUnitOfWork.CompleteAsync();
        }
        else
        {
            // Ensure org access exists
            WorkflowTypeOrganizationAccess? existingAccess = await coreUnitOfWork.Repository<WorkflowTypeOrganizationAccess>()
                .GetAll()
                .Where(a => a.WorkflowTypeId == workflowTypeId && a.OrganizationId == organizationId)
                .FirstOrDefaultAsync();

            if (existingAccess is null)
            {
                var access = new WorkflowTypeOrganizationAccess
                {
                    Id = Guid.NewGuid().ToString(),
                    WorkflowTypeId = workflowTypeId,
                    OrganizationId = organizationId,
                };
                await coreUnitOfWork.Repository<WorkflowTypeOrganizationAccess>().AddAsync(access);
                _ = await coreUnitOfWork.CompleteAsync();
            }
        }
    }
}
