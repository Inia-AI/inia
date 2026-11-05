using IniaAi.Core.Common;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Tools.Reconciliation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IniaAi.Core.Services;

/// <summary>
/// The newest <see cref="ReconciliationPrompts"/> row, the one Tools Settings edits, read once per
/// instance. <see cref="ReconciliationService.ReloadPrompts"/> starts a new instance for each run.
/// </summary>
internal sealed class ReconciliationPromptsLoader : PromptsLoader<ReconciliationPrompts>
{
    public ReconciliationPromptsLoader(ILogger logger, IServiceScopeFactory serviceScopeFactory)
        : base(logger, serviceScopeFactory)
    {
    }

    protected override Task<ReconciliationPrompts?> ReadNewestRowAsync(ICoreUnitOfWork coreUnitOfWork)
    {
        return coreUnitOfWork.Tools.GetReconciliationPromptsAsNoTrackingAsync();
    }

    protected override ReconciliationPrompts Defaults()
    {
        return new ReconciliationPrompts();
    }
}
