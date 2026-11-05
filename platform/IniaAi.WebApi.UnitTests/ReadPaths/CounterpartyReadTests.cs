using ErrorOr;
using IniaAi.Application.Commands.Counterparties.CreateCounterparty;
using IniaAi.Application.Commands.Counterparties.UpdateCounterparty;
using IniaAi.Application.Queries.Counterparties.ExportCounterparties;
using IniaAi.Application.Queries.Counterparties.GetCounterparty;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApi.Contracts.Dto;
using IniaAi.WebApi.Contracts.Responses.Counterparty;

namespace IniaAi.WebApi.UnitTests.ReadPaths;

public class CounterpartyReadTests : ReadPathTests
{
    private const string ORGANIZATION_ID = "org-read";
    private const string HEDGE_FUND_TYPE = "Hedge Fund";
    private const string USER_ID = "user-read";
    private const string PARENT_ID = "counterparty-parent";

    [Fact]
    public async Task ACounterparty_ReadsAsTheSameJson()
    {
        await SeedAsync(BuildStored("counterparty-read", "Read Holdings", isDeleted: false));

        ErrorOr<CounterpartyDto> result = await Handler<GetCounterpartyQueryHandler>().Handle(
            new GetCounterpartyQuery("counterparty-read", ORGANIZATION_ID),
            TestContext.Current.CancellationToken);

        AssertMatchesFixture(Sorted(result.Value), "Counterparty");
    }

    [Fact]
    public async Task ACreatedCounterparty_AnswersWithTheSameJson()
    {
        await SeedAsync();

        ErrorOr<CounterpartyDto> result = await Handler<CreateCounterpartyCommandHandler>().Handle(
            CreateCommand("counterparty-created"),
            TestContext.Current.CancellationToken);

        AssertMatchesFixture(Sorted(result.Value), "CounterpartyCreated");
    }

    [Fact]
    public async Task AResurrectedCounterparty_AnswersWithTheSameJson()
    {
        await SeedAsync(BuildStored("counterparty-resurrected", "Deleted Holdings", isDeleted: true));

        ErrorOr<CounterpartyDto> result = await Handler<CreateCounterpartyCommandHandler>().Handle(
            CreateCommand("counterparty-resurrected"),
            TestContext.Current.CancellationToken);

        AssertMatchesFixture(Sorted(result.Value), "CounterpartyResurrected");
    }

    [Fact]
    public async Task AnUpdatedCounterparty_ReadsAsTheSameJson()
    {
        await SeedAsync(BuildStored("counterparty-updated", "Before Holdings", isDeleted: false));

        ErrorOr<Updated> updated = await Handler<UpdateCounterpartyCommandHandler>().Handle(
            new UpdateCounterpartyCommand(
                "counterparty-updated",
                ORGANIZATION_ID,
                PARENT_ID,
                ["type-hedge-fund"],
                "After Holdings",
                "After Holdings; AFTER",
                "after@example.com",
                "+44 20 7946 0001",
                "https://after.example.com",
                "2 Example Street, London",
                "Watch",
                new Dictionary<string, string> { ["Lei"] = "5493001KJTIIGC8Y1R99" }),
            TestContext.Current.CancellationToken);
        Assert.False(updated.IsError);
        UserData.ChangeTracker.Clear();

        ErrorOr<CounterpartyDto> result = await Handler<GetCounterpartyQueryHandler>().Handle(
            new GetCounterpartyQuery("counterparty-updated", ORGANIZATION_ID),
            TestContext.Current.CancellationToken);

        AssertMatchesFixture(Sorted(result.Value), "CounterpartyUpdated");
    }

