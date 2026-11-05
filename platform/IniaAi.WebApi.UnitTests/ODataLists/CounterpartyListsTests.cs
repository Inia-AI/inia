using ErrorOr;
using IniaAi.Application.Queries.Counterparties.ListCounterparties;
using IniaAi.Application.Queries.Counterparties.ListCounterpartyTypes;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.Counterparty;
using IniaAi.WebApi.Contracts.Responses.CounterpartyTypes;

namespace IniaAi.WebApi.UnitTests.ODataLists;

public class CounterpartyListsTests : ODataListTests
{
    private const string ORGANIZATION_ID = "org-counterparties";
    private const string HEDGE_FUND_TYPE = "Hedge Fund";
    private const string USER_ID = "user-counterparties";

    private static readonly DateTime _created = new(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task TheCounterpartyList_ReturnsTheSameJson()
    {
        await SeedAsync();

        AssertMatchesFixture(await ListCounterpartiesAsync("?$orderby=name"), "Counterparties");
    }

    [Fact]
    public async Task TheCounterpartyList_FiltersSortsPagesAndCountsInTheQuery()
    {
        await SeedAsync();

        ODataPaginatedResponse<CounterpartyListItemDto> page = await ListCounterpartiesAsync(
            "?$filter=parentId eq 'counterparty-parent' or contains(nameVariants, 'Alpha')&$orderby=name desc&$top=1&$skip=1&$count=true");

        Assert.Equal(["Alpha Child"], page.Value.Select(counterparty => counterparty.Name));
        Assert.Equal(2, page.Count);
    }

    [Fact]
    public async Task TheCounterpartyTypeList_ReturnsTheSameJson()
    {
        await SeedAsync();

        AssertMatchesFixture(await ListCounterpartyTypesAsync("?$orderby=name"), "CounterpartyTypes");
    }

    [Fact]
    public async Task TheCounterpartyTypeList_FiltersSortsPagesAndCountsInTheQuery()
    {
        await SeedAsync();

        ODataPaginatedResponse<CounterpartyTypeListItemDto> page = await ListCounterpartyTypesAsync(
            "?$filter=counterpartyCount gt 0&$orderby=counterpartyCount desc,name&$top=1&$skip=1&$count=true");

        Assert.Equal([HEDGE_FUND_TYPE], page.Value.Select(type => type.Name));
        Assert.Equal(2, page.Count);
    }

    private async Task<ODataPaginatedResponse<CounterpartyListItemDto>> ListCounterpartiesAsync(string query)
    {
        ErrorOr<ODataPaginatedResponse<CounterpartyListItemDto>> result = await Handler<ListCounterpartiesQueryHandler>().Handle(
            new ListCounterpartiesQuery(ORGANIZATION_ID, USER_ID, Options<CounterpartyListItemDto>(query)),
            TestContext.Current.CancellationToken);
        return result.Value;
    }

    private async Task<ODataPaginatedResponse<CounterpartyTypeListItemDto>> ListCounterpartyTypesAsync(string query)
    {
        ErrorOr<ODataPaginatedResponse<CounterpartyTypeListItemDto>> result = await Handler<ListCounterpartyTypesQueryHandler>().Handle(
            new ListCounterpartyTypesQuery(ORGANIZATION_ID, USER_ID, Options<CounterpartyTypeListItemDto>(query)),
            TestContext.Current.CancellationToken);
        return result.Value;
    }

    private async Task SeedAsync()
    {
        var corporation = new CounterpartyType(CounterpartyType.TYPE_CORPORATION) { Id = "type-corporation", Description = "A company." };
        var hedgeFund = new CounterpartyType(HEDGE_FUND_TYPE) { Id = "type-hedge-fund", Description = "A fund." };
        var custom = new CounterpartyType("Insurer") { Id = "type-insurer", Description = "A custom type." };

        Counterparty parent = Build("counterparty-parent", "Parent Holdings", "Parent; Holdings", null, corporation, hedgeFund);
        Counterparty child = Build("counterparty-child", "Alpha Child", "Alpha; Child", parent.Id, corporation);
        Counterparty sibling = Build("counterparty-sibling", "Beta Child", "Beta", parent.Id, hedgeFund);
        Counterparty loose = Build("counterparty-loose", "Gamma Loose", "Gamma", null);
        Counterparty other = Build("counterparty-other", "Other Org", "Alpha", null, corporation);
        other.OrganizationId = "org-other";

        UserData.CounterpartyTypes.AddRange(corporation, hedgeFund, custom);
        UserData.Counterparties.AddRange(parent, child, sibling, loose, other);
        _ = await UserData.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static Counterparty Build(string id, string name, string nameVariants, string? parentId, params CounterpartyType[] types)
    {
        return new Counterparty(ORGANIZATION_ID, name)
        {
            Id = id,
            ParentId = parentId,
            NameVariants = nameVariants,
            Email = $"{id}@example.com",
            PhoneNumber = "+44 20 7946 0000",
            Website = $"https://{id}.example.com",
            Address = "1 Example Street, London",
            LastFinancialAnalysisResult = "Stable",
            CounterpartyTypes = [.. types],
            CreatedAt = _created,
            UpdatedAt = _created,
        };
    }
}
