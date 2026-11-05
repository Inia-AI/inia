using IniaAi.Domain.Persistence.Core.Repositories;
using IniaAi.Domain.Persistence.UserData.Repositories;

namespace IniaAi.Domain.Core.Interfaces;

public interface ICoreUnitOfWork : IDisposable
{
    ICoreWorkflowRepository Workflows { get; }
    ICoreWorkflowTypeRepository WorkflowTypes { get; }
    IToolsSettingsRepository Tools { get; }
    // WorkflowTypeCategoryRepository removed - use Repository<T>() method

    /// <summary>
    /// Gets a generic repository for any entity type. Use this for new entities
    /// instead of adding specific repository properties to avoid constructor bloat.
    /// </summary>
    /// <typeparam name="T">The entity type</typeparam>
    /// <returns>Generic repository for the specified entity type</returns>
    IRepository<T> Repository<T>() where T : class;

    Task<int> CompleteAsync(CancellationToken cancellationToken = default);
    Task RevertAsync<T>(T entity) where T : class;
    Task UntrackAsync<T>(T entity) where T : class;
}
