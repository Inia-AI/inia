using System.Collections.Concurrent;
using IniaAi.Core.Workflows.Activity;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Serialization;
using IniaAi.Domain.Specifications.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// The background half of the once-per-version activity script: called on every submission, it
/// generates and stores the stamped version's script when the row does not carry one yet, in its
/// own scope so the submit path never waits. The live template is re-read and verified against
/// the version's content hash before anything is generated — a template edited between the stamp
/// and this pass simply skips, and the newer version's own first run generates instead. Every
/// failure is logged and swallowed: a missing script only means the run keeps its progress-only
/// surface.
/// </summary>
public sealed class WorkflowActivityScriptEnsurer : IWorkflowActivityScriptEnsurer
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WorkflowActivityScriptEnsurer> _logger;
    private readonly ConcurrentDictionary<string, byte> _inFlightVersionIds = new();

    public WorkflowActivityScriptEnsurer(
        IServiceScopeFactory scopeFactory,
        ILogger<WorkflowActivityScriptEnsurer> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void EnsureScriptInBackground(string workflowVersionId, string workflowTypeId)
    {
        if (!_inFlightVersionIds.TryAdd(workflowVersionId, 0))
        {
            return;
        }

        _logger.LogInformation(
            "Ensuring the activity script for workflow version {WorkflowVersionId} ({WorkflowTypeId})",
            workflowVersionId,
            workflowTypeId);

        _ = Task.Run(async () =>
        {
            try
            {
                await EnsureScriptAsync(workflowVersionId, workflowTypeId, CancellationToken.None);
            }
            finally
            {
                _ = _inFlightVersionIds.TryRemove(workflowVersionId, out _);
            }
        });
    }

    public async Task EnsureScriptAsync(
        string workflowVersionId,
        string workflowTypeId,
        CancellationToken cancellationToken)
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ICoreUnitOfWork coreUnitOfWork = scope.ServiceProvider.GetRequiredService<ICoreUnitOfWork>();
            IWorkflowActivityScriptWriter writer =
                scope.ServiceProvider.GetRequiredService<IWorkflowActivityScriptWriter>();
            WorkflowCanonicalizer canonicalizer =
                scope.ServiceProvider.GetRequiredService<WorkflowCanonicalizer>();

            WorkflowVersion? version = await coreUnitOfWork
                .Repository<WorkflowVersion>()
                .GetFirstOrDefaultAsync(new WorkflowVersionByIdForUpdateSpec(workflowVersionId), cancellationToken);

            if (version is null || version.ActivityScriptJson is not null)
            {
                return;
            }

            Workflow? liveTemplate = await coreUnitOfWork.Workflows.GetByTypeIdAsync(workflowTypeId);
            if (liveTemplate is null)
            {
                _logger.LogInformation(
                    "No live template for workflow type {WorkflowTypeId}; version {WorkflowVersionId} keeps no activity script",
                    workflowTypeId,
                    workflowVersionId);
                return;
            }

            string liveHash = canonicalizer.ComputeContentHash(liveTemplate);
            if (!string.Equals(liveHash, version.ContentHash, StringComparison.Ordinal)
                && (version.PayloadTrimmedAt is not null
                    || !string.Equals(liveHash, canonicalizer.ComputeStoredContentHash(version.WorkflowJson), StringComparison.Ordinal)))
            {
                _logger.LogInformation(
                    "Live template for {WorkflowTypeId} no longer matches version {WorkflowVersionId}; a newer version's run will generate instead",
                    workflowTypeId,
                    workflowVersionId);
                return;
            }

            WorkflowActivityScriptWriteResult write = await writer.WriteScriptAsync(liveTemplate, cancellationToken);
            if (write.Outcome is WorkflowActivityScriptWriteOutcome.TransientFailure)
            {
                _logger.LogInformation(
                    "The activity script pass failed transiently for workflow version {WorkflowVersionId}; the next submission retries",
                    workflowVersionId);
                return;
            }

            if (write.Script is { } script)
            {
                version.ActivityScriptJson = WorkflowActivityScriptSerializer.Serialize(script);
            }
            else
            {
                _logger.LogInformation(
                    "The activity script pass declined workflow version {WorkflowVersionId}; caching the decline so submissions stop paying for it",
                    workflowVersionId);
                version.ActivityScriptJson = WorkflowActivityScriptSerializer.SerializeDeclined();
            }

            _ = await coreUnitOfWork.CompleteAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Could not ensure the activity script for workflow version {WorkflowVersionId}; runs keep their progress-only surface",
                workflowVersionId);
        }
    }
}
