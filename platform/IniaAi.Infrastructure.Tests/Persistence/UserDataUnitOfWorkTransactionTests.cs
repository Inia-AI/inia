using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Infrastructure.Tests.Persistence;

/// <summary>
/// A transactional unit commits what its result accepts. A rolled-back or failed attempt leaves the
/// change tracker as it found it: what the attempt added is detached, so the caller's next save
/// cannot write it, and what the caller tracked before stays tracked.
/// </summary>
public sealed class UserDataUnitOfWorkTransactionTests : IDisposable
{
    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly IUserDataUnitOfWork _unitOfWork;
    private readonly UserDataDbContext _context;

    public UserDataUnitOfWorkTransactionTests()
    {
        var services = new ServiceCollection();
        _ = services.AddSqliteTestDatabaseServices();
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        _unitOfWork = _scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
        _context = _scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }

    [Fact]
    public async Task AnAcceptedAttempt_Commits()
    {
        bool committed = await _unitOfWork.InTransactionAsync(
            async token =>
            {
                await AddBootAsync("accepted", token);
                return true;
            },
            accepted => accepted,
            TestContext.Current.CancellationToken);

        Assert.True(committed);
        Assert.Equal(["accepted"], await StoredApplicationsAsync());
    }

    [Fact]
    public async Task ARefusedAttempt_RollsBack_AndDetachesOnlyWhatItAdded()
    {
        HostBoot callers = await SavedBootAsync("caller");

        bool committed = await _unitOfWork.InTransactionAsync(
            async token =>
            {
                await AddBootAsync("refused", token);
                _ = _context.Add(new HostBoot { Application = "never saved", Machine = "m" });
                return false;
            },
            accepted => accepted,
            TestContext.Current.CancellationToken);
        _ = await _unitOfWork.CompleteAsync(TestContext.Current.CancellationToken);

        Assert.False(committed);
        Assert.Equal(["caller"], await StoredApplicationsAsync());
        Assert.Equal(EntityState.Unchanged, _context.Entry(callers).State);
        Assert.Single(_context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task AnAttemptThatThrows_RollsBack_AndDetachesWhatItAdded()
    {
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => _unitOfWork.InTransactionAsync<bool>(
            async token =>
            {
                await AddBootAsync("thrown", token);
                _ = _context.Add(new HostBoot { Application = "never saved", Machine = "m" });
                throw new InvalidOperationException("the attempt broke");
            },
            accepted => accepted,
            TestContext.Current.CancellationToken));
        _ = await _unitOfWork.CompleteAsync(TestContext.Current.CancellationToken);

        Assert.Empty(await StoredApplicationsAsync());
        Assert.Empty(_context.ChangeTracker.Entries());
    }

    private async Task AddBootAsync(string application, CancellationToken cancellationToken)
    {
        _ = _context.Add(new HostBoot { Application = application, Machine = "m" });
        _ = await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<HostBoot> SavedBootAsync(string application)
    {
        var boot = new HostBoot { Application = application, Machine = "m" };
        _ = _context.Add(boot);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return boot;
    }

    private async Task<List<string>> StoredApplicationsAsync()
    {
        using IServiceScope check = _provider.CreateScope();

        return await check.ServiceProvider.GetRequiredService<UserDataDbContext>()
            .Set<HostBoot>()
            .AsNoTracking()
            .Select(boot => boot.Application)
            .ToListAsync(TestContext.Current.CancellationToken);
    }
}
