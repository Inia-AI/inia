using ErrorOr;
using IniaAi.Application.Queries.Counterparties.ListPeerCandidates;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.Counterparty;
using IniaAi.WebApi.IntegrationTests.Builders;
using IniaAi.WebApi.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.WebApi.IntegrationTests.FunctionalTests.Counterparties;

/// <summary>
/// Proves the peer picker's counterparty-type filter against a real relational provider. Every
/// build answers it: the candidate is eligible whether or not a module judges stored financials.
/// </summary>
public class PeerCandidatesTests : IClassFixture<ApiTestFixture>
{
    private const string USABLE_FINANCIALS = """
    {
      "currency": "GBP",
      "units": "Millions",
      "period_kind": { "c1": "FY" },
      "period_end": { "c1": "2024-12-31" },
      "period_label": { "c1": "FY2024" }
    }
    """;

    private readonly ApiTestFixture _fixture;
    private readonly ClientBuilder _apiBuilder;

    public PeerCandidatesTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
        _apiBuilder = fixture.ClientBuilder;
    }

    [Fact]
    public async Task WorkflowMappedToOneCounterpartyType_OffersOnlyThatTypesCounterparties()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        string suffix = Guid.NewGuid().ToString("N");
        string workflowTypeId = $"PeerTypeFilter-{suffix}";
        string mappedTypeId = $"PeerMappedType-{suffix}";
        string otherTypeId = $"PeerOtherType-{suffix}";
        string prefix = $"PeerTyped-{suffix}";

        await SeedWorkflowWithTypeMappingAsync(handle.User.OrganizationId, workflowTypeId, mappedTypeId);
        await SeedCounterpartyAsync(
            handle.User.OrganizationId, $"{prefix}-Right", USABLE_FINANCIALS, mappedTypeId);
        await SeedCounterpartyAsync(
            handle.User.OrganizationId, $"{prefix}-Wrong", USABLE_FINANCIALS, otherTypeId);

        var query = new ListPeerCandidatesQuery(
            handle.User.OrganizationId, "test-user-id", workflowTypeId, prefix);

        ErrorOr<List<PeerCandidateDto>> result = await _apiBuilder.SendAsync(query);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : string.Empty);
        PeerCandidateDto candidate = Assert.Single(result.Value);
        Assert.Equal($"{prefix}-Right", candidate.Name);
        Assert.True(candidate.IsEligible);
    }

    private async Task SeedCounterpartyAsync(
        string organizationId,
        string name,
        string storedFinancials,
        string counterpartyTypeId)
    {
        (IServiceScope? scope, IUserDataUnitOfWork? unitOfWork) = _apiBuilder.GetNewScope<IUserDataUnitOfWork>();
        using (scope)
        {
            var counterparty = new Counterparty(organizationId, name)
            {
                Id = Guid.NewGuid().ToString(),
                LastFinancialAnalysisResult = storedFinancials,
            };

            counterparty.CounterpartyTypes.Add(new CounterpartyType(counterpartyTypeId) { Id = counterpartyTypeId });

            await unitOfWork.Repository<Counterparty>().AddAsync(counterparty);
            _ = await unitOfWork.CompleteAsync();
            _fixture.RegisterCreatedCounterparty(counterparty.Id);
        }
    }

    private async Task SeedWorkflowWithTypeMappingAsync(
        string organizationId,
        string workflowTypeId,
        string mappedCounterpartyTypeId)
    {
        (IServiceScope? scope, ICoreUnitOfWork? coreUnitOfWork) = _apiBuilder.GetNewScope<ICoreUnitOfWork>();
        using (scope)
        {
            var workflowType = new WorkflowType(workflowTypeId, $"Test: {workflowTypeId}")
            {
                CounterpartyTypeMappings =
                [
                    new WorkflowTypeCounterpartyTypeMap
                    {
                        WorkflowTypeId = workflowTypeId,
                        CounterpartyTypeId = mappedCounterpartyTypeId,
                    },
                ],
            };
            await coreUnitOfWork.WorkflowTypes.AddAsync(workflowType);

            await coreUnitOfWork.Repository<WorkflowTypeOrganizationAccess>().AddAsync(
                new WorkflowTypeOrganizationAccess
                {
                    Id = Guid.NewGuid().ToString(),
                    WorkflowTypeId = workflowTypeId,
                    OrganizationId = organizationId,
                });

            var workflow = new Workflow($"Peer Type Filter {workflowTypeId}")
            {
                WorkflowTypeId = workflowTypeId,
                OrganizationId = organizationId,
            };
            var section = new Section("Section 1", workflow);
            _ = new Block(BlockTypes.PeerFinancials, section)
            {
                Name = "Peer Financials",
                ReplacementTag = "peer_financials",
                Properties = new PeerFinancialsProperties(),
            };

            await coreUnitOfWork.Workflows.AddAsync(workflow);
            _ = await coreUnitOfWork.CompleteAsync();
        }
    }
}
