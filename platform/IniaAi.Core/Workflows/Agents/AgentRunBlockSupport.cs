using IniaAi.AiConnection.Common;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Documents;
using IniaAi.Core.Services.AgentRuntime;
using IniaAi.Core.Services.TokenUsage;
using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Responses;
using Microsoft.Extensions.Logging;
using System.Globalization;
using TokenExecutionContext = IniaAi.Domain.Entities.TokenUsage.Enums.ExecutionContext;

namespace IniaAi.Core.Workflows.Agents;

/// <summary>
/// One settled run measured against the fabrication bars: how long the runtime says it took, and
/// whether it came back faster or cheaper than a real run plausibly could.
/// </summary>
internal readonly record struct AgentRunSmell(
    double Seconds,
    bool SuspiciouslyFast,
    bool SuspiciouslyCheap);

/// <summary>
/// The dispatch scaffold and run bookkeeping every agent-family block strategy shares:
/// template rendering under the agent grammar, run-identity resolution, the skill and fuse
/// wire mappings, execution-options wiring (the pre-poll unacknowledged-row contract),
/// document staging, trace anchoring and attachment, and spend recording. A shared
/// block-strategy layer — it speaks <see cref="Block"/> and <see cref="BlockExecutionContext"/>,
/// so genuinely platform-wide agent integration belongs beside the runtime client instead.
/// The strategies keep only their block's own product rules: the request content and what a
/// conclusion means.
/// </summary>
internal static class AgentRunBlockSupport
{
    /// <summary>
    /// What a worker is asked when its block declares no prompt — also what the verifier's
    /// posed-task file shows for such a member, so the record matches what was actually sent.
    /// </summary>
    public const string DEFAULT_WORKER_PROMPT =
        "Process the provided documents and produce your deliverables.";

    /// <summary>
    /// The fabrication failure class measured near-instant, near-zero-spend runs carrying fabricated
    /// answers, so a run under either bar is stamped on its ledger row — recorded provenance,
    /// never a block and never a re-buy (§3.6). One bar for the whole family: a verifier's
    /// judgment and a member's redo leg are suspicious under the same numbers, which is what
    /// makes a whole round's signals comparable.
    /// </summary>
    public const double SUSPICIOUSLY_FAST_SECONDS = 30;

    /// <inheritdoc cref="SUSPICIOUSLY_FAST_SECONDS"/>
    public const decimal SUSPICIOUSLY_CHEAP_USD = 0.02m;

    /// <summary>
    /// How fast a whole stage of legs must come back for its all-failed outcome to read as
    /// infrastructure rather than as work: no provider answers a group of agents this quickly.
    /// Wider than the per-run bar because a stage is several legs, serialized or not.
    /// </summary>
    private const double INSTANT_BURST_SECONDS = 60;

    private const string AGENT_RUNTIME_PROVIDER = "agent-runtime";
    private const string COST_FORMAT = "0.####";
    private const int MILLISECONDS_PER_MINUTE = 60_000;
    private const int BYTES_PER_MIB = 1024 * 1024;

    /// <summary>
    /// How long a block keeps polling. A declared deadline is the run's own, so the block
    /// waits it out plus a margin covering admission, the runtime's teardown and poll spacing,
    /// and sees the runtime's verdict instead of racing it. A block that declared none is
    /// governed by the runtime's own deadline, which the platform does not know, so the block
    /// spends its own patience budget instead. Zero and negative count as none: the save gate
    /// refuses both, and until it does the editor's live estimate card would otherwise quote a
    /// two-minute window, or a negative one, beside a caption saying the leg was charged the
    /// platform's poll patience.
    /// </summary>
    public static TimeSpan PollDeadline(AgentRuntimeSettings settings, int? declaredDeadlineMinutes)
    {
        return declaredDeadlineMinutes is int declared and > 0
            ? TimeSpan.FromMinutes(declared) + TimeSpan.FromSeconds(settings.RunDeadlineMarginSeconds)
            : TimeSpan.FromMinutes(settings.MaxPollDeadlineMinutes);
    }

    /// <summary>The model id the block's run is started and billed under, for the executing organization.</summary>
    public static string RuntimeModelOf(IAgentRunProperties props, BlockExecutionContext context)
    {
        return ModelTiersOf(context.PlatformSettings, context.OrganizationId).RunModelOf(props);
    }

