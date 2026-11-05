using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Common.Text;
using IniaAi.Domain.Signatures;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Domain.Entities.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.Workflows.SaveWorkflow;

public class SaveWorkflowCommandHandler
    : IRequestHandler<SaveWorkflowCommand, ErrorOr<SaveWorkflowResult>>
{
    public const string VERSION_CONFLICT_CODE = WorkflowSavePipeline.VERSION_CONFLICT_CODE;
    public const string CONCURRENT_SAVE_CODE = WorkflowSavePipeline.CONCURRENT_SAVE_CODE;
    public const string CURRENT_HEAD_VERSION_ID_METADATA_KEY =
        WorkflowSavePipeline.CURRENT_HEAD_VERSION_ID_METADATA_KEY;

    private readonly WorkflowSavePipeline _pipeline;
    private readonly ICoreDbContext _coreDbContext;
    private readonly ILogger<SaveWorkflowCommandHandler> _logger;

    public SaveWorkflowCommandHandler(
        WorkflowSavePipeline pipeline,
        ICoreDbContext coreDbContext,
        ILogger<SaveWorkflowCommandHandler> logger)
    {
        _pipeline = pipeline;
        _coreDbContext = coreDbContext;
        _logger = logger;
    }

    public async Task<ErrorOr<SaveWorkflowResult>> Handle(
        SaveWorkflowCommand request,
        CancellationToken cancellationToken)
    {
        Workflow workflow = request.Workflow;

        // The WebApp save path passes a live Workflow object that never went through FromJson,
        // so hoist any residual per-block signature dicts here before canonicalization.
        SignatureFieldsLegacyMigrator.Hoist(workflow);

        IExecutionStrategy strategy = _coreDbContext.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(ct => SaveInTransactionAsync(request, ct), cancellationToken);
    }

    private async Task<ErrorOr<SaveWorkflowResult>> SaveInTransactionAsync(
        SaveWorkflowCommand request,
        CancellationToken cancellationToken)
    {
        _coreDbContext.ClearChangeTracker();

        await using IDbContextTransaction transaction =
            await _coreDbContext.BeginTransactionAsync(cancellationToken);

        try
        {
            ErrorOr<SaveWorkflowResult> result = await _pipeline.SaveCoreAsync(
                request,
                narrativeOverride: null,
                cancellationToken);
            if (!result.IsError)
            {
                await transaction.CommitAsync(cancellationToken);
                _logger.LogInformation(
                    "Saved Core workflow {WorkflowTypeId}: version {VersionId} (deduped={WasDeduped})",
                    LogSanitizer.ForLog(request.WorkflowTypeId),
                    result.Value.VersionId,
                    result.Value.WasDeduped);
            }

            return result;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Reachable for an existing template: two concurrent saves both load and Remove the
            // same single Workflow row; the loser's DELETE affects 0 rows and EF raises this.
            // That delete is the concurrency guard. The only unguarded window is two concurrent
            // first-ever saves of the same type (no prior Workflow row to contend on); concurrent
            // creation of a *new* type instead collides on the WorkflowType primary key.
            _logger.LogWarning(
                ex,
                "Concurrent save detected for workflow {WorkflowTypeId}",
                LogSanitizer.ForLog(request.WorkflowTypeId));

            // Re-read the winner's head so the caller can recover inline, matching the
            // stale-baseVersionId conflict shape. priorHead is the pre-race parent, not the new
            // head, so it would just provoke another conflict. Guarded: a failed read must not
            // turn the 409 into a 500.
            WorkflowVersion? currentHead = await _pipeline.TryReadCurrentHeadAsync(
                request.WorkflowTypeId,
                cancellationToken);

            return Error.Conflict(
                CONCURRENT_SAVE_CODE,
                "The workflow template was modified concurrently; re-read the current version and retry.",
                currentHead is null ? null : WorkflowSavePipeline.BuildHeadMetadata(currentHead));
        }
    }

    public static bool IsVersionConflict(Error error)
    {
        return error.Code is VERSION_CONFLICT_CODE or CONCURRENT_SAVE_CODE;
    }

    public static string? GetCurrentHeadVersionId(Error error)
    {
        return error.Metadata?.GetValueOrDefault(CURRENT_HEAD_VERSION_ID_METADATA_KEY) as string;
    }
}
