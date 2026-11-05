using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Infrastructure.Tests.Helpers;

/// <summary>
/// Builds a real <see cref="UserManager{TUser}"/> over an EF InMemory
/// <see cref="UserDataDbContext"/> so Identity-dependent services can be tested
/// against actual stores instead of mocks. (The UserData model hard-codes
/// nvarchar(max)/jsonb column types, so SQLite cannot create its schema.)
/// </summary>
public sealed class InMemoryIdentityHarness : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public InMemoryIdentityHarness()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddDataProtection();
        _ = services.AddDbContext<UserDataDbContext>(options =>
            options.UseInMemoryDatabase($"identity-harness-{Guid.NewGuid()}"));
        _ = services.AddIdentityCore<User>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<UserDataDbContext>()
            .AddDefaultTokenProviders();

        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();

        Context = _scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        UserManager = _scope.ServiceProvider.GetRequiredService<UserManager<User>>();
    }

    public UserDataDbContext Context { get; }

    public UserManager<User> UserManager { get; }

    public async Task<User> CreateUserAsync(string email, bool isSystem = false)
    {
        var user = new User
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            IsSystem = isSystem,
        };

        IdentityResult result = await UserManager.CreateAsync(user);
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
        return user;
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}