    public static AgentModelTiers ModelTiersOf(PlatformSettings settings, string? organizationId)
    {
        return AgentModelTiers.For(settings.AgentRuntime, settings.ModelRouting, organizationId);
    }

    public static long? DeadlineMsOf(IAgentRunProperties props)
    {
        return (long?)props.DeadlineMinutes * MILLISECONDS_PER_MINUTE;
    }

    public static long? RetainedOutputBytesOf(IAgentRunProperties props)
    {
        return (long?)props.MaxRetainedOutputMib * BYTES_PER_MIB;
    }

    public static List<AgentRunSkillFile> ToSkillFiles(List<AgentWorkerSkill> skills)
    {
        return [.. skills.Select(static skill => new AgentRunSkillFile(skill.Name, skill.Content, skill.Description)
        {
            Assets = [.. (skill.Assets ?? []).Select(static asset =>
                new AgentRunSkillAsset(asset.Name, asset.Content))],
        })];
    }

    /// <summary>
    /// The execution-options wiring both strategies must keep in step: the settled-attempt
    /// ledger fan-out and the pre-poll durability contract — an unacknowledged row written and
    /// flushed the moment a run starts, so a process death mid-poll still leaves a row naming
    /// the burning run for the leaked-run janitor. The callbacks read the ledger through the
    /// family surface on every invocation, so a re-resolved execution state keeps receiving
    /// rows instead of orphaning them on a captured list.
    /// </summary>
    public static AgentRunExecutionOptions BuildExecutionOptions(
        Block block,
        BlockExecutionContext context,
        AgentRuntimeSettings settings,
        AgentRunActivityRegistration activity,
        TimeSpan deadline,
        Func<int, string> executionIdForPass,
        IAgentRunProperties runProperties,
        int round,
        List<AgentRunAttempt> observedAttempts,
        string runNoun,
        VerificationRedoLeg? redoLeg = null)
    {
        AgentRunTraceAnchor? traceAnchor = ResolveTraceAnchor(block, context);

        return new AgentRunExecutionOptions(deadline, activity)
        {
            AdmissionPatience = TimeSpan.FromSeconds(settings.AdmissionPatienceSeconds),
            MaxPasses = settings.MaxRunPasses,
            UpstreamErrorRetryDelay = TimeSpan.FromSeconds(settings.UpstreamErrorRetryDelaySeconds),
            PollErrorTolerance = settings.PollErrorTolerance,
            ExecutionIdForPass = executionIdForPass,
            TraceAnchor = traceAnchor,
            SessionBundleAnchor = ResolveSessionBundleAnchor(traceAnchor, context, round),
            OnAttempt = attempt =>
            {
                observedAttempts.Add(attempt);
                RecordSettledPass(
                    runProperties.RunLedger
                        ?? throw new InvalidOperationException("The block's execution state vanished mid-run"),
                    attempt, round, redoLeg);
            },
            OnRunStarted = async (started, flushCancellationToken) =>
            {
                List<AgentRunPass> ledger = runProperties.RunLedger
                    ?? throw new InvalidOperationException("The block's execution state vanished mid-run");
                var startedPass = new AgentRunPass
                {
                    Round = round,
                    Pass = started.Pass,
                    ExecutionId = started.ExecutionId,
                    Unacknowledged = true,
                };
                StampRedoModes(startedPass, StampSource(redoLeg, started.RanContinuationFallback));
                ledger.Add(startedPass);

                if (context.FlushBlockStateAsync is { } flushBlockState)
                {
                    await flushBlockState(flushCancellationToken);
                    context.Logger.LogInformation(
                        "{block} {RunNoun} '{ExecutionId}' (pass {Pass}) bookkeeping persisted before polling",
                        block, runNoun, started.ExecutionId, started.Pass);
                }
            },
        };
    }