    [Fact]
    public async Task TheCounterpartyExport_ReadsAsTheSameJson()
    {
        _ = UserData.Organizations.Add(new Organization("Read Org", ORGANIZATION_ID));
        var corporation = new CounterpartyType(CounterpartyType.TYPE_CORPORATION) { Id = "type-corporation", Description = "A company." };
        var hedgeFund = new CounterpartyType(HEDGE_FUND_TYPE) { Id = "type-hedge-fund", Description = "A fund." };
        Counterparty deletedParent = BuildCounterparty("counterparty-deleted", "Deleted Parent", null);
        deletedParent.IsDeleted = true;
        Counterparty parent = BuildCounterparty(PARENT_ID, "Parent Holdings", null, corporation, hedgeFund);
        parent.Properties = new Dictionary<string, string> { ["Lei"] = "5493001KJTIIGC8Y1R12", ["Region"] = "EMEA" };
        UserData.Counterparties.AddRange(
            deletedParent,
            parent,
            BuildCounterparty("counterparty-child", "Child Co", PARENT_ID, corporation),
            BuildCounterparty("counterparty-orphan", "Orphan Co", "counterparty-deleted", hedgeFund));
        _ = await UserData.SaveChangesAsync(TestContext.Current.CancellationToken);
        UserData.ChangeTracker.Clear();

        ErrorOr<CounterpartyBundleDto> result = await Handler<ExportCounterpartiesQueryHandler>().Handle(
            new ExportCounterpartiesQuery(ORGANIZATION_ID, USER_ID, "unit-tests"),
            TestContext.Current.CancellationToken);

        Assert.InRange(result.Value.ExportedAt, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow);
        result.Value.ExportedAt = Created;
        AssertMatchesFixture(result.Value, "CounterpartyExport");
    }

    private static CreateCounterpartyCommand CreateCommand(string id)
    {
        return new CreateCounterpartyCommand(
            id,
            ORGANIZATION_ID,
            PARENT_ID,
            ["type-corporation", "type-hedge-fund"],
            "Created Holdings",
            "Created Holdings; CREATED",
            "created@example.com",
            "+44 20 7946 0002",
            "https://created.example.com",
            "3 Example Street, London",
            "Stable",
            new Dictionary<string, string> { ["Lei"] = "5493001KJTIIGC8Y1R12", ["Region"] = "EMEA" });
    }

    private async Task SeedAsync(params Counterparty[] counterparties)
    {
        _ = UserData.Organizations.Add(new Organization("Read Org", ORGANIZATION_ID));
        var corporation = new CounterpartyType(CounterpartyType.TYPE_CORPORATION) { Id = "type-corporation", Description = "A company." };
        UserData.CounterpartyTypes.AddRange(
            corporation,
            new CounterpartyType(HEDGE_FUND_TYPE) { Id = "type-hedge-fund", Description = "A fund." });
        _ = UserData.Counterparties.Add(BuildCounterparty(PARENT_ID, "Parent Holdings", null));
        foreach (Counterparty counterparty in counterparties)
        {
            counterparty.CounterpartyTypes.Add(corporation);
            _ = UserData.Counterparties.Add(counterparty);
        }

        _ = await UserData.SaveChangesAsync(TestContext.Current.CancellationToken);
        UserData.ChangeTracker.Clear();
    }

    private static Counterparty BuildStored(string id, string name, bool isDeleted)
    {
        Counterparty counterparty = BuildCounterparty(id, name, PARENT_ID);
        counterparty.Properties = new Dictionary<string, string> { ["Lei"] = "5493001KJTIIGC8Y1R00", ["Sector"] = "Banking" };
        counterparty.IsDeleted = isDeleted;
        return counterparty;
    }

    private static Counterparty BuildCounterparty(string id, string name, string? parentId, params CounterpartyType[] types)
    {
        return new Counterparty(ORGANIZATION_ID, name)
        {
            Id = id,
            ParentId = parentId,
            NameVariants = $"{name}; {name.ToUpperInvariant()}",
            Email = $"{id}@example.com",
            PhoneNumber = "+44 20 7946 0000",
            Website = $"https://{id}.example.com",
            Address = "1 Example Street, London",
            LastFinancialAnalysisResult = "Stable",
            CounterpartyTypes = [.. types],
            CreatedAt = Created,
            UpdatedAt = Created,
        };
    }

    private static CounterpartyDto Sorted(CounterpartyDto dto)
    {
        dto.CounterpartyTypeIds = [.. dto.CounterpartyTypeIds.Order(StringComparer.Ordinal)];
        return dto;
    }
}
