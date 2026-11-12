using ErrorOr;
using IniaAi.Application.Commands.WorkflowExecutions.StartWorkflowExecution;
using IniaAi.Application.Queries.Workflows.ListWorkflowCategories;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.WebApi.Contracts.Dto;
using IniaAi.WebApi.Models.Dto;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using IniaAi.WebApi.IntegrationTests;
using IniaAi.WebApi.IntegrationTests.Builders;
using IniaAi.WebApi.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.WebApi.FunctionalTests.WorkflowExecutions.Authorization;

/// <summary>
/// A run names its counterparty and its peer companies by id in the request body. An id of
/// another organization's counterparty is refused like an id that does not exist, so a run never
/// carries, shows or reads another organization's counterparty.
/// </summary>
public class RunInputsOfAnotherOrganizationTests : IClassFixture<ApiTestFixture>
{
    private const string ORGANIZATION_ID = "IniaAI";
    private const string USER_ID = "User123";
    private const string WORKFLOW_TYPE_ID = "TestWorkflowType";

    private readonly ApiTestFixture _fixture;
    private readonly ClientBuilder _api;

    public RunInputsOfAnotherOrganizationTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
        _api = fixture.ClientBuilder;
    }

    [Fact]
    public async Task ARun_WithACounterpartyAndAPeerOfItsOwnOrganization_Starts()
    {
        string counterpartyId = await AddCounterpartyAsync(ORGANIZATION_ID);
        string peerId = await AddCounterpartyAsync(ORGANIZATION_ID);

        ErrorOr<WorkflowExecutionDto> started = await StartAsync(counterpartyId, [peerId]);

        Assert.False(started.IsError, Describe(started));
        WorkflowExecution run = await ReadRunAsync(started.Value.Id);
        Assert.Equal(counterpartyId, run.CounterpartyId);
        Assert.Equal([peerId], run.WorkflowExecutionsCounterparties.Select(link => link.CounterpartyId));
    }

    [Fact]
    public async Task ARun_NamingAnotherOrganizationsCounterparty_IsRefusedLikeAnUnknownId()
    {
        string foreignCounterpartyId = await AddCounterpartyAsync(await AddOrganizationAsync());

        ErrorOr<WorkflowExecutionDto> foreign = await StartAsync(foreignCounterpartyId, null);
        ErrorOr<WorkflowExecutionDto> unknown = await StartAsync(Guid.NewGuid().ToString(), null);

        Assert.True(unknown.IsError);
        Assert.True(foreign.IsError, "The run started with another organization's counterparty.");
        Assert.Equal(unknown.FirstError.Type, foreign.FirstError.Type);
        Assert.Equal(unknown.FirstError.Code, foreign.FirstError.Code);
    }

    [Fact]
    public async Task ARun_NamingAnotherOrganizationsCounterpartyAsAPeer_IsRefusedLikeAnUnknownId()
    {
        string ownCounterpartyId = await AddCounterpartyAsync(ORGANIZATION_ID);
        string foreignCounterpartyId = await AddCounterpartyAsync(await AddOrganizationAsync());

        ErrorOr<WorkflowExecutionDto> foreign = await StartAsync(ownCounterpartyId, [foreignCounterpartyId]);
        ErrorOr<WorkflowExecutionDto> unknown = await StartAsync(ownCounterpartyId, [Guid.NewGuid().ToString()]);

        Assert.True(unknown.IsError);
        Assert.True(foreign.IsError, "The run started with another organization's counterparty as a peer.");
        Assert.Equal(unknown.FirstError.Type, foreign.FirstError.Type);
        Assert.Equal(unknown.FirstError.Code, foreign.FirstError.Code);
    }

    private async Task<ErrorOr<WorkflowExecutionDto>> StartAsync(string counterpartyId, List<string>? peerCompanyIds)
    {
        ErrorOr<List<WorkflowCategoryDto>> categories = await _api.SendAsync(new ListWorkflowCategoriesQuery(ORGANIZATION_ID, "test-user-id", IncludeWorkflows: true));
        WorkflowDto workflow = categories.Value
            .Where(category => category.Workflows is not null)
            .SelectMany(category => category.Workflows!)
            .First(candidate => candidate.WorkflowTypeId == WORKFLOW_TYPE_ID);
        StartWorkflowExecutionCommand command = TestDataFactory.BuildValidStartWorkflowExecution(
            workflowDto: workflow,
            counterpartyId: counterpartyId,
            userId: USER_ID,
            organizationId: ORGANIZATION_ID,
            peerCompanyIds: peerCompanyIds);

        return await _api.SendAsync(command);
    }

    private async Task<WorkflowExecution> ReadRunAsync(string id)
    {
        using IServiceScope scope = _api.CurrentScope.ServiceProvider.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        return await db.WorkflowExecutions
            .AsNoTracking()
            .Include(run => run.WorkflowExecutionsCounterparties)
            .SingleAsync(run => run.Id == id, TestContext.Current.CancellationToken);
    }

    private async Task<string> AddOrganizationAsync()
    {
        using IServiceScope scope = _api.CurrentScope.ServiceProvider.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        string id = $"other-{Guid.NewGuid():N}";
        _ = db.Organizations.Add(new Organization($"Other organization {id}", id));
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return id;
    }

    private async Task<string> AddCounterpartyAsync(string organizationId)
    {
        using IServiceScope scope = _api.CurrentScope.ServiceProvider.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        var counterparty = new Counterparty(organizationId, $"Counterparty {Guid.NewGuid():N}");
        _ = db.Counterparties.Add(counterparty);
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        _fixture.RegisterCreatedCounterparty(counterparty.Id);

        return counterparty.Id;
    }

    private static string Describe(ErrorOr<WorkflowExecutionDto> result)
    {
        return result.IsError ? string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}")) : string.Empty;
    }
}
