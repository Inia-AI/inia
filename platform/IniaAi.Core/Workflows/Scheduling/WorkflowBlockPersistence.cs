using IniaAi.Application.Common.Interfaces.Contexts;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Citations.Traces;
using IniaAi.Domain.Entities.Verification;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Responses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Core.Workflows.Scheduling;

/// <summary>
/// Writes a single executed block's result through its own short-lived pooled
/// <c>UserDataDbContext</c>. Because each block owns a private context and connection, sibling
/// blocks running in parallel never share a change tracker, which is what removes the
/// DetectChanges-vs-mutation race that a single shared context suffers under parallel execution.
/// </summary>
public interface IExecutedBlockWriter
{
    /// <summary>
    /// Persists the block's executed state (ExecutionState + Properties jsonb, and an upsert of its
    /// BlockResponse keyed on BlockId) plus any per-block report/log rows, all in one pooled
    /// context. The block row is assumed to already exist (inserted by the skeleton). Rethrows on
    /// failure so the runner's catch-log-continue records the block as failed rather than dropping it.
    /// </summary>
    Task PersistBlockResultAsync(
        Block block,
        ReportParagraph? paragraph,
        WorkflowExecutionLog? executionLog,
        CancellationToken cancellationToken);

    /// <summary>
    /// Persists the workflow-level and per-section <c>ExecutionState</c> snapshots at the end of a
    /// run. The shared tracker is cleared before execution, so these rows are otherwise never
    /// written.
    /// </summary>
    Task PersistWorkflowExecutionStateAsync(
        Workflow workflow,
        CancellationToken cancellationToken);

    /// <summary>
    /// Inserts a freshly expanded loop block (plus its TextInputs/RagSettings children) without
    /// walking navigations, so a child back-reference to a template block can never drag the
    /// already-persisted section/workflow into the insert.
    /// </summary>
    Task InsertLoopBlockAsync(Block block, CancellationToken cancellationToken);
}

public sealed class WorkflowBlockPersistence : IExecutedBlockWriter
{
    private readonly IUserDataDbContextAccessor _accessor;
    private readonly ILogger<WorkflowBlockPersistence> _logger;

    public WorkflowBlockPersistence(
        IUserDataDbContextAccessor accessor,
        ILogger<WorkflowBlockPersistence> logger)
    {
        _accessor = accessor;
        _logger = logger;
    }

