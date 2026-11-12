using ErrorOr;
using IniaAi.Application.Commands.Counterparties.CreateCounterparty;
using IniaAi.Application.Commands.Counterparties.UpdateCounterparty;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.WebApi.Contracts.Responses.Counterparty;
using IniaAi.WebApi.IntegrationTests;
using IniaAi.WebApi.IntegrationTests.Builders;
using IniaAi.WebApi.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.WebApi.FunctionalTests.Counterparties.Authorization;

/// <summary>
/// A counterparty names its parent by id in the request body. A parent that belongs to another
/// organization is refused like one that does not exist, so no counterparty hangs under another
/// organization's.
/// </summary>
public class ParentOfAnotherOrganizationTests : IClassFixture<ApiTestFixture>, IAsyncLifetime
{
    private const string ORGANIZATION_ID = "IniaAI";

    private readonly ApiTestFixture _fixture;
    private readonly ClientBuilder _api;
    private readonly List<string> _made = [];

    public ParentOfAnotherOrganizationTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
        _api = fixture.ClientBuilder;
    }

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>Detaches every child first: the fixture deletes the rows in the order they were made, parents before children.</summary>
    public async ValueTask DisposeAsync()
    {
        using IServiceScope scope = _api.CurrentScope.ServiceProvider.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        _ = await db.Counterparties
            .IgnoreQueryFilters()
            .Where(row => row.ParentId != null && _made.Contains(row.ParentId))
            .ExecuteUpdateAsync(rows => rows.SetProperty(row => row.ParentId, (string?)null));
    }

    [Fact]
    public async Task ACounterparty_IsCreatedAndMovedUnderAParentOfItsOwnOrganization()
    {
        string firstParentId = await AddCounterpartyAsync(ORGANIZATION_ID);
        string secondParentId = await AddCounterpartyAsync(ORGANIZATION_ID);

        ErrorOr<CounterpartyDto> created = await _api.SendAsync(await CreateAsync(firstParentId));
        Assert.False(created.IsError, Describe(created.ErrorsOrEmptyList));
        _fixture.RegisterCreatedCounterparty(created.Value.Id);
        ErrorOr<Updated> moved = await _api.SendAsync(await UpdateAsync(created.Value.Id, secondParentId));

        Assert.False(moved.IsError, Describe(moved.ErrorsOrEmptyList));
        Assert.Equal(secondParentId, await ParentOfAsync(created.Value.Id));
    }

    [Fact]
    public async Task ACreate_NamingAnotherOrganizationsCounterpartyAsParent_IsRefusedLikeAnUnknownParent()
    {
        string foreignParentId = await AddCounterpartyAsync(await AddOrganizationAsync());

        ErrorOr<CounterpartyDto> foreign = await _api.SendAsync(await CreateAsync(foreignParentId));
        ErrorOr<CounterpartyDto> unknown = await _api.SendAsync(await CreateAsync(Guid.NewGuid().ToString()));

        Assert.True(unknown.IsError);
        Assert.True(foreign.IsError, "The counterparty was created under another organization's counterparty.");
        Assert.Equal((unknown.FirstError.Type, unknown.FirstError.Code), (foreign.FirstError.Type, foreign.FirstError.Code));
    }

    [Fact]
    public async Task AnUpdate_NamingAnotherOrganizationsCounterpartyAsParent_IsRefusedAndChangesNothing()
    {
        string ownParentId = await AddCounterpartyAsync(ORGANIZATION_ID);
        string childId = await AddCounterpartyAsync(ORGANIZATION_ID, ownParentId);
        string foreignParentId = await AddCounterpartyAsync(await AddOrganizationAsync());

        ErrorOr<Updated> foreign = await _api.SendAsync(await UpdateAsync(childId, foreignParentId));
        ErrorOr<Updated> unknown = await _api.SendAsync(await UpdateAsync(childId, Guid.NewGuid().ToString()));

        Assert.True(unknown.IsError);
        Assert.True(foreign.IsError, "The counterparty was moved under another organization's counterparty.");
        Assert.Equal((unknown.FirstError.Type, unknown.FirstError.Code), (foreign.FirstError.Type, foreign.FirstError.Code));
        Assert.Equal(ownParentId, await ParentOfAsync(childId));
    }

    private async Task<CreateCounterpartyCommand> CreateAsync(string parentId)
    {
        return TestDataFactory.BuildValidCreateCounterparty(counterpartyTypeId: await AddCounterpartyTypeAsync()) with { ParentId = parentId };
    }

    private async Task<UpdateCounterpartyCommand> UpdateAsync(string id, string parentId)
    {
        CreateCounterpartyCommand fields = TestDataFactory.BuildValidCreateCounterparty(counterpartyTypeId: await AddCounterpartyTypeAsync());

        return new UpdateCounterpartyCommand(id, ORGANIZATION_ID, parentId, fields.CounterpartyTypeIds, fields.Name, fields.NameVariants, fields.Email, fields.PhoneNumber, fields.Website, fields.Address, null);
    }

    private async Task<string> AddCounterpartyTypeAsync()
    {
        using IServiceScope scope = _api.CurrentScope.ServiceProvider.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        CounterpartyType type = TestDataFactory.BuildValidCounterpartyType();
        type.Name = $"Type-{Guid.NewGuid():N}";
        _ = db.Set<CounterpartyType>().Add(type);
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return type.Id;
    }

    private static string Describe(List<Error> errors)
    {
        return string.Join("; ", errors.Select(error => $"{error.Code}: {error.Description}"));
    }

    private async Task<string?> ParentOfAsync(string id)
    {
        using IServiceScope scope = _api.CurrentScope.ServiceProvider.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        return await db.Counterparties.AsNoTracking().Where(row => row.Id == id).Select(row => row.ParentId).SingleAsync(TestContext.Current.CancellationToken);
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

    private async Task<string> AddCounterpartyAsync(string organizationId, string? parentId = null)
    {
        using IServiceScope scope = _api.CurrentScope.ServiceProvider.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        var counterparty = new Counterparty(organizationId, $"Counterparty {Guid.NewGuid():N}") { ParentId = parentId };
        _ = db.Counterparties.Add(counterparty);
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        _fixture.RegisterCreatedCounterparty(counterparty.Id);
        _made.Add(counterparty.Id);

        return counterparty.Id;
    }
}
