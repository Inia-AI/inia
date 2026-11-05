using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using IniaAi.Testing.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ServiceProvider = Microsoft.Extensions.DependencyInjection.ServiceProvider;

namespace IniaAi.Application.UnitTests.Commands.UserManagement;

/// <summary>
/// Identity and the application's commands over a real SQLite user-data database that holds one
/// organization, for the tests of the password commands.
/// </summary>
internal static class IdentityTestDatabase
{
    public const string ORGANIZATION_ID = "org-reset";

    /// <summary>
    /// Builds the services and seeds the organization.
    /// </summary>
    /// <param name="configure">Registers what a test needs beside Identity, such as the outgoing
    /// mail, the configuration or a logger. It runs last, so its registrations win; without a
    /// configuration of its own, a test gets an empty one.</param>
    public static ServiceProvider Build(Action<IServiceCollection>? configure = null)
    {
        ServiceCollection services = new();
        _ = services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        _ = services.AddLogging();
        _ = services.AddDataProtection();
        _ = services.AddSqliteTestDatabaseServices();
        _ = services.AddIdentityCore<User>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<UserDataDbContext>()
            .AddDefaultTokenProviders();
        _ = services.AddApplication();
        _ = services.AddScoped<IUserSessionRevoker, UserSessionRevoker>();
        configure?.Invoke(services);

        ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope seeding = provider.CreateScope();
        UserDataDbContext db = seeding.ServiceProvider.GetRequiredService<UserDataDbContext>();
        _ = db.Organizations.Add(new Organization(ORGANIZATION_ID, ORGANIZATION_ID));
        _ = db.SaveChanges();

        return provider;
    }

    /// <summary>
    /// Creates a confirmed account for <paramref name="email"/> in the organization, through
    /// Identity, and fails the test when Identity refuses it.
    /// </summary>
    public static async Task<User> CreateAccountAsync(this UserManager<User> users, string email, string? password = null, bool isSystem = false)
    {
        User user = new(ORGANIZATION_ID) { UserName = email, Email = email, EmailConfirmed = true, IsSystem = isSystem };
        IdentityResult created = password is null ? await users.CreateAsync(user) : await users.CreateAsync(user, password);
        Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(error => error.Description)));
        return user;
    }
}
