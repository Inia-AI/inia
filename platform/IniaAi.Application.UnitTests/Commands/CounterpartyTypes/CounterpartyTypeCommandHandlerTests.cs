using ErrorOr;
using IniaAi.Application.Commands.CounterpartyTypes.CreateCounterpartyType;
using IniaAi.Application.Commands.CounterpartyTypes.DeleteCounterpartyType;
using IniaAi.Application.Commands.CounterpartyTypes.UpdateCounterpartyType;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses.CounterpartyTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Commands.CounterpartyTypes;

/// <summary>
/// Counterparty types are shared by every organization. An editor adds, renames, redescribes and
/// deletes them; the built-in types keep their names and stay, because monitoring finds them by name.
/// </summary>
public class CounterpartyTypeCommandHandlerTests : IDisposable
{
    private const string CORPORATION_DESCRIPTION = "A company acting as a single legal entity";
    private const string ORG_ID = "test-org-counterparty-types";
    private const string OTHER_ORG_ID = "test-org-counterparty-types-other";
    private const string USER_ID = "test-user-counterparty-types";
    private const string CUSTOM_ID = "type-custom";
    private const string CUSTOM_NAME = "Sovereign";
    private const string BUILT_IN_ID = "type-corporation";
    private const string ANOTHER_ID = "type-another";
    private const string ANOTHER_NAME = "Municipality";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly IUserDataUnitOfWork _unitOfWork;
    private readonly BuiltInCounterpartyTypes _builtIn = new([]);

    public CounterpartyTypeCommandHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();
        _unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();

