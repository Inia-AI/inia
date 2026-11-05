using IniaAi.Common.Interfaces.Persistence;

namespace IniaAi.Application.Common.Interfaces.Contexts;

/// <summary>
/// Provides a method for retrieving a new instance of <see cref="IUserDataDbContext"/>.
/// Used to abstract access to EF Core's <c>IDbContextFactory</c>.
/// </summary>
public interface IUserDataDbContextAccessor
{
    /// <summary>
    /// Creates a new instance of <see cref="IUserDataDbContext"/>.
    /// </summary>
    /// <returns>An instance of <see cref="IUserDataDbContext"/>.</returns>
    IUserDataDbContext CreateDbContext();

    Task<IUserDataDbContext> CreateDbContextAsync();
}
