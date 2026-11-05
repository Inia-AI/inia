using IniaAi.Application.Commands.Signatures;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.UnitTests.Commands.Signatures;

public class SignatureActorResolverTests : IDisposable
{
    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly IUserDataUnitOfWork _unitOfWork;

    public SignatureActorResolverTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();
        _unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
    }

    public void Dispose()
    {
        _context.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ResolveEmails_EmptyInput_ReturnsEmptyDict()
    {
        Dictionary<string, string?> result = await SignatureActorResolver.ResolveEmailsAsync(
            _unitOfWork,
            Array.Empty<string>(),
            TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ResolveEmails_OnlyWhitespaceIds_ReturnsEmptyDict()
    {
        Dictionary<string, string?> result = await SignatureActorResolver.ResolveEmailsAsync(
            _unitOfWork,
            new[] { string.Empty, " ", "  " },
            TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ResolveEmails_KnownUsers_ReturnsEmailDictKeyedById()
    {
        await SeedUserAsync("u1", "u1@example.com");
        await SeedUserAsync("u2", "u2@example.com");

        Dictionary<string, string?> result = await SignatureActorResolver.ResolveEmailsAsync(
            _unitOfWork,
            new[] { "u1", "u2" },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.Equal("u1@example.com", result["u1"]);
        Assert.Equal("u2@example.com", result["u2"]);
    }

    [Fact]
    public async Task ResolveEmails_UnknownIdsAbsent_KnownIdsPresent()
    {
        await SeedUserAsync("u1", "u1@example.com");

        Dictionary<string, string?> result = await SignatureActorResolver.ResolveEmailsAsync(
            _unitOfWork,
            new[] { "u1", "missing-user" },
            TestContext.Current.CancellationToken);

        // Missing IDs are simply absent from the dict — callers use TryGetValue
        // and the writer accepts null emails.
        Assert.Single(result);
        Assert.True(result.ContainsKey("u1"));
        Assert.False(result.ContainsKey("missing-user"));
    }

    [Fact]
    public async Task ResolveEmails_DuplicateIds_DoNotProduceDuplicateRows()
    {
        await SeedUserAsync("u1", "u1@example.com");

        Dictionary<string, string?> result = await SignatureActorResolver.ResolveEmailsAsync(
            _unitOfWork,
            new[] { "u1", "u1", "u1" },
            TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal("u1@example.com", result["u1"]);
    }

    private async Task SeedUserAsync(string id, string email)
    {
        var user = new User
        {
            Id = id,
            UserName = email,
            Email = email,
            OrganizationId = "org-test",
        };
        _ = _context.Users.Add(user);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
