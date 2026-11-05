using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.Workflows.Templates;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence;
using IniaAi.Infrastructure.Persistence.UserData;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SqliteUserDataDbContext = IniaAi.Testing.DependencyInjection.SqliteUserDataDbContext;
using SqliteUserDataSchema = IniaAi.Testing.DependencyInjection.SqliteUserDataSchema;

namespace IniaAi.Infrastructure.Tests.Persistence;

/// <summary>
/// A soft delete records the user who made it, whichever repository or handler deleted the row.
/// </summary>
public sealed class SoftDeleteActorTests : IDisposable
{
    private const string ACTOR = "user-who-deleted";

    private readonly SqliteConnection _connection = SqliteUserDataSchema.OpenInMemoryDatabase();
    private readonly string _coreDatabase = $"soft-delete-actor-{Guid.NewGuid():N}";

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public async Task ARepositorySoftDelete_RecordsTheActingUser()
    {
        string id = await SeedAsync();

        await using (SqliteUserDataDbContext context = Context(ACTOR))
        {
            var repository = new UserDataRepositoryWrapper<CounterpartyType>(context);
            repository.Remove((await context.CounterpartyTypes.FindAsync([id], TestContext.Current.CancellationToken))!);
            _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(ACTOR, (await ReloadAsync(id)).DeletedByUserId);
    }

    [Fact]
    public async Task ARowDeletedThroughUpdateWithoutTracking_RecordsTheActingUser()
    {
        string id = await SeedAsync();

        await using (SqliteUserDataDbContext context = Context(ACTOR))
        {
            CounterpartyType row = await context.CounterpartyTypes.AsNoTracking().SingleAsync(r => r.Id == id, TestContext.Current.CancellationToken);
            row.IsDeleted = true;
            _ = context.Update(row);
            _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(ACTOR, (await ReloadAsync(id)).DeletedByUserId);
    }

    [Fact]
    public async Task ADeleteThatNamesItsUser_KeepsThatUser()
    {
        string id = await SeedAsync();

        await using (SqliteUserDataDbContext context = Context(ACTOR))
        {
            CounterpartyType row = (await context.CounterpartyTypes.FindAsync([id], TestContext.Current.CancellationToken))!;
            row.IsDeleted = true;
            row.DeletedByUserId = "handler-named-user";
            _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal("handler-named-user", (await ReloadAsync(id)).DeletedByUserId);
    }

    [Fact]
    public async Task ADeleteWithNoActingUser_NamesNoOne()
    {
        string id = await SeedAsync();

        await using (SqliteUserDataDbContext context = Context(null))
        {
            new UserDataRepositoryWrapper<CounterpartyType>(context).Remove((await context.CounterpartyTypes.FindAsync([id], TestContext.Current.CancellationToken))!);
            _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        CounterpartyType reloaded = await ReloadAsync(id);
        Assert.True(reloaded.IsDeleted);
        Assert.Null(reloaded.DeletedByUserId);
    }

    [Fact]
    public async Task ACoreRowAHandlerMarksDeleted_RecordsTheActingUser()
    {
        string id;
        await using (CoreDbContext context = CoreContext(null))
        {
            DocumentCategory category = DocumentCategory.Create($"Category {Guid.NewGuid():N}");
            _ = context.Add(category);
            _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            id = category.Id;
        }

        await using (CoreDbContext context = CoreContext(ACTOR))
        {
            (await context.Set<DocumentCategory>().SingleAsync(c => c.Id == id, TestContext.Current.CancellationToken)).IsDeleted = true;
            _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using CoreDbContext reloaded = CoreContext(null);
        DocumentCategory deleted = await reloaded.Set<DocumentCategory>().IgnoreQueryFilters().SingleAsync(c => c.Id == id, TestContext.Current.CancellationToken);
        Assert.True(deleted.IsDeleted);
        Assert.Equal(ACTOR, deleted.DeletedByUserId);
    }

    private CoreDbContext CoreContext(string? actor)
    {
        DbContextOptions<CoreDbContext> options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase(_coreDatabase)
            .AddInterceptors(new SoftDeleteActorInterceptor(new FixedActingUser(actor)))
            .Options;
        return new CoreDbContext(options);
    }

    private SqliteUserDataDbContext Context(string? actor)
    {
        DbContextOptions<UserDataDbContext> options = new DbContextOptionsBuilder<UserDataDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new SoftDeleteActorInterceptor(new FixedActingUser(actor)))
            .Options;
        return new SqliteUserDataDbContext(options);
    }

    private async Task<string> SeedAsync()
    {
        await using SqliteUserDataDbContext context = Context(null);
        var row = new CounterpartyType { Name = $"Type {Guid.NewGuid():N}" };
        _ = context.CounterpartyTypes.Add(row);
        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return row.Id;
    }

    private async Task<CounterpartyType> ReloadAsync(string id)
    {
        await using SqliteUserDataDbContext context = Context(null);
        return await context.CounterpartyTypes.IgnoreQueryFilters().SingleAsync(row => row.Id == id, TestContext.Current.CancellationToken);
    }

    private sealed record FixedActingUser(string? UserId) : IActingUser;
}
