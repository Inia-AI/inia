using IniaAi.Common.Interfaces;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Configuration;
using IniaAi.Core.Services.Abstraction;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Graph;
using IniaAi.Core.Workflows.Outputs.Interfaces;
using IniaAi.Core.Workflows.Scheduling;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;

namespace IniaAi.Core.Tests.Workflows.Scheduling.Helpers;

internal static class TestWorkflowHandlers
{
    internal static WorkflowHandler BuildWithScheduler(IServiceProvider serviceProvider, int maxBlockParallelism)
    {
        return BuildWithScheduler(serviceProvider, TestSchedulers.NewRealScheduler(maxBlockParallelism));
    }

    internal static WorkflowHandler BuildWithScheduler(IServiceProvider serviceProvider, LevelScheduler scheduler)
    {
        return new WorkflowHandler(
            serviceProvider.GetRequiredService<ILogger<WorkflowHandler>>(),
            serviceProvider.GetRequiredService<IOptions<PlatformSettings>>(),
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            serviceProvider.GetRequiredService<IWorkflowOutputService>(),
            serviceProvider.GetRequiredService<IDocumentProcessingService>(),
            serviceProvider.GetRequiredService<IFileStorageManager>(),
            serviceProvider.GetRequiredService<IEntityPlaceholderResolver>(),
            serviceProvider.GetRequiredService<IWorkflowGraphBuilder>(),
            scheduler,
            serviceProvider.GetRequiredService<IExecutedBlockWriter>());
    }

    /// <summary>
    /// The real handler with a substituted block writer and an optional per-block hook running in
    /// <see cref="WorkflowHandler.OnBlockExecutedAsync"/> — used to count executed blocks and to
    /// inject block-internal exceptions.
    /// </summary>
    internal static WorkflowHandler BuildHooked(
        IServiceProvider serviceProvider,
        IExecutedBlockWriter blockWriter,
        int maxBlockParallelism = 1,
        Func<Block, Task>? onBlockExecuted = null)
    {
        return BuildHooked(
            serviceProvider,
            blockWriter,
            TestSchedulers.NewRealScheduler(maxBlockParallelism),
            onBlockExecuted);
    }

    internal static WorkflowHandler BuildHooked(
        IServiceProvider serviceProvider,
        IExecutedBlockWriter blockWriter,
        LevelScheduler scheduler,
        Func<Block, Task>? onBlockExecuted = null)
    {
        return new HookedHandler(
            serviceProvider.GetRequiredService<ILogger<WorkflowHandler>>(),
            serviceProvider.GetRequiredService<IOptions<PlatformSettings>>(),
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            serviceProvider.GetRequiredService<IWorkflowOutputService>(),
            serviceProvider.GetRequiredService<IDocumentProcessingService>(),
            serviceProvider.GetRequiredService<IFileStorageManager>(),
            serviceProvider.GetRequiredService<IEntityPlaceholderResolver>(),
            serviceProvider.GetRequiredService<IWorkflowGraphBuilder>(),
            scheduler,
            blockWriter,
            onBlockExecuted);
    }

    private sealed class HookedHandler : WorkflowHandler
    {
        private readonly Func<Block, Task>? _onBlockExecuted;

        public HookedHandler(
            ILogger<WorkflowHandler> logger,
            IOptions<PlatformSettings> platformSettings,
            IServiceScopeFactory serviceScopeFactory,
            IWorkflowOutputService workflowOutputService,
            IDocumentProcessingService documentProcessingService,
            IFileStorageManager fileStorageManager,
            IEntityPlaceholderResolver entityPlaceholderResolver,
            IWorkflowGraphBuilder graphBuilder,
            LevelScheduler scheduler,
            IExecutedBlockWriter blockWriter,
            Func<Block, Task>? onBlockExecuted)
            : base(
                logger,
                platformSettings,
                serviceScopeFactory,
                workflowOutputService,
                documentProcessingService,
                fileStorageManager,
                entityPlaceholderResolver,
                graphBuilder,
                scheduler,
                blockWriter)
        {
            _onBlockExecuted = onBlockExecuted;
        }

        protected override async Task OnBlockExecutedAsync(
            Block block,
            IUserDataUnitOfWork userDataUnitOfWork,
            CancellationToken cancellationToken)
        {
            if (_onBlockExecuted is not null)
            {
                await _onBlockExecuted(block);
            }

            await base.OnBlockExecutedAsync(block, userDataUnitOfWork, cancellationToken);
        }
    }
}
