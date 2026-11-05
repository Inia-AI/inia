using ErrorOr;
using IniaAi.Application.Commands.Counterparties.UpdateCounterparty;
using IniaAi.Application.Common.Interfaces.Persistence;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace IniaAi.Application.UnitTests.Commands.Counterparties.UpdateCounterparty;

public class UpdateCounterpartyCommandHandlerTests : IDisposable
{
    private const string ORG_ID = "test-org-update-counterparty";
    private const string TYPE_FUND = "type-hedge-fund";
    private const string TYPE_CORPORATE = "type-corporate";

    private const string STYLE_JPY = "{\"currency\":\"JPY\",\"units\":\"millions\"}";
    private const string STYLE_EUR = "{\"currency\":\"EUR\"}";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly UpdateCounterpartyCommandHandler _handler;

    public UpdateCounterpartyCommandHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();

        _context.CounterpartyTypes.AddRange(
            new CounterpartyType { Id = TYPE_FUND, Name = "Hedge Fund" },
            new CounterpartyType { Id = TYPE_CORPORATE, Name = "Corporate" });
        _ = _context.SaveChanges();
        _context.ChangeTracker.Clear();

        IUserDataUnitOfWork unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        _handler = new UpdateCounterpartyCommandHandler(
            unitOfWork,
            _context,
            Substitute.For<IDbExceptionAnalyzer>());
    }

    public void Dispose()
    {
        _ = _context.Database.EnsureDeleted();
        _context.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Handle_SetsBag_PersistsEveryKeyVerbatim()
    {
        SeedCounterparty("FUND-A", "Fund A", TYPE_FUND);
        UpdateCounterpartyCommand command = BuildCommand(
            "FUND-A",
            "Fund A",
            TYPE_FUND,
            new Dictionary<string, string> { ["reportingStyle"] = STYLE_JPY, ["notes"] = "call the PM" });

        ErrorOr<Updated> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Dictionary<string, string> bag = await ReloadBagAsync("FUND-A");
        Assert.Equal(STYLE_JPY, bag["reportingStyle"]);
        Assert.Equal("call the PM", bag["notes"]);
    }

    [Fact]
    public async Task Handle_NullBag_LeavesStoredBagUnchanged()
    {
        SeedCounterparty(
            "FUND-B",
            "Fund B",
            TYPE_FUND,
            new Dictionary<string, string> { ["reportingStyle"] = STYLE_JPY });
        UpdateCounterpartyCommand command = BuildCommand("FUND-B", "Fund B (renamed)", TYPE_FUND, properties: null);

        ErrorOr<Updated> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Dictionary<string, string> bag = await ReloadBagAsync("FUND-B");
        Assert.Equal(STYLE_JPY, bag["reportingStyle"]);
    }

    [Fact]
    public async Task Handle_EmptyBag_ClearsStoredBag()
    {
        SeedCounterparty(
            "FUND-C",
            "Fund C",
            TYPE_FUND,
            new Dictionary<string, string> { ["reportingStyle"] = STYLE_JPY });
        UpdateCounterpartyCommand command = BuildCommand("FUND-C", "Fund C", TYPE_FUND, new Dictionary<string, string>());

        ErrorOr<Updated> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        _context.ChangeTracker.Clear();
        Counterparty entity = await _context.Counterparties
            .SingleAsync(c => c.Id == "FUND-C", TestContext.Current.CancellationToken);
        Assert.Null(entity.Properties);
    }

    [Fact]
    public async Task Handle_SuppliedBag_ReplacesStoredBagWholesale()
    {
        SeedCounterparty(
            "FUND-D",
            "Fund D",
            TYPE_FUND,
            new Dictionary<string, string> { ["reportingStyle"] = STYLE_JPY, ["stale"] = "drop me" });
        UpdateCounterpartyCommand command = BuildCommand(
            "FUND-D",
            "Fund D",
            TYPE_FUND,
            new Dictionary<string, string> { ["reportingStyle"] = STYLE_EUR });

        ErrorOr<Updated> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Dictionary<string, string> bag = await ReloadBagAsync("FUND-D");
        Assert.Equal(STYLE_EUR, bag["reportingStyle"]);
        Assert.False(bag.ContainsKey("stale"));
    }

    [Fact]
    public async Task Handle_BlankKeys_AreDropped()
    {
        SeedCounterparty("FUND-E", "Fund E", TYPE_FUND);
        UpdateCounterpartyCommand command = BuildCommand(
            "FUND-E",
            "Fund E",
            TYPE_FUND,
            new Dictionary<string, string> { ["   "] = "ignored", ["kept"] = "value" });

        ErrorOr<Updated> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Dictionary<string, string> bag = await ReloadBagAsync("FUND-E");
        Assert.Equal("value", bag["kept"]);
        Assert.Single(bag);
    }

    [Fact]
    public async Task Handle_TakesTheRequestedParent()
    {
        SeedCounterparty("PARENT-A", "Parent A", TYPE_CORPORATE);
        SeedCounterparty("FUND-F", "Fund F", TYPE_FUND, parentId: "PARENT-A");

        ErrorOr<Updated> result = await _handler.Handle(
            BuildCommand("FUND-F", "Fund F", TYPE_FUND, properties: null),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        _context.ChangeTracker.Clear();
        Counterparty entity = await _context.Counterparties.SingleAsync(c => c.Id == "FUND-F", TestContext.Current.CancellationToken);
        Assert.Null(entity.ParentId);
    }

    private async Task<Dictionary<string, string>> ReloadBagAsync(string id)
    {
        _context.ChangeTracker.Clear();
        Counterparty entity = await _context.Counterparties
            .SingleAsync(c => c.Id == id, TestContext.Current.CancellationToken);

        Assert.NotNull(entity.Properties);
        return entity.Properties;
    }

    private static UpdateCounterpartyCommand BuildCommand(
        string id,
        string name,
        string counterpartyTypeId,
        Dictionary<string, string>? properties)
    {
        return new UpdateCounterpartyCommand(
            id,
            ORG_ID,
            null,
            [counterpartyTypeId],
            name,
            string.Empty,
            null,
            null,
            null,
            null,
            null,
            properties);
    }

    private void SeedCounterparty(
        string id,
        string name,
        string counterpartyTypeId,
        Dictionary<string, string>? properties = null,
        string? parentId = null)
    {
        var counterparty = new Counterparty(ORG_ID, name)
        {
            Id = id,
            ParentId = parentId,
            Properties = properties,
        };
        counterparty.CounterpartyTypes.Add(_context.CounterpartyTypes.Single(t => t.Id == counterpartyTypeId));

        _ = _context.Counterparties.Add(counterparty);
        _ = _context.SaveChanges();
        _context.ChangeTracker.Clear();
    }

}
