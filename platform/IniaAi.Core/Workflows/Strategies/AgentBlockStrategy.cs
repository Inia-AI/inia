using ErrorOr;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Services.AgentRuntime;
using IniaAi.Core.Workflows.Agents;
using IniaAi.Core.Workflows.Annotations;
using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.ExecutionStates.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IniaAi.Core.Workflows.Strategies;

public class AgentBlockStrategy : IBlockStrategy
{
    private const string BLOCK_KIND = "An Agent block";
    private const string RUN_NOUN = "Agent run";

    /// <summary>
    /// Platform-side subtype stamped on a settled run whose declared output files could not be
    /// composed into the block output.
    /// </summary>
    public const string OUTPUT_FILES_UNUSABLE_SUBTYPE = "output_files_unusable";

    /// <summary>
    /// Overridable so tests can drive a real run to its deadline without waiting minutes; the
    /// policy itself lives on <see cref="AgentRunBlockSupport.PollDeadline"/>.
    /// </summary>
    protected virtual TimeSpan ResolvePollDeadline(AgentRuntimeSettings settings, int? declaredDeadlineMinutes)
    {
        return AgentRunBlockSupport.PollDeadline(settings, declaredDeadlineMinutes);
    }

    public async Task<BlockStrategyOutput> ExecuteBlockAsync(
        Block block,
        BlockExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        block.ExecutionState ??= new BlockExecutionState(block);
        BlockStrategyOutput output = new();

        AgentProperties props = block.Properties as AgentProperties
            ?? throw new InvalidOperationException($"Block properties must be of type {nameof(AgentProperties)}");
        props.ExecutionState ??= new(props);
        AgentPropertiesExecutionState executionState = props.ExecutionState;

        if (context.AgentRunExecutor is not IAgentRunExecutor executor)
        {
            throw Fail(
                block, context,
                "The agent runtime is disabled on this host (AgentRuntimeSettings.Enabled=false), so the Agent block cannot execute");
        }

        if (props.WorkerSkills.Count == 0)
        {
            throw Fail(block, context, "An Agent block needs at least one worker skill");
        }

        if (AgentRunOutputFiles.DescribeFirstPathProblem(props.OutputFiles) is string pathProblem)
        {
            throw Fail(block, context, $"The Agent block's {pathProblem}");
        }

        string workflowExecutionId = AgentRunBlockSupport.ResolveRunIdentitySource(block, context, BLOCK_KIND);
        int round = context.VerificationRound;

        executionState.RunPasses ??= [];

        // An editor re-run executes the same live instance — this invocation owns its round's
        // rows. Safe while no path re-executes a leg in place — a resume-in-place feature would
        // wipe the leg's leaked-run rows here and must move this.
        _ = executionState.RunPasses.RemoveAll(pass => pass.Round == round);

        executionState.WorkerSkills = AgentRunBlockSupport.RenderSkills(props.WorkerSkills, block, context);
        AgentRunBlockSupport.RejectUnusableRenderedSkills(executionState.WorkerSkills, block, context);
        executionState.Prompt = AgentRunBlockSupport.RenderTemplate(props.PromptTemplate, "Prompt", block, context);
        executionState.SystemPrompt = AgentRunBlockSupport.RenderTemplate(
            props.SystemPromptTemplate, "System prompt", block, context);
        string? effort = string.IsNullOrWhiteSpace(props.Effort) ? null : props.Effort;
        JsonObject? outputSchema = ResolveOutputSchema(executionState.OutputSchema, block, context);

        // Stated once: the leg's role names both the run id and the desk registration, and a
        // strategy that let the two disagree would ship a feed row contradicting its own run id.
        const string role = AgentRunIds.WORKER_ROLE;
        string ExecutionIdForPass(int pass) => AgentRunIds.For(
            workflowExecutionId, block.Id, role, round, pass);
        string executionId = ExecutionIdForPass(AgentRunIds.FIRST_PASS);

        StagedDocumentSet staged = await ResolveDocumentsAsync(
            block, context, [.. executionState.WorkerSkills.Select(static skill => skill.Name)], cancellationToken);
        List<AgentRunDocument> documents = staged.RunDocuments;
        List<AgentRunSkillFile> skillFiles = AgentRunBlockSupport.ToSkillFiles(executionState.WorkerSkills);

        string basePrompt = string.IsNullOrWhiteSpace(executionState.Prompt)
            ? AgentRunBlockSupport.DEFAULT_WORKER_PROMPT
            : executionState.Prompt;
        if (props.OutputFiles.Count > 0)
        {
            basePrompt += "\n\n" + AgentRunOutputFiles.TaskInstruction(props.OutputFiles);
        }

        VerificationRedoLeg? redoLeg = context.VerificationRedoLeg;

        // On a scratch leg the directive is a preamble over the block's own prompt; on a
        // continuation the revision turn IS the prompt — delivered as the resumed session's
        // answer, which already knows its task. The scratch composition is always in hand as
        // the continuation's degrade shape.
        string scratchPrompt = redoLeg is null
            ? basePrompt
            : redoLeg.ScratchDirective + "\n\n" + basePrompt;
        string prompt = redoLeg?.Revise?.Directive ?? scratchPrompt;

        AgentRuntimeSettings settings = context.PlatformSettings.AgentRuntime;

        AgentRunStartRequest request = AgentRunFacts.WithRunFacts(new AgentRunStartRequest(executionId, prompt, RunToken: null)
        {
            Documents = documents,
            SkillFiles = skillFiles,
            Model = AgentRunBlockSupport.RuntimeModelOf(props, context),
            Effort = effort,
            SystemPrompt = string.IsNullOrWhiteSpace(executionState.SystemPrompt)
                ? null
                : executionState.SystemPrompt,
            OutputSchema = outputSchema,
            DeadlineMs = AgentRunBlockSupport.DeadlineMsOf(props),
            MaxBudgetUsd = props.MaxBudgetUsd,
            MaxTurns = props.MaxTurns,
            MaxRetainedOutputBytes = AgentRunBlockSupport.RetainedOutputBytesOf(props),
            Continuation = redoLeg?.Revise?.Continuation,
        });

        if (request.Continuation is not null
            && AgentRunInputCapacity.StartRequestBytes(request) > AgentRunInputCapacity.INPUT_BUDGET_BYTES)
        {
            // The planner cannot see document bytes, so the shared-budget check lands here,
            // where they are known — degraded before any HTTP rather than refused by the
            // client, which would fail the leg and waste the round (D32, D36e).
            context.Logger.LogInformation(
                "{block} the re-carried out/ set plus this leg's inputs decode past the runtime's "
                + "{Budget}-byte input budget; the leg runs from scratch — a recorded override, not a failure",
                block, AgentRunInputCapacity.INPUT_BUDGET_BYTES);

            redoLeg = redoLeg! with { Revise = null, ActualRedoMode = VerificationRedoLeg.SCRATCH_MODE };
            prompt = scratchPrompt;
            request = request with { Prompt = prompt, Continuation = null };
        }

        if (redoLeg is not null || props.OutputFiles.Count > 0)
        {
            // The composed value is written back so the durable record of what this leg was
            // asked lives on the block, not only in the trace pack, and the editor's
            // "User prompt after execution" panel keeps telling the truth about the wire.
            executionState.Prompt = prompt;
        }

        var activity = AgentRunActivityRegistration.ForBlock(
            executionId,
            context.OrganizationId ?? string.Empty,
            workflowExecutionId,
            block.Id,
            block.Name ?? string.Empty,
            round,
            role);

        var observedAttempts = new List<AgentRunAttempt>();
        AgentRunExecutionOptions options = AgentRunBlockSupport.BuildExecutionOptions(
            block, context, settings, activity,
            ResolvePollDeadline(settings, props.DeadlineMinutes),
            ExecutionIdForPass, props, round, observedAttempts, RUN_NOUN, redoLeg);

        if (request.Continuation is not null)
        {
            options = options with { ContinuationFallbackPrompt = scratchPrompt };
        }

        try
        {
            ErrorOr<AgentRunConclusion> concluded;
            try
            {
                concluded = await executor.ExecuteAsync(request, options, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await RecordRunSpendAsync(block, context, props, observedAttempts, traceFiles: null, round, redoLeg);
                throw;
            }

            if (concluded.IsError)
            {
                IReadOnlyList<AgentRunAttempt> attempts = AgentRunAttempts.Of(concluded.FirstError);
                IReadOnlyList<AgentRunTraceFile>? failureTraces = AgentRunAttempts.TraceFilesOf(concluded.FirstError);
                await RecordRunSpendAsync(block, context, props, attempts, failureTraces, round, redoLeg);
                AgentRunBlockSupport.AttachTraces(failureTraces, block, context);

                throw Fail(
                    block, context,
                    MachineryFailureMessage(concluded.FirstError, executionId, options.Deadline, attempts));
            }

            AgentRunConclusion conclusion = concluded.Value;
            AgentRunBlockSupport.AttachTraces(conclusion.TraceFiles, block, context);
            await RecordRunSpendAsync(block, context, props, conclusion.Attempts, conclusion.TraceFiles, round, redoLeg);
            AgentRunBlockSupport.RecordRunSmell(props.ExecutionState!.RunPasses, conclusion);

            if (conclusion.Monitor.TryGetResult(out AgentRunResult? result))
            {
                string blockOutput = ComposeBlockOutput(block, context, props, conclusion, result.Output ?? string.Empty);
                AgentRunOutput attempt = AgentRunBlockSupport.RecordAttemptOutput(
                    block, context, round, conclusion.Monitor.ExecutionId, blockOutput);
                AgentRunBlockSupport.SettleResponse(block, attempt);
                if (context.IsCitationSource)
                {
                    List<Fact> citationFacts = AgentCitationFacts.Build(
                        result.Output, staged.ByStagedName, context.AllPages);
                    context.BlockAnnotations.AddFacts(citationFacts);
                    // Store on the attempt row so a best-attempt restore re-applies them; they
                    // resolve against staged pages at run time and never survive in the text.
                    attempt.CitationFactsJson = citationFacts.Count > 0
                        ? AgentCitationFactStore.Serialize(citationFacts)
                        : null;
                }

                return output;
            }

            if (conclusion.Monitor.TryGetError(out AgentRunError? error))
            {
                throw Fail(
                    block, context,
                    $"Agent run failed with subtype '{error.Subtype ?? "unknown"}'. "
                    + AgentRunBlockSupport.SpendSummary(conclusion.Attempts));
            }

            throw Fail(block, context, $"Agent run ended {conclusion.Monitor.Status} without a usable result");
        }
        finally
        {
            // A run bought under the continuation fallback was asked the scratch composition,
            // and the durable record must say so whatever exit this invocation takes.
            if (observedAttempts.Any(static attempt => attempt.RanContinuationFallback))
            {
                executionState.Prompt = scratchPrompt;
            }
        }
    }

    /// <summary>
    /// With no declared output files the block output is the model's answer, unchanged. With
    /// files, it is the answer plus the files the settled run itself published, read from that
    /// run's collected rows only, so an earlier pass's or round's copy never stands in. Stored on
    /// the attempt row, so a best-attempt restore brings back the files of the attempt it selects.
    /// </summary>
    private static string ComposeBlockOutput(
        Block block,
        BlockExecutionContext context,
        AgentProperties props,
        AgentRunConclusion conclusion,
        string answer)
    {
        if (props.OutputFiles.Count == 0)
        {
            return answer;
        }

        string runId = conclusion.Monitor.ExecutionId;
        string? problem = conclusion.TraceFiles is null
            ? $"The platform could not collect the published files of run '{runId}', so the Agent block's output files are unavailable"
            : null;
        if (problem is null)
        {
            ErrorOr<string> composed = AgentRunOutputFiles.Compose(
                answer, props.OutputFiles, AgentRunOutputFiles.SettledOutFilesOf(conclusion));
            if (!composed.IsError)
            {
                return composed.Value;
            }

            problem = $"{composed.FirstError.Description} (run '{runId}')";
        }

        // The run settled, but the block produced nothing: its ledger row must not read as a
        // settled pass, or the verification loop would count this leg as a redo that stands.
        foreach (AgentRunPass pass in props.ExecutionState!.RunPasses.Where(
            pass => string.Equals(pass.ExecutionId, runId, StringComparison.Ordinal)))
        {
            pass.Status = AgentRunStatus.Failed.ToString();
            pass.FailureSubtype = OUTPUT_FILES_UNUSABLE_SUBTYPE;
        }

        throw Fail(block, context, problem);
    }

    private static Task RecordRunSpendAsync(
        Block block,
        BlockExecutionContext context,
        AgentProperties props,
        IReadOnlyList<AgentRunAttempt> attempts,
        IReadOnlyList<AgentRunTraceFile>? traceFiles,
        int round,
        VerificationRedoLeg? redoLeg)
    {
        return AgentRunBlockSupport.RecordRunSpendAsync(
            block,
            context,
            AgentRunBlockSupport.RuntimeModelOf(props, context),
            $"Agent block '{block.Name}'",
            props.ExecutionState!.RunPasses,
            attempts,
            traceFiles,
            round,
            redoLeg);
    }

    /// <summary>
    /// The executor's machinery errors keep their block-era failure texts: the run id, the
    /// block deadline, and the single-pass policy are the block's own vocabulary, not the
    /// layer's.
    /// </summary>
    private static string MachineryFailureMessage(
        Error error,
        string executionId,
        TimeSpan blockDeadline,
        IReadOnlyList<AgentRunAttempt> attempts)
    {
        string failedRunId = attempts.Count > 0 ? attempts[^1].ExecutionId : executionId;
        string message = error.Code switch
        {
            AgentRunExecutorErrors.START_FAILED =>
                $"Agent run start failed: {error.Description}",
            AgentRunExecutorErrors.ADMISSION_EXHAUSTED =>
                $"The agent runtime had no capacity for run '{failedRunId}' for as long as this block waited: {error.Description}",
            AgentRunExecutorErrors.POLL_FAILED =>
                $"Polling agent run '{failedRunId}' failed: {error.Description}",
            AgentRunExecutorErrors.DEADLINE_EXCEEDED =>
                $"Agent run '{failedRunId}' did not finish within the block deadline of {blockDeadline}; "
                + $"last status {LastObservedStatus(error)}",
            AgentRunExecutorErrors.UNKNOWN_STATUS =>
                $"Agent run '{failedRunId}' reported an unknown status; withholding its acknowledgement because its true state is unknowable",
            AgentRunExecutorErrors.AWAITING_INPUT =>
                $"Agent run '{failedRunId}' paused for input, which the Agent block cannot answer",
            _ => $"Agent run '{failedRunId}' failed: {error.Code}: {error.Description}",
        };

        return attempts.Count == 0
            ? message
            : $"{message}. {AgentRunBlockSupport.SpendSummary(attempts)}";
    }

    private static object? LastObservedStatus(Error error)
    {
        return error.Metadata?.GetValueOrDefault(AgentRunExecutorErrors.STATUS_METADATA_KEY);
    }

    private static JsonObject? ResolveOutputSchema(string outputSchema, Block block, BlockExecutionContext context)
    {
        ErrorOr<JsonObject?> parsed = AgentOutputSchemas.Parse(outputSchema);
        if (!parsed.IsError)
        {
            return parsed.Value;
        }

        if (parsed.FirstError.Code == AgentOutputSchemas.ERROR_INVALID_JSON
            && parsed.FirstError.Metadata?.GetValueOrDefault(AgentOutputSchemas.EXCEPTION_METADATA_KEY) is JsonException exception)
        {
            context.Logger.LogError(exception, "{block} Output schema is not valid JSON", block);
            throw new InvalidOperationException("The Agent block's output schema is not valid JSON", exception);
        }

        throw Fail(block, context, "The Agent block's output schema must be a JSON object");
    }

    /// <summary>
    /// What staging produced, both halves: the wire documents the run request carries, and the
    /// staged-name-to-document map that citation resolution needs afterwards — the staged name
    /// is the only identity the agent can utter for a file, so discarding the map would orphan
    /// every citation the run reports.
    /// </summary>
    private sealed record StagedDocumentSet(
        List<AgentRunDocument> RunDocuments,
        Dictionary<string, Document> ByStagedName);

    private static async Task<StagedDocumentSet> ResolveDocumentsAsync(
        Block block,
        BlockExecutionContext context,
        IReadOnlyCollection<string> skillFileNames,
        CancellationToken cancellationToken)
    {
        List<Document> documents = ResolveClassScopedDocuments(block, context);
        if (documents.Count == 0)
        {
            return new StagedDocumentSet([], []);
        }

        var usedNames = new HashSet<string>(skillFileNames, StringComparer.Ordinal);
        var byStagedName = new Dictionary<string, Document>(documents.Count, StringComparer.Ordinal);
        List<Task<AgentRunDocument>> resolutions = new(documents.Count);
        foreach (Document document in documents)
        {
            string fileName = document.FileName;
            string stagedName = AgentStagedNames.Stage(fileName, usedNames);
            if (!string.Equals(stagedName, fileName, StringComparison.Ordinal))
            {
                context.Logger.LogWarning(
                    "{block} Document {documentId} is staged for the agent run under another name to fit the runtime's input-name rules",
                    block, document.Id);
            }

            byStagedName[stagedName] = document;
            resolutions.Add(AgentRunBlockSupport.StageDocumentAsync(
                document, stagedName, block, context, cancellationToken));
        }

        return new StagedDocumentSet([.. await Task.WhenAll(resolutions)], byStagedName);
    }

    private static List<Document> ResolveClassScopedDocuments(Block block, BlockExecutionContext context)
    {
        List<Document> documents = block.GetClassScopedDocuments();

        if (documents.Count == 0 && block.IsScopedToDocumentClasses())
        {
            throw Fail(
                block, context,
                $"The Agent block is scoped to document classes ({string.Join(", ", block.SupportedDocumentClasses)}) "
                + "but no uploaded document carries any of them - upload a matching document or widen the block's document classes");
        }

        if (documents.Count > 0 && documents.Count < block.Documents.Count)
        {
            context.Logger.LogInformation(
                "{block} {ExcludedCount} attached document(s) fall outside the block's document classes and are not sent to the agent",
                block,
                block.Documents.Count - documents.Count);
        }

        return documents;
    }

    private static InvalidOperationException Fail(Block block, BlockExecutionContext context, string message)
    {
        return AgentRunBlockSupport.Fail(block, context, message);
    }
}
