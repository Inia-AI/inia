using ErrorOr;
using IniaAi.Application.Queries.Counterparties.ListCounterpartyTypes;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.CounterpartyTypes;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.UnitTests.Queries.CounterpartyTypes;

/// <summary>
/// Every organization shares the counterparty types, so the list must not tell one organization
/// how many counterparties another one has.
/// </summary>
public class ListCounterpartyTypesQueryHandlerTests : IDisposable
{
    private const string ORG_ID = "test-org-type-list";
    private const string OTHER_ORG_ID = "test-org-type-list-other";
    private const string USER_ID = "test-user-type-list";
    private const string TYPE_ID = "type-sovereign";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;

    public ListCounterpartyTypesQueryHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();
    }

    public void Dispose()
    {
        _ = _context.Database.EnsureDeleted();
        _context.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Handle_CountsOnlyTheCallersCounterparties_OfASharedType()
    {
        var type = new CounterpartyType { Id = TYPE_ID, Name = "Sovereign" };
        _ = _context.CounterpartyTypes.Add(type);
        foreach ((string organizationId, string name) in new[]
        {
            (ORG_ID, "Republic of Example"),
            (OTHER_ORG_ID, "Kingdom of Elsewhere"),
            (OTHER_ORG_ID, "Federation of Nowhere"),
        })
        {
            var counterparty = new Counterparty(organizationId, name);
            counterparty.CounterpartyTypes.Add(type);
            _ = _context.Counterparties.Add(counterparty);
        }
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        var handler = new ListCounterpartyTypesQueryHandler(_context, new BuiltInCounterpartyTypes([]));
        ErrorOr<ODataPaginatedResponse<CounterpartyTypeListItemDto>> result = await handler.Handle(
            new ListCounterpartyTypesQuery(ORG_ID, USER_ID, ODataTestOptions.For<CounterpartyTypeListItemDto>("?$top=10")),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        CounterpartyTypeListItemDto listed = Assert.Single(result.Value.Value);
        Assert.Equal(1, listed.CounterpartyCount);
    }
}
