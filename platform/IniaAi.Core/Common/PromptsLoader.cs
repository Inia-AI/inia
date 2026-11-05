using IniaAi.Domain.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IniaAi.Core.Common;

/// <summary>
/// Reads one prompt set once, on first use: the first caller triggers the read and every later
/// caller gets the same instance, so an edit landing mid-operation cannot split it. A failed
/// read falls back to the code-default prompts. Hold one instance per operation that must see
/// one prompt set; a fresh instance reads the newest row.
/// </summary>
public abstract class PromptsLoader<TPrompts> where TPrompts : class
{
    private readonly ILogger _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly Lazy<Task<TPrompts>> _prompts;

    protected PromptsLoader(ILogger logger, IServiceScopeFactory serviceScopeFactory)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
        _prompts = new Lazy<Task<TPrompts>>(ReadNewestRowOrDefaultsAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public Task<TPrompts> LoadAsync()
    {
        return _prompts.Value;
    }

    protected abstract Task<TPrompts?> ReadNewestRowAsync(ICoreUnitOfWork coreUnitOfWork);

    protected abstract TPrompts Defaults();

    private async Task<TPrompts> ReadNewestRowOrDefaultsAsync()
    {
        try
        {
            using IServiceScope scope = _serviceScopeFactory.CreateScope();
            ICoreUnitOfWork coreUnitOfWork = scope.ServiceProvider.GetRequiredService<ICoreUnitOfWork>();

            return await ReadNewestRowAsync(coreUnitOfWork).ConfigureAwait(false) ?? Defaults();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read the {PromptSet}; the code defaults are in use", typeof(TPrompts).Name);
            return Defaults();
        }
    }
}
