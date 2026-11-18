using ErrorOr;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.Counterparties.ListPeerCandidates;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses.Counterparty;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.UnitTests.Queries.Counterparties.ListPeerCandidates;

/// <summary>
/// Only the PeerFinancials module judges a counterparty's stored financials. A deployment
/// without it still lists the counterparties of the accepted types for a workflow that uses
/// <c>{PeerCompanies}</c>, and disables none of them for their stored financials.
/// </summary>
public class ListPeerCandidatesOpenBuildTests : IDisposable
{
    private const string ORGANIZATION_ID = "org-open-peers";
    private const string USER_ID = "user-1";
    private const string WORKFLOW_TYPE_ID = "OpenPeerComparison";
    private const string BANK_TYPE_ID = "bank";
    private const string FUND_TYPE_ID = "fund";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _userData;
    private readonly CoreDbContext _core;

    public ListPeerCandidatesOpenBuildTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();
        _ = services.AddSeamDefaults();
        _serviceProvider = services.BuildServiceProvider();
        _userData = _serviceProvider.GetRequiredService<UserDataDbContext>();
        _core = _serviceProvider.GetRequiredService<CoreDbContext>();
    }

    [Fact]
    public async Task ACounterpartyWithNoStoredAnalysis_IsEligible()
    {
        SeedCounterparty("Never Analysed", BANK_TYPE_ID);

        PeerCandidateDto candidate = Assert.Single(await RunAsync(workflowTypeId: null));

        Assert.True(candidate.IsEligible);
    }

    [Fact]
    public async Task AWorkflowUsingPeerCompanies_ListsItsAcceptedTypes_AndDisablesNone()
    {
        SeedCounterparty("Analysed Bank", BANK_TYPE_ID, """{ "currency": "EUR" }""");
        SeedCounterparty("Unread Bank", BANK_TYPE_ID, "{not json");
        SeedCounterparty("New Bank", BANK_TYPE_ID);
        SeedCounterparty("A Fund", FUND_TYPE_ID);
        SeedWorkflowUsingPeerCompanies(acceptedTypeId: BANK_TYPE_ID);

        List<PeerCandidateDto> candidates = await RunAsync(WORKFLOW_TYPE_ID);

        Assert.Equal(["Analysed Bank", "New Bank", "Unread Bank"], candidates.Select(candidate => candidate.Name));
        Assert.All(candidates, candidate => Assert.True(candidate.IsEligible));
    }

    private async Task<List<PeerCandidateDto>> RunAsync(string? workflowTypeId)
    {
        var handler = new ListPeerCandidatesQueryHandler(
            _serviceProvider.GetRequiredService<IUserDataUnitOfWork>(),
            _serviceProvider.GetRequiredService<ICoreUnitOfWork>(),
            _serviceProvider.GetRequiredService<WorkflowTypeAccess>(),
            _serviceProvider.GetRequiredService<IPeerCandidateJudge>());

        ErrorOr<List<PeerCandidateDto>> result = await handler.Handle(
            new ListPeerCandidatesQuery(ORGANIZATION_ID, USER_ID, workflowTypeId, Search: null),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);

        return result.Value;
    }

    private void SeedCounterparty(string name, string counterpartyTypeId, string? storedFinancials = null)
    {
        var counterparty = new Counterparty(ORGANIZATION_ID, name)
        {
            LastFinancialAnalysisResult = storedFinancials,
        };
        CounterpartyType counterpartyType = _userData.CounterpartyTypes.Find(counterpartyTypeId)
            ?? new CounterpartyType(counterpartyTypeId) { Id = counterpartyTypeId };
        counterparty.CounterpartyTypes.Add(counterpartyType);

        _ = _userData.Counterparties.Add(counterparty);
        _ = _userData.SaveChanges();
        _userData.ChangeTracker.Clear();
    }

    private void SeedWorkflowUsingPeerCompanies(string acceptedTypeId)
    {
        var workflowType = new WorkflowType(WORKFLOW_TYPE_ID, "Open Peer Comparison")
        {
            CounterpartyTypeMappings =
            [
                new WorkflowTypeCounterpartyTypeMap
                {
                    WorkflowTypeId = WORKFLOW_TYPE_ID,
                    CounterpartyTypeId = acceptedTypeId,
                },
            ],
        };
        workflowType.OrganizationAccess.Add(new WorkflowTypeOrganizationAccess
        {
            Id = Guid.NewGuid().ToString(),
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            OrganizationId = ORGANIZATION_ID,
        });
        _ = _core.WorkflowTypes.Add(workflowType);

        var workflow = new Workflow("Open Peer Comparison")
        {
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            OrganizationId = ORGANIZATION_ID,
        };
        var section = new Section("Section 1", workflow);
        _ = new Block(BlockTypes.AiQuery, section)
        {
            Name = "Peer summary",
            ReplacementTag = "peer_summary",
            Properties = new AiQueryProperties { UserPromptTemplate = "Compare against {PeerCompanies}." },
        };

        _ = _core.Workflows.Add(workflow);
        _ = _core.SaveChanges();
        _core.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        _userData.Dispose();
        _core.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}
