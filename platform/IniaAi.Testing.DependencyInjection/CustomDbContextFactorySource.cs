using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;

namespace IniaAi.Testing.DependencyInjection;

#pragma warning disable EF1001 // Internal EF Core API usage.
public class CustomDbContextFactorySource<TContext> : IDbContextFactorySource<TContext>
#pragma warning restore EF1001 // Internal EF Core API usage.
    where TContext : DbContext
{
    public Func<IServiceProvider, DbContextOptions<TContext>, TContext> Factory { get; }

    public CustomDbContextFactorySource()
    {
        Factory = (serviceProvider, options) => (TContext)Activator.CreateInstance(typeof(TContext), options)!;
    }
}
