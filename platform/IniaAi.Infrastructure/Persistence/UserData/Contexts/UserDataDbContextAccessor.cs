using IniaAi.Application.Common.Interfaces.Contexts;
using IniaAi.Common.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Infrastructure.Persistence.UserData.Contexts;

/// <summary>
/// Default implementation of <see cref="IUserDataDbContextAccessor"/> using <see cref="IDbContextFactory{TContext}"/>.
/// </summary>
public class UserDataDbContextAccessor : IUserDataDbContextAccessor
{
    private readonly IDbContextFactory<UserDataDbContext> _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserDataDbContextAccessor"/> class.
    /// </summary>
    /// <param name="factory">The factory used to create <see cref="UserDataDbContext"/> instances.</param>
    public UserDataDbContextAccessor(IDbContextFactory<UserDataDbContext> factory)
    {
        _factory = factory;
    }

    /// <inheritdoc />
    public IUserDataDbContext CreateDbContext()
    {
        return _factory.CreateDbContext();
    }

    /// <inheritdoc />
    public async Task<IUserDataDbContext> CreateDbContextAsync()
    {
        return await _factory.CreateDbContextAsync();
    }
}