        _context.CounterpartyTypes.AddRange(
            new CounterpartyType { Id = CUSTOM_ID, Name = CUSTOM_NAME, Description = "A state borrower" },
            new CounterpartyType { Id = BUILT_IN_ID, Name = CounterpartyType.TYPE_CORPORATION },
            new CounterpartyType { Id = ANOTHER_ID, Name = ANOTHER_NAME });
        _ = _context.SaveChanges();
        _context.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        _ = _context.Database.EnsureDeleted();
        _context.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Create_AddsTheType_AndReturnsItWithNoCounterparties()
    {
        ErrorOr<CounterpartyTypeListItemDto> result = await CreateHandler().Handle(
            new CreateCounterpartyTypeCommand(ORG_ID, USER_ID, "  Supranational  ", "Development banks"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal("Supranational", result.Value.Name);
        Assert.Equal("Development banks", result.Value.Description);
        Assert.Equal(0, result.Value.CounterpartyCount);
        Assert.False(result.Value.IsBuiltIn);
        CounterpartyType stored = await _context.CounterpartyTypes.AsNoTracking()
            .SingleAsync(t => t.Id == result.Value.Id, TestContext.Current.CancellationToken);
        Assert.Equal("Supranational", stored.Name);
    }

    [Fact]
    public async Task Create_RefusesANameAnotherTypeHas_IgnoringCase()
    {
        ErrorOr<CounterpartyTypeListItemDto> result = await CreateHandler().Handle(
            new CreateCounterpartyTypeCommand(ORG_ID, USER_ID, CUSTOM_NAME.ToUpperInvariant(), null),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
    }

    [Fact]
    public async Task Update_RenamesAndRedescribesACustomType()
    {
        ErrorOr<CounterpartyTypeListItemDto> result = await UpdateHandler().Handle(
            new UpdateCounterpartyTypeCommand(ORG_ID, USER_ID, CUSTOM_ID, "Sovereign Issuer", "A state that issues debt"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        CounterpartyType stored = await _context.CounterpartyTypes.AsNoTracking()
            .SingleAsync(t => t.Id == CUSTOM_ID, TestContext.Current.CancellationToken);
        Assert.Equal("Sovereign Issuer", stored.Name);
        Assert.Equal("A state that issues debt", stored.Description);
    }

    [Fact]
    public async Task ADescription_IsStoredTrimmed_AndABlankOneAsNone()
    {
        ErrorOr<CounterpartyTypeListItemDto> created = await CreateHandler().Handle(
            new CreateCounterpartyTypeCommand(ORG_ID, USER_ID, "Supranational", "  Development banks  "),
            TestContext.Current.CancellationToken);
        ErrorOr<CounterpartyTypeListItemDto> updated = await UpdateHandler().Handle(
            new UpdateCounterpartyTypeCommand(ORG_ID, USER_ID, CUSTOM_ID, "Sovereign Issuer", "   "),
            TestContext.Current.CancellationToken);

        Assert.Equal("Development banks", created.Value.Description);
        Assert.Null(updated.Value.Description);
        CounterpartyType stored = await _context.CounterpartyTypes.AsNoTracking()
            .SingleAsync(t => t.Id == CUSTOM_ID, TestContext.Current.CancellationToken);
        Assert.Null(stored.Description);
    }

    [Fact]
    public async Task Update_RedescribesABuiltInType_ButRefusesToRenameIt()
    {
        ErrorOr<CounterpartyTypeListItemDto> redescribed = await UpdateHandler().Handle(
            new UpdateCounterpartyTypeCommand(ORG_ID, USER_ID, BUILT_IN_ID, CounterpartyType.TYPE_CORPORATION, CORPORATION_DESCRIPTION),
            TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();
        ErrorOr<CounterpartyTypeListItemDto> renamed = await UpdateHandler().Handle(
            new UpdateCounterpartyTypeCommand(ORG_ID, USER_ID, BUILT_IN_ID, "Fund", CORPORATION_DESCRIPTION),
            TestContext.Current.CancellationToken);

        Assert.False(redescribed.IsError);
        Assert.True(redescribed.Value.IsBuiltIn);
        Assert.True(renamed.IsError);
        Assert.Equal(ErrorType.Conflict, renamed.FirstError.Type);
        CounterpartyType stored = await _context.CounterpartyTypes.AsNoTracking()
            .SingleAsync(t => t.Id == BUILT_IN_ID, TestContext.Current.CancellationToken);
        Assert.Equal(CounterpartyType.TYPE_CORPORATION, stored.Name);
        Assert.Equal(CORPORATION_DESCRIPTION, stored.Description);
    }

    [Fact]
    public async Task Update_RefusesANameAnotherTypeHas()
    {
        ErrorOr<CounterpartyTypeListItemDto> result = await UpdateHandler().Handle(
            new UpdateCounterpartyTypeCommand(ORG_ID, USER_ID, CUSTOM_ID, ANOTHER_NAME.ToLowerInvariant(), null),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
    }

    [Fact]
    public async Task Update_CountsOnlyTheCallersCounterparties()
    {
        CounterpartyType custom = await _context.CounterpartyTypes.SingleAsync(t => t.Id == CUSTOM_ID, TestContext.Current.CancellationToken);
        foreach (string organizationId in new[] { ORG_ID, OTHER_ORG_ID, OTHER_ORG_ID })
        {
            var counterparty = new Counterparty(organizationId, $"Borrower of {organizationId}");
            counterparty.CounterpartyTypes.Add(custom);
            _ = _context.Counterparties.Add(counterparty);
        }
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        ErrorOr<CounterpartyTypeListItemDto> result = await UpdateHandler().Handle(
            new UpdateCounterpartyTypeCommand(ORG_ID, USER_ID, CUSTOM_ID, CUSTOM_NAME, null),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(1, result.Value.CounterpartyCount);
    }

    [Fact]
    public async Task Update_AnUnknownType_IsNotFound()
    {
        ErrorOr<CounterpartyTypeListItemDto> result = await UpdateHandler().Handle(
            new UpdateCounterpartyTypeCommand(ORG_ID, USER_ID, "no-such-type", "Anything", null),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Delete_RemovesACustomType_AndItsCounterpartiesKeepTheirOtherTypes()
    {
        var counterparty = new Counterparty(ORG_ID, "Republic of Example");
        counterparty.CounterpartyTypes.Add(await _context.CounterpartyTypes.SingleAsync(t => t.Id == CUSTOM_ID, TestContext.Current.CancellationToken));
        counterparty.CounterpartyTypes.Add(await _context.CounterpartyTypes.SingleAsync(t => t.Id == ANOTHER_ID, TestContext.Current.CancellationToken));
        _ = _context.Counterparties.Add(counterparty);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        ErrorOr<Deleted> result = await DeleteHandler().Handle(
            new DeleteCounterpartyTypeCommand(ORG_ID, USER_ID, CUSTOM_ID),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        _context.ChangeTracker.Clear();
        Assert.False(await _context.CounterpartyTypes.AnyAsync(t => t.Id == CUSTOM_ID, TestContext.Current.CancellationToken));
        Counterparty kept = await _context.Counterparties.Include(c => c.CounterpartyTypes)
            .SingleAsync(c => c.Id == counterparty.Id, TestContext.Current.CancellationToken);
        Assert.Equal([ANOTHER_ID], kept.CounterpartyTypes.Select(t => t.Id));
    }

    [Fact]
    public async Task Delete_RefusesABuiltInType()
    {
        ErrorOr<Deleted> result = await DeleteHandler().Handle(
            new DeleteCounterpartyTypeCommand(ORG_ID, USER_ID, BUILT_IN_ID),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.True(await _context.CounterpartyTypes.AnyAsync(t => t.Id == BUILT_IN_ID, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Delete_AnUnknownType_IsNotFound()
    {
        ErrorOr<Deleted> result = await DeleteHandler().Handle(
            new DeleteCounterpartyTypeCommand(ORG_ID, USER_ID, "no-such-type"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    private CreateCounterpartyTypeCommandHandler CreateHandler()
    {
        return new CreateCounterpartyTypeCommandHandler(_unitOfWork, NullLogger<CreateCounterpartyTypeCommandHandler>.Instance, _builtIn);
    }

    private UpdateCounterpartyTypeCommandHandler UpdateHandler()
    {
        return new UpdateCounterpartyTypeCommandHandler(_unitOfWork, NullLogger<UpdateCounterpartyTypeCommandHandler>.Instance, _builtIn);
    }

    private DeleteCounterpartyTypeCommandHandler DeleteHandler()
    {
        return new DeleteCounterpartyTypeCommandHandler(_unitOfWork, NullLogger<DeleteCounterpartyTypeCommandHandler>.Instance, _builtIn);
    }
}
