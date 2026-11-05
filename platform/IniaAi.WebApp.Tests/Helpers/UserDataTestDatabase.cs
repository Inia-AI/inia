using IniaAi.Configuration;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IniaAi.WebApp.Tests.Helpers;

/// <summary>
/// A real user-data database on in-memory SQLite, with the unit of work and the caller-organization
/// check the WebApp controllers use.
/// </summary>
public sealed class UserDataTestDatabase : IDisposable
{
    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _root;
    private readonly IServiceScope _scope;

    public UserDataTestDatabase(PlatformSettings platformSettings)
    {
        ServiceCollection services = new();
        _ = services.AddLogging();
        _ = services.AddSqliteTestDatabaseServices();
        _ = services.AddSingleton(Options.Create(platformSettings));
        _ = services.AddScoped<CallerOrganizations>();

        _root = services.BuildServiceProvider();
        _scope = _root.CreateScope();
    }

    public T Get<T>() where T : notnull
    {
        return _scope.ServiceProvider.GetRequiredService<T>();
    }

    /// <summary>
    /// Adds a user whose default organization is <paramref name="organizationId"/> and who left
    /// <paramref name="leftOrganizationIds"/>. Every organization must already be seeded.
    /// </summary>
    public async Task<string> SeedMemberAsync(string organizationId, params string[] leftOrganizationIds)
    {
        string name = $"member-{Guid.NewGuid():N}";
        var member = new User(organizationId)
        {
            UserName = name,
            NormalizedUserName = name.ToUpperInvariant(),
            Email = $"{name}@example.test",
            NormalizedEmail = $"{name}@EXAMPLE.TEST".ToUpperInvariant(),
        };

        await SeedAsync(
            [
                member,
                new UserOrganization { UserId = member.Id, OrganizationId = organizationId, IsDefault = true },
                .. leftOrganizationIds.Select(left => new UserOrganization { UserId = member.Id, OrganizationId = left, IsActive = false }),
            ]);

        return member.Id;
    }

    public async Task SeedOrganizationsAsync(params string[] organizationIds)
    {
        await SeedAsync([.. organizationIds.Select(id => new Organization(id, id))]);
    }

    /// <summary>
    /// Saves <paramref name="entities"/> through a context of its own, so the code under test reads them
    /// from the database rather than from its own change tracker.
    /// </summary>
    public async Task SeedAsync(params object[] entities)
    {
        using IServiceScope seeding = _root.CreateScope();
        UserDataDbContext db = seeding.ServiceProvider.GetRequiredService<UserDataDbContext>();
        db.AddRange(entities);
        _ = await db.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);
    }

    public void Dispose()
    {
        _scope.Dispose();
        _root.Dispose();
    }
}
