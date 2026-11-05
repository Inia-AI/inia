using IniaAi.Domain.Core.Interfaces;
using IniaAi.Infrastructure.Persistence.Abstractions;
using IniaAi.Infrastructure.Persistence.Core.Contexts;

namespace IniaAi.Infrastructure.Persistence.Core;

/// <summary>
/// Core-specific repository wrapper that uses CoreDbContext
/// </summary>
public class CoreRepositoryWrapper<T> : ArdaliaRepositoryWrapper<T, CoreDbContext>, ICoreRepository<T>
    where T : class
{
    public CoreRepositoryWrapper(CoreDbContext context) : base(context)
    {
    }
}
