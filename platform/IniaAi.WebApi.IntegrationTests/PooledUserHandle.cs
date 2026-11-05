using IniaAi.Domain.Entities.UserManagement;

namespace IniaAi.WebApi.IntegrationTests;

/// <summary>
/// Represents a disposable handle for a checked-out test user from a pool.
/// When disposed, returns the user to the pool for reuse in parallel tests.
/// </summary>
public sealed class PooledUserHandle : IAsyncDisposable
{
    /// <summary>
    /// Gets the checked-out <see cref="User"/> for this handle.
    /// </summary>
    public User User { get; }

    private readonly ConcurrentTestUserPool _pool;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PooledUserHandle"/> class.
    /// </summary>
    /// <param name="pool">The user pool from which the user was checked out.</param>
    /// <param name="user">The checked-out user.</param>
    public PooledUserHandle(ConcurrentTestUserPool pool, User user)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        User = user ?? throw new ArgumentNullException(nameof(user));
    }

    /// <summary>
    /// Asynchronously disposes the handle and returns the user to the pool.
    /// </summary>
    /// <returns>A completed task.</returns>g
    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _pool.ReturnUser(User);
            _disposed = true;
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Provides extension methods for working with <see cref="ConcurrentTestUserPool"/>.
/// </summary>
public static class UserPoolExtensions
{
    /// <summary>
    /// Checks out a user from the pool and returns a <see cref="PooledUserHandle"/>
    /// that will return the user to the pool when disposed.
    /// </summary>
    /// <param name="pool">The user pool.</param>
    /// <returns>A <see cref="PooledUserHandle"/> wrapping the checked-out user.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the pool is empty.</exception>
    public static PooledUserHandle CheckoutUserHandle(this ConcurrentTestUserPool pool)
    {
        if (pool is null)
        {
            throw new ArgumentNullException(nameof(pool));
        }

        User user = pool.CheckoutUser();
        return new PooledUserHandle(pool, user);
    }
}
