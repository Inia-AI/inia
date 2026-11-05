using IniaAi.Application.Common.Services;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.UnitTests.Common.Services;

public class UserDisplayNameResolverTests : IDisposable
{
    private const string ORG = "org-test";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly IUserDataUnitOfWork _unitOfWork;

    public UserDisplayNameResolverTests()
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
    public async Task Resolve_PrefersFullName_OverEmailAndUserName()
    {
        Seed("u1", firstName: "Ada", lastName: "Lovelace", email: "ada@inia.ai", userName: "ada");
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        IReadOnlyDictionary<string, string> map = await Resolver().ResolveAsync(
            ["u1"], TestContext.Current.CancellationToken);

        Assert.Equal("Ada Lovelace", map["u1"]);
    }

    [Fact]
    public async Task Resolve_IncludesMiddleName_WhenPresent()
    {
        Seed("u1", firstName: "Ada", middleName: "Byron", lastName: "Lovelace");
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        IReadOnlyDictionary<string, string> map = await Resolver().ResolveAsync(
            ["u1"], TestContext.Current.CancellationToken);

        Assert.Equal("Ada Byron Lovelace", map["u1"]);
    }

    [Fact]
    public async Task Resolve_FallsBackToEmail_WhenNoName()
    {
        Seed("u1", email: "person@inia.ai", userName: "person");
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        IReadOnlyDictionary<string, string> map = await Resolver().ResolveAsync(
            ["u1"], TestContext.Current.CancellationToken);

        Assert.Equal("person@inia.ai", map["u1"]);
    }

    [Fact]
    public async Task Resolve_FallsBackToUserName_WhenNoNameOrEmail()
    {
        Seed("u1", userName: "justusername");
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        IReadOnlyDictionary<string, string> map = await Resolver().ResolveAsync(
            ["u1"], TestContext.Current.CancellationToken);

        Assert.Equal("justusername", map["u1"]);
    }

    [Fact]
    public async Task Resolve_OmitsUnknownIds_SoCallerCanFallBack()
    {
        Seed("known", firstName: "Known", lastName: "Person");
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        IReadOnlyDictionary<string, string> map = await Resolver().ResolveAsync(
            ["known", "missing"], TestContext.Current.CancellationToken);

        Assert.True(map.ContainsKey("known"));
        Assert.False(map.ContainsKey("missing"));
    }

    [Fact]
    public async Task Resolve_EmptyInput_ReturnsEmpty()
    {
        IReadOnlyDictionary<string, string> map = await Resolver().ResolveAsync(
            [], TestContext.Current.CancellationToken);

        Assert.Empty(map);
    }

    private UserDisplayNameResolver Resolver()
    {
        return new UserDisplayNameResolver(_unitOfWork);
    }

    private void Seed(
        string id,
        string? firstName = null,
        string? middleName = null,
        string? lastName = null,
        string? email = null,
        string? userName = null)
    {
        _ = _context.Users.Add(new User(ORG)
        {
            Id = id,
            FirstName = firstName,
            MiddleName = middleName,
            LastName = lastName,
            Email = email,
            UserName = userName,
        });
    }
}
