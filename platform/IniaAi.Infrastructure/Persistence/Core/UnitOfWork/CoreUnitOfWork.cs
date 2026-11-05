using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Persistence.Core.Repositories;
using IniaAi.Domain.Persistence.UserData.Repositories;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Infrastructure.Persistence.Core.UnitOfWork;

public class CoreUnitOfWork : ICoreUnitOfWork
{
    private readonly CoreDbContext _context;
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<Type, object> _genericRepositories = [];
    private bool _disposed;

    // Lazy-loaded specific repositories

    public CoreUnitOfWork(CoreDbContext context, IServiceProvider serviceProvider)
    {
        _context = context;
        _serviceProvider = serviceProvider;
    }

    // Lazy properties - created only when accessed
    public ICoreWorkflowRepository Workflows =>
        field ??= _serviceProvider.GetRequiredService<ICoreWorkflowRepository>();

    public ICoreWorkflowTypeRepository WorkflowTypes =>
        field ??= _serviceProvider.GetRequiredService<ICoreWorkflowTypeRepository>();

    public IToolsSettingsRepository Tools =>
        field ??= _serviceProvider.GetRequiredService<IToolsSettingsRepository>();

    /// <summary>
    /// Generic repository method for new entities - avoids adding new properties to the interface.
    /// </summary>
    public IRepository<T> Repository<T>() where T : class
    {
        Type type = typeof(T);

        if (!_genericRepositories.TryGetValue(type, out object? value))
        {
            value = _serviceProvider.GetRequiredService<ICoreRepository<T>>();
            _genericRepositories[type] = value;
        }

        return (IRepository<T>)value;
    }

    public async Task<int> CompleteAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RevertAsync<T>(T entity) where T : class
    {
        _context.Entry(entity).State = EntityState.Unchanged;
        await Task.CompletedTask;
    }

    public async Task UntrackAsync<T>(T entity) where T : class
    {
        if (entity is Workflow workflow)
        {
            await UntrackWorkflowAsync(workflow);
        }
        else
        {
            _context.Entry(entity).State = EntityState.Detached;
        }

        await Task.CompletedTask;
    }

    private async Task UntrackWorkflowAsync(Workflow workflow)
    {
        _context.Entry(workflow).State = EntityState.Detached;

        foreach (Section section in workflow.Sections)
        {
            _context.Entry(section).State = EntityState.Detached;

            foreach (Block block in section.Blocks)
            {
                DetachBlock(block);
            }
        }

        foreach (WorkflowVariable variable in workflow.Variables)
        {
            _context.Entry(variable).State = EntityState.Detached;
        }

        foreach (WorkflowRegressionField regressionField in workflow.RegressionFields)
        {
            _context.Entry(regressionField).State = EntityState.Detached;
        }

        foreach (WorkflowSignatureField signatureField in workflow.SignatureFields)
        {
            _context.Entry(signatureField).State = EntityState.Detached;
        }

        await Task.CompletedTask;
    }

    private void DetachBlock(Block block)
    {
        _context.Entry(block).State = EntityState.Detached;

        foreach (TextInput textInput in block.TextInputs)
        {
            _context.Entry(textInput).State = EntityState.Detached;
        }

        foreach (RagSettings ragSetting in block.RagSettings)
        {
            _context.Entry(ragSetting).State = EntityState.Detached;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _context?.Dispose();
        }

        _disposed = true;
    }
}
