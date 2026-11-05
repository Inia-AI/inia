using ErrorOr;
using IniaAi.Application.Commands.Counterparties.CreateCounterparty;
using IniaAi.Application.Common.Interfaces.Persistence;
using IniaAi.Common.Constants;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses.Counterparty;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace IniaAi.Application.UnitTests.Commands.Counterparties.CreateCounterparty;

public class CreateCounterpartyCommandHandlerTests : IDisposable
{
    private const string ORG_ID = "test-org-create-counterparty";
    private const string OTHER_ORG_ID = "test-org-other";
    private const string TYPE_BROKER_DEALER = "type-broker-dealer";
    private const string TYPE_CORPORATE = "type-corporate";
    private const string TYPE_HEDGE_FUND = "type-hedge-fund";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly CreateCounterpartyCommandHandler _handler;

    public CreateCounterpartyCommandHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();

        _context.CounterpartyTypes.AddRange(
            new CounterpartyType { Id = TYPE_BROKER_DEALER, Name = "Broker-Dealer" },
            new CounterpartyType { Id = TYPE_CORPORATE, Name = "Corporate" },
            new CounterpartyType { Id = TYPE_HEDGE_FUND, Name = "Hedge Fund" });
        _ = _context.SaveChanges();

        IUserDataUnitOfWork unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        _handler = new CreateCounterpartyCommandHandler(unitOfWork, Substitute.For<IDbExceptionAnalyzer>());
    }

    public void Dispose()
    {
        _ = _context.Database.EnsureDeleted();
        _context.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Handle_WithFreshId_CreatesCounterparty()
    {
        // Arrange
        CreateCounterpartyCommand command = BuildCommand("WEDBUSH-SECURITIES", "Wedbush Securities", TYPE_BROKER_DEALER);

        // Act
        ErrorOr<CounterpartyDto> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsError);
        Assert.Equal("WEDBUSH-SECURITIES", result.Value.Id);

        // Clear tracked instances so the assertions read persisted state, not the handler's in-memory entity
        _context.ChangeTracker.Clear();
        Counterparty? entity = await _context.Counterparties
            .Include(c => c.CounterpartyTypes)
            .SingleOrDefaultAsync(c => c.Id == "WEDBUSH-SECURITIES", TestContext.Current.CancellationToken);
        Assert.NotNull(entity);
        Assert.Equal("Wedbush Securities", entity.Name);
        Assert.Single(entity.CounterpartyTypes);
        Assert.Equal(TYPE_BROKER_DEALER, entity.CounterpartyTypes.Single().Id);
    }

    [Fact]
    public async Task Handle_WhenIdHeldBySoftDeletedCounterpartyInSameOrganization_ResurrectsIt()
    {
        // Arrange
        SeedCounterparty("HALDEN-METALS-AG", ORG_ID, "Halden Metals AG (old)", TYPE_CORPORATE, isDeleted: true);
        CreateCounterpartyCommand command = BuildCommand("HALDEN-METALS-AG", "Halden Metals AG", TYPE_BROKER_DEALER);

        // Act
        ErrorOr<CounterpartyDto> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsError);
        Assert.Equal("HALDEN-METALS-AG", result.Value.Id);

        // Clear tracked instances so the assertions read persisted state, not the handler's in-memory entity
        _context.ChangeTracker.Clear();
        List<Counterparty> rows = await _context.Counterparties
            .IgnoreQueryFilters()
            .Include(c => c.CounterpartyTypes)
            .Where(c => c.Id == "HALDEN-METALS-AG")
            .ToListAsync(TestContext.Current.CancellationToken);
        Counterparty entity = Assert.Single(rows);
        Assert.False(entity.IsDeleted);
        Assert.Null(entity.DeletedAt);
        Assert.Null(entity.DeletedByUserId);
        Assert.Equal("Halden Metals AG", entity.Name);
        Assert.Equal(TYPE_BROKER_DEALER, entity.CounterpartyTypes.Single().Id);
    }

    [Fact]
    public async Task Handle_WhenResurrecting_TakesTheRequestedParent()
    {
        SeedCounterparty("PARENT-B", ORG_ID, "Parent B", TYPE_CORPORATE, isDeleted: false);
        SeedCounterparty("CHILD-B", ORG_ID, "Child B (old)", TYPE_CORPORATE, isDeleted: true, parentId: "PARENT-B");

        ErrorOr<CounterpartyDto> result = await _handler.Handle(
            BuildCommand("CHILD-B", "Child B", TYPE_CORPORATE),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Null(result.Value.ParentId);
        _context.ChangeTracker.Clear();
        Counterparty entity = await _context.Counterparties.SingleAsync(c => c.Id == "CHILD-B", TestContext.Current.CancellationToken);
        Assert.Null(entity.ParentId);
    }

    [Fact]
    public async Task Handle_WhenIdHeldByActiveCounterpartyInSameOrganization_ReturnsConflictWithGuidance()
    {
        // Arrange
        SeedCounterparty("WEDBUSH-SECURITIES", ORG_ID, "Wedbush Securities", TYPE_CORPORATE, isDeleted: false);
        CreateCounterpartyCommand command = BuildCommand("WEDBUSH-SECURITIES", "Wedbush Securities", TYPE_BROKER_DEALER);

        // Act
        ErrorOr<CounterpartyDto> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Conflict, error.Type);
        Assert.Equal("Counterparty.DuplicateId", error.Code);
        Assert.Contains("WEDBUSH-SECURITIES", error.Description);
        Assert.Contains($"{CounterpartyTerms.PLURAL} tab", error.Description);
    }

    [Fact]
    public async Task Handle_WhenIdHeldByCounterpartyInAnotherOrganization_ReturnsConflictWithoutResurrecting()
    {
        // Arrange
        SeedCounterparty("HALDEN-METALS-AG", OTHER_ORG_ID, "Halden Metals AG", TYPE_CORPORATE, isDeleted: true);
        CreateCounterpartyCommand command = BuildCommand("HALDEN-METALS-AG", "Halden Metals AG", TYPE_BROKER_DEALER);

        // Act
        ErrorOr<CounterpartyDto> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Conflict, error.Type);
        Assert.Equal("Counterparty.DuplicateId", error.Code);
        Assert.DoesNotContain("your organization", error.Description);

        _context.ChangeTracker.Clear();
        Counterparty? entity = await _context.Counterparties
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(c => c.Id == "HALDEN-METALS-AG", TestContext.Current.CancellationToken);
        Assert.NotNull(entity);
        Assert.True(entity.IsDeleted);
        Assert.Equal(OTHER_ORG_ID, entity.OrganizationId);
    }

    [Fact]
    public async Task Handle_WithProperties_PersistsBagAndReturnsItInDto()
    {
        // Arrange
        CreateCounterpartyCommand command = BuildCommand("AKITO-FUND", "Akito Fund", TYPE_HEDGE_FUND) with
        {
            Properties = new Dictionary<string, string>
            {
                ["reportingStyle"] = "{\"currency\":\"JPY\"}",
                ["notes"] = "quarterly filing",
            },
        };

        // Act
        ErrorOr<CounterpartyDto> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsError);
        Assert.NotNull(result.Value.Properties);
        Assert.Equal("{\"currency\":\"JPY\"}", result.Value.Properties!["reportingStyle"]);
        Assert.Equal("quarterly filing", result.Value.Properties!["notes"]);

        _context.ChangeTracker.Clear();
        Counterparty? entity = await _context.Counterparties
            .SingleOrDefaultAsync(c => c.Id == "AKITO-FUND", TestContext.Current.CancellationToken);
        Assert.NotNull(entity);
        Assert.NotNull(entity.Properties);
        Assert.Equal("{\"currency\":\"JPY\"}", entity.Properties!["reportingStyle"]);
    }

    [Fact]
    public async Task Handle_WithoutProperties_LeavesPropertiesNull()
    {
        // Arrange
        CreateCounterpartyCommand command = BuildCommand("PLAIN-FUND", "Plain Fund", TYPE_HEDGE_FUND);

        // Act
        ErrorOr<CounterpartyDto> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsError);
        Assert.Null(result.Value.Properties);

        _context.ChangeTracker.Clear();
        Counterparty? entity = await _context.Counterparties
            .SingleOrDefaultAsync(c => c.Id == "PLAIN-FUND", TestContext.Current.CancellationToken);
        Assert.NotNull(entity);
        Assert.Null(entity.Properties);
    }

    private static CreateCounterpartyCommand BuildCommand(string id, string name, string counterpartyTypeId)
    {
        return new CreateCounterpartyCommand(
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
            null);
    }

    private void SeedCounterparty(string id, string organizationId, string name, string counterpartyTypeId, bool isDeleted, string? parentId = null)
    {
        var counterparty = new Counterparty(organizationId, name)
        {
            Id = id,
            ParentId = parentId,
            IsDeleted = isDeleted,
            DeletedAt = isDeleted ? DateTime.UtcNow : null,
        };
        counterparty.CounterpartyTypes.Add(_context.CounterpartyTypes.Single(t => t.Id == counterpartyTypeId));

        _ = _context.Counterparties.Add(counterparty);
        _ = _context.SaveChanges();
        _context.ChangeTracker.Clear();
    }

}
