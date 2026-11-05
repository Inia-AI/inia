using AutoFixture;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace IniaAi.WebApi.IntegrationTests;

public class ConcurrentTestUserPool : IAsyncDisposable
{
    private readonly ConcurrentBag<User> _userPool = [];
    private readonly ConcurrentDictionary<string, User> _checkedOutUsers = new();
    private readonly IServiceProvider _serviceProvider;
    private readonly List<string> _createdUserIds = [];

    public ConcurrentTestUserPool(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Seeds the pool with N users in the DB.
    /// </summary>
    public async Task SeedAsync(int count = 10)
    {
        var fixture = new Fixture();
        fixture.Behaviors
            .OfType<ThrowingRecursionBehavior>()
            .ToList()
            .ForEach(b => fixture.Behaviors.Remove(b));
        fixture.Behaviors.Add(new OmitOnRecursionBehavior());

        // Configure AutoFixture to generate UTC DateTimes for PostgreSQL compatibility
        fixture.Register(() => DateTime.SpecifyKind(DateTime.UtcNow.AddDays(fixture.Create<int>() % 365), DateTimeKind.Utc));

        using IServiceScope scope = _serviceProvider.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        for (int i = 0; i < count; i++)
        {
            string userId = Guid.NewGuid().ToString();
            // Create user manually to avoid AutoFixture filling IdentityUser properties with long random strings
            var user = new User
            {
                Id = userId,
                OrganizationId = "IniaAI",
                Email = $"user{i}_{userId[..8]}@test.com",
                NormalizedEmail = $"USER{i}_{userId[..8]}@TEST.COM",
                UserName = $"testuser{i}_{userId[..8]}",
                NormalizedUserName = $"TESTUSER{i}_{userId[..8]}",
                FirstName = $"Test{i}",
                LastName = "User",
                EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString(),
                ConcurrencyStamp = Guid.NewGuid().ToString()
            };

            _ = db.Users.Add(user);
            _userPool.Add(user);
            _createdUserIds.Add(user.Id);
        }

        _ = await db.SaveChangesAsync();
    }

    public async Task<User> CreateAndCheckoutCustomUser(Action<User> customize)
    {
        var fixture = new Fixture();
        User user = fixture.Build<User>()
            .With(u => u.OrganizationId, "Inia")
            .With(u => u.Email, $"custom_{Guid.NewGuid():N}@test.com")
            .With(u => u.UserName, $"customuser_{Guid.NewGuid():N}")
            .Create();

        customize(user);

        using IServiceScope scope = _serviceProvider.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        _ = db.Users.Add(user);
        _ = await db.SaveChangesAsync();

        _ = _checkedOutUsers.TryAdd(user.Id, user);
        _createdUserIds.Add(user.Id);

        return user;
    }

    /// <summary>
    /// Retrieves a test user from the pool (thread-safe).
    /// Throws if pool is empty.
    /// </summary>
    public User CheckoutUser()
    {
        if (!_userPool.TryTake(out User? user))
        {
            throw new InvalidOperationException("No more test users in pool.");
        }

        _ = _checkedOutUsers.TryAdd(user.Id, user);
        return user;
    }

    /// <summary>
    /// Returns a test user to the pool for reuse (thread-safe).
    /// </summary>
    public void ReturnUser(User user)
    {
        if (_checkedOutUsers.TryRemove(user.Id, out _))
        {
            _userPool.Add(user);
        }
    }

    /// <summary>
    /// Cleanup: removes created users from the DB.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        //using var scope = _serviceProvider.CreateScope();
        //var unitOfWork = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();

        //await unitOfWork.Users.RemoveByIds(_createdUserIds);

        //await unitOfWork.CompleteAsync();
    }

    /// <summary>
    /// Optional: Retrieve all currently checked-out user IDs (for debugging).
    /// </summary>
    public IReadOnlyCollection<string> CheckedOutUserIds => _checkedOutUsers.Keys.ToList().AsReadOnly();
}
