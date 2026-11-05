using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Data;
using ServiceProvider = Microsoft.Extensions.DependencyInjection.ServiceProvider;

namespace IniaAi.Infrastructure.Tests.Persistence;

/// <summary>
/// The SQLite user-data database a test gets holds the schema from the start, foreign keys
/// included, belongs to its provider alone, and lives only as long as its open connection, so the
/// container that holds the connection has to close it when the test disposes the container.
/// </summary>
public class SqliteTestDatabaseServicesTests
{
    [Fact]
    public void DisposingTheProvider_ClosesTheConnection()
    {
        ServiceCollection services = new();
        _ = services.AddSqliteTestDatabaseServices();
        SqliteConnection connection;

        using (ServiceProvider provider = services.BuildServiceProvider())
        {
            connection = provider.GetRequiredService<SqliteConnection>();
            Assert.Equal(ConnectionState.Open, connection.State);
        }

        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task EachProvider_GetsADatabaseOfItsOwn_ThatAlreadyHoldsTheSchema()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        ServiceCollection services = new();
        _ = services.AddSqliteTestDatabaseServices();
        using ServiceProvider first = services.BuildServiceProvider();
        using ServiceProvider second = services.BuildServiceProvider();

        using (IServiceScope scope = first.CreateScope())
        {
            UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
            _ = db.Organizations.Add(new Organization("Only in the first database"));
            _ = await db.SaveChangesAsync(cancellationToken);
        }

        using IServiceScope other = second.CreateScope();
        Assert.False(await other.ServiceProvider.GetRequiredService<UserDataDbContext>().Organizations.AnyAsync(cancellationToken));
    }

    [Fact]
    public async Task TheCopiedSchema_RefusesARowWhoseParentIsMissing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        ServiceCollection services = new();
        _ = services.AddSqliteTestDatabaseServices();
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        _ = db.UserOrganizations.Add(new UserOrganization { UserId = "no-such-user", OrganizationId = "no-such-organization" });

        DbUpdateException refused = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));

        Assert.Contains("FOREIGN KEY constraint failed", refused.InnerException?.Message);
    }
}
