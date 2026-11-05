using ErrorOr;
using IniaAi.Application.Commands.Counterparties.DeleteCounterparty;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.UnitTests.Commands.Counterparties.DeleteCounterparty;

/// <summary>
/// Deleting a counterparty deletes everything beneath it, so no child is left active under a
/// deleted parent. Its siblings and other organizations' counterparties stay.
/// </summary>
public class DeleteCounterpartyCommandHandlerTests : IDisposable
{
    private const string ORG_ID = "test-org-delete-counterparty";
    private const string OTHER_ORG_ID = "test-org-delete-counterparty-other";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly DeleteCounterpartyCommandHandler _handler;

    public DeleteCounterpartyCommandHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();
        _handler = new DeleteCounterpartyCommandHandler(_serviceProvider.GetRequiredService<IUserDataUnitOfWork>());
    }

    public void Dispose()
    {
        _ = _context.Database.EnsureDeleted();
        _context.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Handle_DeletesTheCounterpartyAndEveryDescendant_AndNothingElse()
    {
        Counterparty group = Seed(ORG_ID, "GROUP", "Example Group", parentId: null);
        Counterparty bank = Seed(ORG_ID, "BANK", "Example Bank", group.Id);
        Counterparty branch = Seed(ORG_ID, "BRANCH", "Example Bank Branch", bank.Id);
        Counterparty sibling = Seed(ORG_ID, "SIBLING", "Unrelated Group", parentId: null);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        ErrorOr<Deleted> result = await _handler.Handle(new DeleteCounterpartyCommand(group.Id, ORG_ID), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        _context.ChangeTracker.Clear();
        List<string> active = await _context.Counterparties.Select(c => c.Id).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal([sibling.Id], active);
        List<string> deleted = await _context.Counterparties.IgnoreQueryFilters()
            .Where(c => c.IsDeleted)
            .Select(c => c.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal([bank.Id, branch.Id, group.Id], deleted.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Handle_ATreeThatLoopsBackOnItself_IsDeletedOnce()
    {
        Counterparty first = Seed(ORG_ID, "FIRST", "First", parentId: "SECOND");
        Counterparty second = Seed(ORG_ID, "SECOND", "Second", first.Id);
        Counterparty itsOwnParent = Seed(ORG_ID, "SELF", "Self", parentId: "SELF");
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        ErrorOr<Deleted> loop = await _handler.Handle(new DeleteCounterpartyCommand(first.Id, ORG_ID), timeout.Token);
        ErrorOr<Deleted> self = await _handler.Handle(new DeleteCounterpartyCommand(itsOwnParent.Id, ORG_ID), timeout.Token);

        Assert.False(loop.IsError);
        Assert.False(self.IsError);
        _context.ChangeTracker.Clear();
        Assert.Empty(await _context.Counterparties.Select(c => c.Id).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            [first.Id, second.Id, itsOwnParent.Id],
            (await _context.Counterparties.IgnoreQueryFilters().Where(c => c.IsDeleted).Select(c => c.Id).ToListAsync(TestContext.Current.CancellationToken)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Handle_ACounterpartyOfAnotherOrganization_IsNotFound()
    {
        Counterparty foreign = Seed(OTHER_ORG_ID, "FOREIGN", "Foreign Group", parentId: null);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        ErrorOr<Deleted> result = await _handler.Handle(new DeleteCounterpartyCommand(foreign.Id, ORG_ID), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.True(await _context.Counterparties.AnyAsync(c => c.Id == foreign.Id, TestContext.Current.CancellationToken));
    }

    private Counterparty Seed(string organizationId, string id, string name, string? parentId)
    {
        var counterparty = new Counterparty(organizationId, name) { Id = id, ParentId = parentId };
        _ = _context.Counterparties.Add(counterparty);
        return counterparty;
    }
}