    public async Task PersistBlockResultAsync(
        Block block,
        ReportParagraph? paragraph,
        WorkflowExecutionLog? executionLog,
        CancellationToken cancellationToken)
    {
        // Pooled lease; disposed in the finally to return it to the pool.
        IUserDataDbContext db = await _accessor.CreateDbContextAsync();

        try
        {
            BlockResponse? response = block.BlockResponse;

            // Entry (not Attach) tracks just this single entity without walking the navigation
            // graph, so sibling navs (Workflow/Section/TextInputs/RagSettings) are never pulled in.
            _ = db.Entry(block).State = EntityState.Unchanged;
            db.Entry(block).Property(b => b.ExecutionState).IsModified = true;
            db.Entry(block).Property(b => b.Properties).IsModified = true;
            // BlockResponseId is a denormalized scalar pointer on Block, not the relational FK.
            db.Entry(block).Property(b => b.BlockResponseId).IsModified = true;

            // Resolved inputs live on the block's TextInputs/RagSettings rows; persist them here
            // since nothing else writes them after the shared tracker is cleared.
            foreach (TextInput textInput in block.TextInputs)
            {
                _ = db.Entry(textInput).State = EntityState.Unchanged;
                db.Entry(textInput).Property(t => t.ExecutionState).IsModified = true;
            }

            foreach (RagSettings ragSettings in block.RagSettings)
            {
                _ = db.Entry(ragSettings).State = EntityState.Unchanged;
                db.Entry(ragSettings).Property(r => r.ExecutionState).IsModified = true;
            }

            // Entry-not-Attach never walks this navigation, so collected trace rows must be
            // added explicitly or they silently never reach the database. Rows are append-only
            // evidence: insert the not-yet-persisted ones, never update or delete. Deduped by
            // the natural key (run execution id, attempt, path), not the row id: the shared
            // layer persists a settled run's pack before its acknowledge, and a crash-replayed
            // block re-execution re-collects the same pack under fresh ids - an id check would
            // re-insert those rows into the unique natural-key index and fail the whole persist.
            if (block.AgentRunTraceFiles.Count > 0)
            {
                List<string> runExecutionIds = block.AgentRunTraceFiles
                    .Select(f => f.RunExecutionId)
                    .Distinct()
                    .ToList();
                HashSet<(string, int, string)> alreadyPersisted = (await db.Set<AgentRunTraceFile>()
                    .Where(f => runExecutionIds.Contains(f.RunExecutionId))
                    .Select(f => new { f.RunExecutionId, f.Attempt, f.Path })
                    .ToListAsync(cancellationToken))
                    .Select(key => (key.RunExecutionId, key.Attempt, key.Path))
                    .ToHashSet();

                foreach (AgentRunTraceFile traceFile in block.AgentRunTraceFiles)
                {
                    if (!alreadyPersisted.Contains((traceFile.RunExecutionId, traceFile.Attempt, traceFile.Path)))
                    {
                        _ = db.Entry(traceFile).State = EntityState.Added;
                    }
                }
            }

            // A crash-replayed re-execution re-records a round under a fresh row id, so the
            // upsert keys on the natural key (BlockId, Round), not the row id. The projection
            // skips the Output payloads; a round whose run id is unchanged is already persisted
            // verbatim and is left untouched.
            if (block.AgentRunOutputs.Count > 0)
            {
                List<int> rounds = [.. block.AgentRunOutputs.Select(static row => row.Round)];
                Dictionary<int, (string Id, string RunExecutionId)> persistedByRound =
                    (await db.Set<AgentRunOutput>()
                        .Where(row => row.BlockId == block.Id && rounds.Contains(row.Round))
                        .Select(row => new { row.Round, row.Id, row.RunExecutionId })
                        .ToListAsync(cancellationToken))
                    .ToDictionary(
                        static row => row.Round,
                        static row => (row.Id, row.RunExecutionId));

                foreach (AgentRunOutput attempt in block.AgentRunOutputs)
                {
                    if (!persistedByRound.TryGetValue(attempt.Round, out (string Id, string RunExecutionId) persisted))
                    {
                        _ = db.Entry(attempt).State = EntityState.Added;
                    }
                    else if (!string.Equals(persisted.RunExecutionId, attempt.RunExecutionId, StringComparison.Ordinal))
                    {
                        attempt.Id = persisted.Id;
                        _ = db.Entry(attempt).State = EntityState.Modified;
                    }
                }
            }

            // Failure-record rows are append-only evidence with no FK to dedupe through, keyed
            // by the verifier pass that produced them. Run identity is deterministic per
            // (execution, block, round, pass), so a crash-replayed re-execution re-composes the
            // same pass's rows under fresh row ids — a pass whose RunExecutionId is already
            // persisted is skipped whole, never re-inserted. A later round of the same execution
            // carries its own run id and appends beside them.
            if (block.VerificationCriterionRecords.Count > 0)
            {
                HashSet<string> pendingRunIds = [.. block.VerificationCriterionRecords
                    .Select(static row => row.RunExecutionId)];
                HashSet<string> persistedRunIds = [.. await db.Set<VerificationCriterionRecord>()
                    .Where(row => pendingRunIds.Contains(row.RunExecutionId))
                    .Select(row => row.RunExecutionId)
                    .ToListAsync(cancellationToken)];

                foreach (VerificationCriterionRecord record in block.VerificationCriterionRecords)
                {
                    if (!persistedRunIds.Contains(record.RunExecutionId))
                    {
                        _ = db.Entry(record).State = EntityState.Added;
                    }
                }
            }

            // A re-entering persist replaces the block's data output, as it replaces its paragraph below.
            if (block.DataOutput is ReportDataOutput dataOutput)
            {
                foreach (string staleId in await db.Set<ReportDataOutput>()
                    .Where(row => row.BlockId == block.Id)
                    .Select(row => row.Id)
                    .ToListAsync(cancellationToken))
                {
                    _ = db.Remove(new ReportDataOutput { Id = staleId });
                }

                _ = db.Entry(dataOutput).State = EntityState.Added;
            }

            // One trace row per block, rewritten as the block's report paragraph adds its
            // decisions: keyed on the block, so a re-entering persist updates the row it wrote.
            if (block.CitationTrace is BlockCitationTrace citationTrace)
            {
                string? persistedTraceId = await db.Set<BlockCitationTrace>()
                    .Where(row => row.BlockId == block.Id)
                    .Select(row => row.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (persistedTraceId is null)
                {
                    _ = db.Entry(citationTrace).State = EntityState.Added;
                }
                else
                {
                    citationTrace.Id = persistedTraceId;
                    _ = db.Entry(citationTrace).State = EntityState.Modified;
                }
            }

            if (response is not null)
            {
                // BlockResponses is UNIQUE on BlockId (one row per block), NOT on the PK. The strategy
                // mints a fresh-GUID BlockResponse on every run, so keying the upsert on r.Id would
                // INSERT a second row for the same BlockId on re-execution -> 23505. Key on BlockId
                // and update the existing row in place.
                BlockResponse? existing = await db.Set<BlockResponse>()
                    .FirstOrDefaultAsync(r => r.BlockId == response.BlockId, cancellationToken);

                if (existing is null)
                {
                    _ = db.Entry(response).State = EntityState.Added;
                }
                else
                {
                    existing.RawResponse = response.RawResponse;
                    existing.JsonResponse = response.JsonResponse;
                    existing.HtmlResponse = response.HtmlResponse;
                    existing.MarkdownResponse = response.MarkdownResponse;
                    existing.DebugInfo = response.DebugInfo;
                    existing.RegressionFacts = response.RegressionFacts;
                    existing.BinaryResponse = response.BinaryResponse;
                    existing.MediaType = response.MediaType;
                    block.BlockResponseId = existing.Id;
                }
            }

            // Per-block report/log rows ride along in the same pooled context + single save.
            // A release can re-enter (a taint-queue replay, a crash-replayed re-execution), and
            // both entities mint a fresh GUID per build — so a block-anchored row replaces its
            // predecessor on the natural key instead of appending a duplicate. Both key on the
            // block alone: an executed block row belongs to exactly one execution. This is
            // last-write-wins for every block type — a block that persisted a completed log and
            // later persists an error log keeps only the error row, its final word. Rows without
            // a block id (section-level skip logs) have no re-entering writer and append as
            // before. Stale rows delete as key-only stubs — their payloads (report text,
            // prompts, jsonb) are never read, so they are not fetched.
            if (paragraph is not null)
            {
                if (!string.IsNullOrEmpty(paragraph.BlockId))
                {
                    foreach (string staleId in await db.Set<ReportParagraph>()
                        .Where(row => row.BlockId == paragraph.BlockId)
                        .Select(row => row.Id)
                        .ToListAsync(cancellationToken))
                    {
                        _ = db.Remove(new ReportParagraph { Id = staleId });
                    }
                }

                _ = db.Add(paragraph);
            }

            string? logBlockId = (executionLog ?? paragraph?.WorkflowExecutionLogs?.FirstOrDefault())?.BlockId;
            if (logBlockId is not null)
            {
                foreach (string staleId in await db.Set<WorkflowExecutionLog>()
                    .Where(row => row.BlockId == logBlockId)
                    .Select(row => row.Id)
                    .ToListAsync(cancellationToken))
                {
                    _ = db.Remove(new WorkflowExecutionLog { Id = staleId });
                }
            }

            if (executionLog is not null)
            {
                _ = db.Add(executionLog);
            }

            _ = await db.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to persist block result for block {BlockId} ({BlockName}).",
                block.Id,
                block.Name);

            // This is the only writer of block state (the shared graph is detached), so a swallowed
            // failure would silently lose the block's result. Rethrow so the runner records it.
            throw;
        }
        finally
        {
            if (db is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (db is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    public async Task InsertLoopBlockAsync(Block block, CancellationToken cancellationToken)
    {
        IUserDataDbContext db = await _accessor.CreateDbContextAsync();

        try
        {
            _ = db.Entry(block).State = EntityState.Added;

            foreach (TextInput textInput in block.TextInputs)
            {
                _ = db.Entry(textInput).State = EntityState.Added;
            }

            foreach (RagSettings ragSettings in block.RagSettings)
            {
                _ = db.Entry(ragSettings).State = EntityState.Added;
            }

            _ = await db.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to insert loop block {BlockId} ({BlockName}).", block.Id, block.Name);
            throw;
        }
        finally
        {
            if (db is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (db is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    public async Task PersistWorkflowExecutionStateAsync(
        Workflow workflow,
        CancellationToken cancellationToken)
    {
        IUserDataDbContext db = await _accessor.CreateDbContextAsync();

        try
        {
            _ = db.Entry(workflow).State = EntityState.Unchanged;
            db.Entry(workflow).Property(w => w.ExecutionState).IsModified = true;

            foreach (Section section in workflow.Sections)
            {
                _ = db.Entry(section).State = EntityState.Unchanged;
                db.Entry(section).Property(s => s.ExecutionState).IsModified = true;
            }

            _ = await db.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to persist workflow/section execution state for workflow {WorkflowId}.",
                workflow.Id);
            throw;
        }
        finally
        {
            if (db is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (db is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