    /// <summary>
    /// How a platform document becomes a staged run input — one owner for the byte source and
    /// content-type handling, whichever block stages it.
    /// </summary>
    public static async Task<AgentRunDocument> StageDocumentAsync(
        Document document,
        string stagedName,
        Block block,
        BlockExecutionContext context,
        CancellationToken cancellationToken)
    {
        FileMetadata metadata = document.FileMetadata
            ?? throw Fail(block, context, $"Document '{document.Name}' has no file metadata to load bytes from");

        byte[] bytes = await context.FileStorageManager.ReadDocumentBytesAsync(
            metadata.OrganizationId, metadata, cancellationToken);

        string? documentClass = document.CurrentDocumentInfo?.DocumentClass;

        return new AgentRunDocument(
            stagedName,
            bytes,
            metadata.ContentType,
            string.IsNullOrWhiteSpace(documentClass) ? null : documentClass);
    }

    /// <summary>
    /// The round's live ledger row — the latest settled pass of the given round, read
    /// through the family surface so the read rule sits beside <see cref="RecordSettledPass"/>,
    /// which writes it. A historical read: a superseded round still settled, so it still
    /// answers here.
    /// </summary>
    public static AgentRunPass? SettledPassFor(Block block, int round)
    {
        return PassesForRound(block, round)
            .Where(IsSettled)
            .OrderBy(static pass => pass.Pass)
            .LastOrDefault();
    }

    /// <summary>
    /// Every run the block bought for one round, settled or not — what a caller reading the
    /// round's spend and outcomes needs, where <see cref="SettledPassFor"/> answers only about
    /// the run that produced work.
    /// </summary>
    public static IReadOnlyList<AgentRunPass> PassesForRound(Block block, int round)
    {
        return [.. (block.Properties as IAgentRunProperties)?.RunLedger?.Where(pass => pass.Round == round) ?? []];
    }

    /// <summary>
    /// The mode a round's leg actually ran with, read off its last ledger row — the row the
    /// dispatching strategy stamps, including when the executor re-ran a failed continuation
    /// from scratch mid-call. Null when the leg never started a run, in which case the caller
    /// falls back to what it planned.
    /// </summary>
    public static string? ActualRedoModeOf(Block block, int round)
    {
        return (block.Properties as IAgentRunProperties)?.RunLedger
            ?.Where(pass => pass.Round == round)
            .MaxBy(static pass => pass.Pass)?.ActualRedoMode;
    }

    /// <summary>
    /// The block's current deliverable's ledger row: the latest settled pass whose attempt
    /// still stands — best-attempt retention marks a rejected redo's pass
    /// <see cref="AgentRunPass.Superseded"/>, so a restored member answers with the round it
    /// actually released. A member that was not redone settles nothing past round one, so its
    /// round-one pass keeps answering at every later verify round; a member whose redo leg
    /// failed answers with its last good round the same way.
    /// </summary>
    public static AgentRunPass? LatestSettledPass(Block block)
    {
        return (block.Properties as IAgentRunProperties)?.RunLedger
            ?.Where(static pass => IsSettled(pass) && !pass.Superseded)
            .OrderBy(static pass => pass.Round)
            .ThenBy(static pass => pass.Pass)
            .LastOrDefault();
    }

    private static bool IsSettled(AgentRunPass pass)
    {
        return string.Equals(pass.Status, AgentRunStatus.Succeeded.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Stamps a rejected round's ledger rows <see cref="AgentRunPass.Superseded"/> — the write
    /// counterpart of <see cref="LatestSettledPass"/>'s filter, kept beside it so the pair
    /// stays one rule.
    /// </summary>
    public static void MarkRoundSuperseded(Block block, int round)
    {
        foreach (AgentRunPass rejected in PassesForRound(block, round))
        {
            rejected.Superseded = true;
        }
    }

    /// <summary>
    /// One round's durable attempt row, the read counterpart of
    /// <see cref="RecordAttemptOutput"/> — best-attempt selection restores a member from it.
    /// </summary>
    public static AgentRunOutput? AttemptOutputFor(Block block, int round)
    {
        return block.AgentRunOutputs.FirstOrDefault(row => row.Round == round);
    }

    /// <summary>
    /// Editor test executions (BlockTest/SectionTest) run without a persisted workflow
    /// execution, so each click mints a fresh identity — reusing one would payload_conflict
    /// against the runtime when the author edits content between clicks. A missing id on any
    /// other path is a wiring bug and stays a loud failure.
    /// </summary>
    public static string ResolveRunIdentitySource(Block block, BlockExecutionContext context, string blockKind)
    {
        if (!string.IsNullOrWhiteSpace(context.WorkflowExecutionId))
        {
            return context.WorkflowExecutionId;
        }

        if (context.ExecutionContext is TokenExecutionContext.BlockTest or TokenExecutionContext.SectionTest)
        {
            return $"{context.ExecutionContext}-{Guid.NewGuid():N}";
        }

        throw Fail(block, context, $"{blockKind} needs a workflow execution id to mint its run identity");
    }

    /// <summary>
    /// Runs before documents are fetched: a description is only bounded once its tags resolve, so
    /// a template that saved clean can still render past the cap, and the client would otherwise
    /// refuse the run after every document's bytes had already been downloaded.
    /// </summary>
    public static void RejectUnusableRenderedSkills(
        List<AgentWorkerSkill> skills,
        Block block,
        BlockExecutionContext context)
    {
        if (AgentSkillNames.DescribeFirstProblem(
            skills.Select(static skill => (skill.Name, skill.Description))) is string problem)
        {
            throw Fail(block, context, problem);
        }
    }

    public static List<AgentWorkerSkill> RenderSkills(
        List<AgentWorkerSkill> workerSkills,
        Block block,
        BlockExecutionContext context)
    {
        return [.. workerSkills.Select(skill => new AgentWorkerSkill
        {
            Name = skill.Name,
            Description = RenderTemplate(skill.Description, $"Skill '{skill.Name}' description", block, context),
            Content = RenderTemplate(skill.Content, $"Skill '{skill.Name}'", block, context),
            Assets = skill.Assets?.Select(asset => RenderAsset(asset, skill.Name, block, context)).ToList(),
        })];
    }

    /// <summary>
    /// Assets are file bytes, not prose: an unflagged one carries over verbatim, so a tag-shaped
    /// string inside a script stays literal. Only a file the author opted in with
    /// <see cref="AgentWorkerSkillAsset.FillTags"/> renders under the same rules as skill content.
    /// </summary>
    private static AgentWorkerSkillAsset RenderAsset(
        AgentWorkerSkillAsset asset,
        string skillName,
        Block block,
        BlockExecutionContext context)
    {
        AgentWorkerSkillAsset rendered = asset.Clone();

        if (asset.FillTags)
        {
            rendered.Content = RenderTemplate(asset.Content, $"Skill '{skillName}' file '{asset.Name}'", block, context);
        }

        return rendered;
    }

    public static string RenderTemplate(
        string template,
        string fieldName,
        Block block,
        BlockExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return string.Empty;
        }

        AgentTemplateRenderer.AgentRenderResult result = AgentTemplateRenderer.Render(template, context.TextInputs);
        foreach (string warning in result.Warnings)
        {
            context.Logger.LogWarning("{block} {Field}: {warning}", block, fieldName, warning);
        }

        return result.Content;
    }

    /// <summary>
    /// Attaches the conclusion's evidence rows with ledger-anchored retention, and stamps the
    /// block's own identity onto them. Retention keys on the run ledger, which every path
    /// populates before this runs: a prior round's rows stay (their runs are the block's own
    /// ledgered history — the verification loop re-executes the same live instance per round),
    /// a row whose run the ledger no longer knows goes (an editor re-run wiped the ledger, so
    /// the row is another execution's leftovers), and a row matching an incoming run id is
    /// replaced (a crash replay re-collects the same run). Null rows mean collection never
    /// produced an outcome, so the prior evidence stays untouched.
    /// </summary>
    public static void AttachTraces(
        IReadOnlyList<AgentRunTraceFile>? traceFiles,
        Block block,
        BlockExecutionContext context)
    {
        if (traceFiles is null)
        {
            return;
        }

        HashSet<string> ledgeredRunIds = new(
            (block.Properties as IAgentRunProperties)?.RunLedger?.Select(static pass => pass.ExecutionId) ?? [],
            StringComparer.Ordinal);
        HashSet<string> incomingRunIds = new(
            traceFiles.Select(static file => file.RunExecutionId), StringComparer.Ordinal);

        foreach (AgentRunTraceFile stale in block.AgentRunTraceFiles
            .Where(row => !ledgeredRunIds.Contains(row.RunExecutionId)
                || incomingRunIds.Contains(row.RunExecutionId))
            .ToList())
        {
            _ = block.AgentRunTraceFiles.Remove(stale);
        }

        foreach (AgentRunTraceFile file in traceFiles)
        {
            file.OrganizationId = context.OrganizationId ?? string.Empty;
            file.BlockId = block.Id;
            file.Block = block;
            block.AgentRunTraceFiles.Add(file);
        }
    }

    /// <summary>
    /// An editor re-run executes the same live instance, so this invocation owns its round's
    /// row and replaces only that. The row never touches the block's response: settling is a
    /// separate act, so the loop can put best-attempt selection between the two.
    /// </summary>
    public static AgentRunOutput RecordAttemptOutput(
        Block block,
        BlockExecutionContext context,
        int round,
        string runExecutionId,
        string output)
    {
        foreach (AgentRunOutput stale in block.AgentRunOutputs.Where(row => row.Round == round).ToList())
        {
            _ = block.AgentRunOutputs.Remove(stale);
        }

        var attempt = new AgentRunOutput
        {
            OrganizationId = context.OrganizationId ?? string.Empty,
            BlockId = block.Id,
            Block = block,
            Round = round,
            RunExecutionId = runExecutionId,
            Output = output,
        };
        block.AgentRunOutputs.Add(attempt);

        return attempt;
    }

    /// <summary>
    /// Writes the block's <see cref="BlockResponse"/> from an attempt row. Fired per leg — the
    /// re-verify pass must read each member's current work — and again by the loop when
    /// best-attempt retention rejects a redo, restoring the retained attempt from its durable
    /// row (the settle choreography: per-leg settle plus a release-time re-settle).
    /// </summary>
    public static void SettleResponse(Block block, AgentRunOutput selected)
    {
        _ = new BlockResponse(block) { RawResponse = selected.Output };
    }

    /// <summary>
    /// A job-path block is a persisted UserData row the evidence can anchor to, so the shared
    /// layer commits collected trace rows against it before acknowledging. Editor test
    /// executions run against the template's block, which has no UserData row to satisfy the
    /// foreign key — and their evidence lives on the in-memory block instance for the session —
    /// so they declare no anchor and keep the collection-gated acknowledgement, rather than
    /// withholding completes that would pile abandoned packs onto the runtime with every test
    /// click. A job-path context missing its organization gets the same ephemeral treatment,
    /// loudly: an empty organization stamp would persist rows no tenant-scoped query can see,
    /// which is storage without evidence.
    /// </summary>
    public static AgentRunTraceAnchor? ResolveTraceAnchor(Block block, BlockExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(context.WorkflowExecutionId))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(context.OrganizationId))
        {
            context.Logger.LogWarning(
                "{block} Execution context carries no organization id; trace evidence for this run cannot be durably anchored",
                block);

            return null;
        }

        return new AgentRunTraceAnchor(context.OrganizationId, block.Id);
    }

    /// <summary>
    /// Where this leg's session bundle is filed, or null to take no custody of it. Only a leg
    /// inside a verification group is ever asked to redo its work, so only a group leg is worth
    /// the bytes — every other Agent run leaves its bundle on the runtime, where the
    /// acknowledgement releases it. Derived from the trace anchor rather than re-deriving its
    /// preconditions: a leg with nowhere to file trace evidence has nowhere to file a pointer
    /// row either, and one rule that moves is better than two that must be kept in step.
    /// </summary>
    public static AgentRunSessionBundleAnchor? ResolveSessionBundleAnchor(
        AgentRunTraceAnchor? traceAnchor,
        BlockExecutionContext context,
        int round)
    {
        if (!context.BelongsToVerificationGroup || traceAnchor?.BlockId is not string blockId)
        {
            return null;
        }

        return AgentRunSessionBundleAnchor.ForBlock(traceAnchor.OrganizationId, blockId, round);
    }

    /// <summary>
    /// In-memory only; durability rides the next pass's start-time flush or the usual
    /// post-block persist. A redo leg's mode stamps are set, never cleared — a settle that
    /// enriches a start-time row must not erase what the start wrote.
    /// </summary>
    public static void RecordSettledPass(
        List<AgentRunPass> runPasses,
        AgentRunAttempt attempt,
        int round,
        VerificationRedoLeg? redoLeg)
    {
        AgentRunPass? pass = runPasses.FirstOrDefault(
            recorded => recorded.ExecutionId == attempt.ExecutionId);
        if (pass is null)
        {
            pass = new AgentRunPass();
            runPasses.Add(pass);
        }

        pass.Round = round;
        pass.Pass = attempt.Pass;
        pass.ExecutionId = attempt.ExecutionId;
        pass.Status = attempt.Status.ToString();
        pass.FailureSubtype = attempt.FailureSubtype;
        pass.EstimatedCostUsd = attempt.EstimatedCostUsd;
        pass.Unacknowledged = attempt.Unacknowledged;
        StampRedoModes(pass, StampSource(redoLeg, attempt.RanContinuationFallback));
    }

    /// <summary>
    /// Stamps the settled run's smell signals on its ledger row: how long the runtime's own
    /// monitor says it took, and whether it came back under either bar of
    /// <see cref="SUSPICIOUSLY_FAST_SECONDS"/>. Only the settled run carries a monitor, so an
    /// earlier failed attempt's row keeps a null duration rather than a guessed one.
    /// </summary>
    public static void RecordRunSmell(List<AgentRunPass> runPasses, AgentRunConclusion conclusion)
    {
        AgentRunPass? pass = runPasses.FirstOrDefault(
            recorded => recorded.ExecutionId == conclusion.Monitor.ExecutionId);
        if (pass is null)
        {
            return;
        }

        AgentRunSmell smell = SmellOf(conclusion, pass.EstimatedCostUsd);

        pass.DurationSeconds = smell.Seconds;
        pass.SuspiciouslyFast = smell.SuspiciouslyFast;
        pass.SuspiciouslyCheap = smell.SuspiciouslyCheap;
    }

    /// <summary>
    /// The infrastructure signal a fan-out of agent legs can carry (D48): every leg of the round
    /// failed, instantly, having spent nothing. Model work is neither free nor immediate, so the
    /// pattern says the runtime could not reach a provider — expired credentials being the classic
    /// case — rather than that the blocks failed at their task. Operator-facing and nothing more:
    /// the caller neither fails the round nor buys it again, because a re-buy against a dead
    /// provider spends the same nothing twice. Null when the stage says nothing. Measured here
    /// rather than read off <see cref="AgentRunPass.SuspiciouslyFast"/> because a leg that never
    /// settled has no monitor to date it, which is exactly this case.
    /// </summary>
    public static string? InstantBurstReason(
        IReadOnlyList<Block> legBlocks,
        int round,
        TimeSpan stageWallClock)
    {
        if (stageWallClock.TotalSeconds >= INSTANT_BURST_SECONDS
            || legBlocks.Any(block => SettledPassFor(block, round) is not null))
        {
            return null;
        }

        List<AgentRunPass> roundPasses = [.. legBlocks.SelectMany(block => PassesForRound(block, round))];
        if (roundPasses.Count == 0 || roundPasses.Sum(static pass => pass.EstimatedCostUsd) > 0m)
        {
            return null;
        }

        return $"every leg of round {round} failed within {stageWallClock.TotalSeconds:0.#} seconds having "
            + $"spent nothing ({roundPasses.Count} run(s) across {legBlocks.Count} block(s)). Model work is "
            + "neither free nor instant, so this reads as the runtime being unable to reach a provider — its "
            + "credentials are the first thing to check — rather than as work the blocks failed at";
    }

    /// <summary>
    /// The two bars applied to one settled run, so every record of that run agrees about it: the
    /// ledger row and the verifier's judgment read the same seconds off the runtime's monitor and
    /// the same cost — the settled run's own, never the block's total across earlier machinery
    /// passes, because a dear failed pass says nothing about whether the answer that settled came
    /// back too cheap to be real.
    /// </summary>
    public static AgentRunSmell SmellOf(AgentRunConclusion conclusion, decimal settledCostUsd)
    {
        double seconds = (conclusion.Monitor.UpdatedAt - conclusion.Monitor.CreatedAt).TotalSeconds;

        return new AgentRunSmell(
            seconds,
            seconds < SUSPICIOUSLY_FAST_SECONDS,
            settledCostUsd < SUSPICIOUSLY_CHEAP_USD);
    }

    private static void StampRedoModes(AgentRunPass pass, VerificationRedoLeg? redoLeg)
    {
        if (redoLeg is null)
        {
            return;
        }

        pass.ConfiguredRedoMode = redoLeg.ConfiguredRedoMode;
        pass.ActualRedoMode = redoLeg.ActualRedoMode;
    }

    /// <summary>
    /// The leg whose modes a run's row records. A run bought under the executor's continuation
    /// fallback ran scratch whatever the leg planned, so its rows carry the override — the
    /// configured mode stays what the engineer chose, which is what makes the override
    /// readable as one.
    /// </summary>
    private static VerificationRedoLeg? StampSource(VerificationRedoLeg? redoLeg, bool ranContinuationFallback)
    {
        if (redoLeg is null || !ranContinuationFallback)
        {
            return redoLeg;
        }

        return redoLeg with { ActualRedoMode = VerificationRedoLeg.SCRATCH_MODE };
    }

    /// <summary>
    /// Merges the settled attempts into the ledger and records the spend, so a failed block
    /// still says what it cost and which runs to look at. The bill is the run's tokens, read out
    /// of the collected trace pack and priced from the pricing table (<see cref="AgentRunBilling"/>);
    /// the runtime's own figure stands in only for what no pricing row covers, and a block whose
    /// trace was withheld says so on the spend record rather than reporting zero. Awaited by the
    /// block: the token usage service records through the block's own service scope, which is
    /// gone once the block returns. Never throws.
    /// </summary>
    public static async Task RecordRunSpendAsync(
        Block block,
        BlockExecutionContext context,
        string? model,
        string blockDescription,
        List<AgentRunPass> runPasses,
        IReadOnlyList<AgentRunAttempt> attempts,
        IReadOnlyList<AgentRunTraceFile>? traceFiles,
        int round,
        VerificationRedoLeg? redoLeg = null)
    {
        if (attempts.Count == 0)
        {
            return;
        }

        foreach (AgentRunAttempt attempt in attempts)
        {
            RecordSettledPass(runPasses, attempt, round, redoLeg);
        }

        var bill = new AgentRunBill(
            new LlmUsageRecord
            {
                OrganizationId = context.OrganizationId ?? string.Empty,
                UserId = context.UserId,
                WorkflowTypeId = context.WorkflowTypeId,
                WorkflowExecutionId = context.WorkflowExecutionId,
                SectionId = context.SectionId,
                BlockId = block.Id,
                UsageType = UsageType.LlmAgentRun,
                ExecutionContext = context.ExecutionContext,
                BlockType = block.Type,
                Provider = AGENT_RUNTIME_PROVIDER,
                Family = ModelIdNormalizer.GetFamily(model ?? string.Empty),
                ModelId = model ?? string.Empty,
                Components = [],
                Description = blockDescription,
            },
            attempts.Count,
            attempts.TotalCostUsd(),
            AgentRunTraceTokens.Read(traceFiles),
            model ?? AgentRunTraceTokens.ModelPinOf(traceFiles) ?? string.Empty,
            round,
            UncoveredRuntimeCostUsd: AgentRunTraceTokens.UncoveredCostUsd(attempts, traceFiles));

        try
        {
            _ = await AgentRunBilling.RecordAsync(bill, context.TokenUsageService, CancellationToken.None);
        }
        catch (Exception exception)
        {
            context.Logger.LogError(exception, "Recording the spend of agent block {BlockId} failed", bill.Spend.BlockId);
        }
    }

    /// <summary>
    /// What this block bought and what it is worth looking at next — the one sentence every
    /// failure path ends with, so a settled failure and an exhausted machinery failure say it
    /// the same way.
    /// </summary>
    public static string SpendSummary(IReadOnlyList<AgentRunAttempt> attempts)
    {
        if (attempts.Count == 0)
        {
            return "No run was started, so nothing was spent.";
        }

        AgentRunAttempt last = attempts[^1];

        return $"Run '{last.ExecutionId}' was pass {last.Pass} of this block; "
            + $"${attempts.TotalCostUsd().ToString(COST_FORMAT, CultureInfo.InvariantCulture)} was spent across {attempts.Count} run(s).";
    }

    public static InvalidOperationException Fail(Block block, BlockExecutionContext context, string message)
    {
        context.Logger.LogError("{block} {Message}", block, message);
        return new InvalidOperationException(message);
    }
}
