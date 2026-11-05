using ErrorOr;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Services.AgentRuntime;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using System.Globalization;
using System.Text.Json.Nodes;

namespace IniaAi.Application.Common.Workflows;

public static class AgentBlockGuard
{
    private const string SCRATCH_NAME = "agent-guard-scratch";
    private const int MILLISECONDS_PER_MINUTE = 60_000;
    private const int BYTES_PER_MIB = 1024 * 1024;

    private const int MAX_REPORTED_SCHEMA_PROBLEMS = 8;

    public enum AgentViolationKind
    {
        RuntimeDisabled,
        InvalidProperties,
        SkillShape,
        SkillNameShape,
        SkillNameCollision,
        SkillDescriptionLength,
        SkillAssetShape,
        SkillAssetNameShape,
        SkillAssetNameCollision,
        SkillAssetCount,
        SkillInvocationUnknown,
        SkillInvocationBareForm,
        /// <summary>No longer raised: a block with no model of its own runs its tier's model.</summary>
        ModelMissing,
        ModelNotAllowed,
        EffortNotAllowed,
        FuseValue,
        FuseAboveCeiling,
        LoopScope,
        /// <summary>No longer raised: a tag into a loop or a parameterized section saves with a warning (<see cref="AgentTemplateGrammar.SaveWarnings"/>).</summary>
        LoopProducer,
        OutputSchemaInvalid,
        VerificationMembersMissing,
        VerificationMemberUnresolved,
        VerificationMemberNotAgent,
        VerificationMemberDuplicated,
        VerificationMemberInAnotherGroup,
        VerificationGroupSplitAcrossSegments,
        VerificationGroupCycle,
        VerificationMemberReadsMember,
        VerificationMemberRunsAlone,
        VerificationMemberProducesDocuments,
        VerificationMemberUpdatesCounterparty,
        VerificationCriteriaMissing,
        VerificationCriterionIdBlank,
        VerificationCriterionIdTooLong,
        VerificationCriterionIdDuplicate,
        VerificationCriterionTargetUnknown,
        VerificationPassCapOutOfRange,
        VerificationModelBelowTier,
        VerificationWallClockOverJanitor,
        VerificationGroupInLoopSection,
        VerificationWallClockSumOverJanitor,
        VerificationMemberInParameterizedSection,
        TierModelNotAllowed,
        OutputFilesInvalid,
    }

    public sealed record AgentBlockViolation(AgentViolationKind Kind, string Message);

    /// <summary>
    /// Definition-scoped validation for shared components: a block or section validated here
    /// may reference members and producers that live only in the consuming workflows, so
    /// member resolution is not required — the fan-out re-runs the full graph validation per
    /// consuming workflow, where it is.
    /// </summary>
    public static List<AgentBlockViolation> ValidateDefinition(
        Block? blockDefinition,
        Section? sectionDefinition,
        AgentRuntimeSettings agentRuntime,
        AgentRunLimits? runLimits = null,
        WorkflowExecutionJanitorSettings? janitor = null,
        int? maxBlockParallelism = null,
        AgentModelTiers? modelTiers = null)
    {
        if (sectionDefinition is null && blockDefinition?.Type.IsAgentFamily() is not true)
        {
            return [];
        }

        return ValidateGraphCore(
            ScratchWorkflowFor(blockDefinition, sectionDefinition),
            agentRuntime,
            runLimits,
            janitor,
            maxBlockParallelism,
            requireMemberResolution: false,
            modelTiers ?? AgentModelTiers.For(agentRuntime));
    }

    private static Workflow ScratchWorkflowFor(Block? blockDefinition, Section? sectionDefinition)
    {
        var scratch = new Workflow(SCRATCH_NAME);
        if (sectionDefinition is not null)
        {
            scratch.Sections.Add(sectionDefinition);
            return scratch;
        }

        var scratchSection = new Section(SCRATCH_NAME, scratch);
        scratchSection.Blocks.Add(blockDefinition!);
        return scratch;
    }

    /// <param name="runLimits">
    /// The enforceable snapshot: fresh enough to refuse an over-ceiling fuse. Null skips the
    /// ceiling check, and the save carries a warning instead of refusing the author. Nothing
    /// else reads it — no gate here prices a fuse, so no save outcome depends on the cache.
    /// </param>
    /// <param name="modelTiers">
    /// The saving organization's agent model tiers, which a block with no model of its own runs.
    /// Null uses the deployment's tiers.
    /// </param>
    public static List<AgentBlockViolation> ValidateGraph(
        Workflow workflow,
        AgentRuntimeSettings agentRuntime,
        AgentRunLimits? runLimits = null,
        WorkflowExecutionJanitorSettings? janitor = null,
        int? maxBlockParallelism = null,
        AgentModelTiers? modelTiers = null)
    {
        return ValidateGraphCore(
            workflow, agentRuntime, runLimits, janitor, maxBlockParallelism, requireMemberResolution: true,
            modelTiers ?? AgentModelTiers.For(agentRuntime));
    }

    private static List<AgentBlockViolation> ValidateGraphCore(
        Workflow workflow,
        AgentRuntimeSettings agentRuntime,
        AgentRunLimits? runLimits,
        WorkflowExecutionJanitorSettings? janitor,
        int? maxBlockParallelism,
        bool requireMemberResolution,
        AgentModelTiers modelTiers)
    {
        List<Block> allBlocks = workflow.GetAllBlocks();
        var agentBlocks = new List<Block>();
        var verificationBlocks = new List<Block>();
        foreach (Block block in allBlocks)
        {
            if (block.Type == BlockTypes.Agent)
            {
                agentBlocks.Add(block);
            }
            else if (block.Type == BlockTypes.Verification)
            {
                verificationBlocks.Add(block);
            }
        }

        if (!agentRuntime.Enabled)
        {
            List<Block> familyBlocks = [.. agentBlocks, .. verificationBlocks];
            return familyBlocks.Count == 0 ? [] : [RuntimeDisabledViolation(familyBlocks)];
        }

        List<AgentBlockViolation> violations = ValidateAgentBlocks(agentBlocks, modelTiers, runLimits);
        if (agentBlocks.Count == 0 && verificationBlocks.Count == 0)
        {
            return violations;
        }

        foreach (AgentTemplateGrammar.LoopScopeViolation violation
            in AgentTemplateGrammar.FindLoopScopeViolations(workflow))
        {
            violations.Add(new AgentBlockViolation(
                AgentViolationKind.LoopScope,
                $"{BlockKindLabel(violation.BlockType)} '{violation.BlockTag}' sits inside looped or parameterized section "
                + $"'{violation.SectionTitle}'. A verifier names its members by tag and settles one verdict for "
                + "them, and expansion would copy it once per iteration. Move the verification block to a plain section."));
        }

        return violations;
    }

    internal static string BlockKindLabel(BlockTypes type)
    {
        return type == BlockTypes.Verification ? "Verification block" : "Agent block";
    }

    private static AgentBlockViolation RuntimeDisabledViolation(List<Block> familyBlocks)
    {
        string tags = string.Join(", ", familyBlocks.Select(b => b.ReplacementTag));
        return new AgentBlockViolation(
            AgentViolationKind.RuntimeDisabled,
            $"The workflow contains agent-family blocks ({tags}), but the agent runtime is disabled on this "
            + $"environment ({AgentRuntimeSettings.SectionName}:{nameof(AgentRuntimeSettings.Enabled)}). "
            + "Agent-family blocks can only be authored once every host runs a build that supports them and the flag is on.");
    }

    private static List<AgentBlockViolation> ValidateAgentBlocks(
        List<Block> agentBlocks,
        AgentModelTiers modelTiers,
        AgentRunLimits? runLimits)
    {
        var violations = new List<AgentBlockViolation>();

        foreach (Block block in agentBlocks)
        {
            if (block.Properties is not AgentProperties props)
            {
                violations.Add(new AgentBlockViolation(
                    AgentViolationKind.InvalidProperties,
                    $"Agent block '{block.ReplacementTag}' has properties of type "
                    + $"'{block.Properties.GetType().Name}' instead of {nameof(AgentProperties)}."));
                continue;
            }

            ValidateSkillShapes(block, props, requireAtLeastOne: true, violations);
            ValidateSkillNames(block, props, violations);
            ValidateSkillAssets(block, props, violations);
            ValidateSkillInvocations(block, props, violations);
            ValidateOutputSchema(block, props, violations);
            ValidateOutputFiles(block, props, violations);
            ValidateModelAndEffort(block, props, modelTiers, violations);
            ValidateDeclaredFuses(block, props, runLimits, violations);
        }

        return violations;
    }

    /// <summary>
    /// The model the block runs (its own, else its tier's) must be a form of one in the runtime's
    /// pinned allowlist, and a set effort from its vocabulary; the runtime's own 400 on start is
    /// the backstop when the platform mirror drifts. Answers whether that model is allowed, so a
    /// caller can run stricter tier rules only on a model that passed.
    /// </summary>
    internal static bool ValidateModelAndEffort(
        Block block,
        IAgentRunProperties props,
        AgentModelTiers modelTiers,
        List<AgentBlockViolation> violations)
    {
        string blockKind = BlockKindLabel(block.Type);
        bool modelAllowed = true;

        if (DescribeModelViolation(block, props, modelTiers) is { } modelViolation)
        {
            violations.Add(modelViolation);
            modelAllowed = false;
        }

        if (!string.IsNullOrWhiteSpace(props.Effort)
            && !AgentRuntimeSettings.EffortLevels.Contains(props.Effort, StringComparer.Ordinal))
        {
            violations.Add(new AgentBlockViolation(
                AgentViolationKind.EffortNotAllowed,
                $"{blockKind} '{block.ReplacementTag}' selects effort '{props.Effort}', which is not one of "
                + $"({string.Join(", ", AgentRuntimeSettings.EffortLevels)}). Leave it unset to omit effort "
                + "from the run and use the runtime's default."));
        }

        return modelAllowed;
    }

    /// <summary>
    /// Declared fuses are checked for shape always, and against the runtime's ceilings whenever
    /// <paramref name="runLimits"/> is known — refusing the save is the cheapest failure, before
    /// any document spend. The ceilings are the runtime's own advertisement
    /// (<c>GET /v1/limits</c>, cached), never a mirrored constant, so they cannot drift; when
    /// they are unknown (null — runtime unreachable or the cache stale) the ceiling check is
    /// skipped and the caller warns instead, because the runtime's 400 at run start still
    /// refuses an over-ceiling fuse before any spend. Never clamp silently: a clamped value
    /// hides a misconfiguration behind a run that quietly did something else.
    /// </summary>
    internal static void ValidateDeclaredFuses(
        Block block,
        IAgentRunProperties props,
        AgentRunLimits? runLimits,
        List<AgentBlockViolation> violations)
    {
        string blockKind = BlockKindLabel(block.Type);
        (string Name, bool IsAtOrBelowZero)[] fuses =
        [
            (nameof(IAgentRunProperties.DeadlineMinutes), props.DeadlineMinutes is <= 0),
            (nameof(IAgentRunProperties.MaxBudgetUsd), props.MaxBudgetUsd is <= 0m),
            (nameof(IAgentRunProperties.MaxTurns), props.MaxTurns is <= 0),
            (nameof(IAgentRunProperties.MaxRetainedOutputMib), props.MaxRetainedOutputMib is <= 0),
        ];

        bool anyAtOrBelowZero = false;
        foreach ((string name, bool isAtOrBelowZero) in fuses)
        {
            if (isAtOrBelowZero)
            {
                anyAtOrBelowZero = true;
                violations.Add(new AgentBlockViolation(
                    AgentViolationKind.FuseValue,
                    $"{blockKind} '{block.ReplacementTag}' sets {name} at or below zero. Give the fuse a "
                    + "positive value, or clear it to leave the runtime's own default in force."));
            }
        }

        if (runLimits is null || anyAtOrBelowZero)
        {
            return;
        }

        if (props.DeadlineMinutes is int declaredDeadline
            && declaredDeadline * (decimal)MILLISECONDS_PER_MINUTE > runLimits.DeadlineMs.Ceiling)
        {
            AddCeilingViolation(
                violations, block, nameof(IAgentRunProperties.DeadlineMinutes),
                FormatFuseNumber(declaredDeadline),
                $"{FormatFuseNumber(runLimits.DeadlineMs.Ceiling / MILLISECONDS_PER_MINUTE)} minutes");
        }

        if (props.MaxBudgetUsd is decimal declaredBudget && declaredBudget > runLimits.MaxBudgetUsd.Ceiling)
        {
            AddCeilingViolation(
                violations, block, nameof(IAgentRunProperties.MaxBudgetUsd),
                FormatFuseNumber(declaredBudget),
                $"{runLimits.MaxBudgetUsd.Ceiling.ToString("0.00##", CultureInfo.InvariantCulture)} USD");
        }

        if (props.MaxTurns is int declaredTurns && declaredTurns > runLimits.MaxTurns.Ceiling)
        {
            AddCeilingViolation(
                violations, block, nameof(IAgentRunProperties.MaxTurns),
                FormatFuseNumber(declaredTurns),
                $"{FormatFuseNumber(runLimits.MaxTurns.Ceiling)} turns");
        }

        if (props.MaxRetainedOutputMib is int declaredRetained
            && declaredRetained * (decimal)BYTES_PER_MIB > runLimits.MaxRetainedOutputBytes.Ceiling)
        {
            AddCeilingViolation(
                violations, block, nameof(IAgentRunProperties.MaxRetainedOutputMib),
                FormatFuseNumber(declaredRetained),
                $"{FormatFuseNumber(runLimits.MaxRetainedOutputBytes.Ceiling / BYTES_PER_MIB)} MiB");
        }
    }

    private static void AddCeilingViolation(
        List<AgentBlockViolation> violations,
        Block block,
        string fuseName,
        string declared,
        string ceiling)
    {
        violations.Add(new AgentBlockViolation(
            AgentViolationKind.FuseAboveCeiling,
            $"{BlockKindLabel(block.Type)} '{block.ReplacementTag}' sets {fuseName} to {declared}, above this runtime's "
            + $"ceiling of {ceiling}. Lower it to at most the ceiling, or clear it to use the "
            + "runtime's own default."));
    }

    internal static void ValidateSkillShapes(
        Block block,
        IAgentRunProperties props,
        bool requireAtLeastOne,
        List<AgentBlockViolation> violations)
    {
        List<AgentWorkerSkill> skills = props.WorkerSkills;
        bool anyMalformed = skills.Any(static skill =>
            string.IsNullOrWhiteSpace(skill.Name)
            || string.IsNullOrWhiteSpace(skill.Description)
            || string.IsNullOrWhiteSpace(skill.Content));
        if ((requireAtLeastOne && skills.Count == 0) || anyMalformed)
        {
            string requirement = requireAtLeastOne
                ? "must define at least one worker skill, and every skill"
                : "has a malformed worker skill: every skill";
            violations.Add(new AgentBlockViolation(
                AgentViolationKind.SkillShape,
                $"{BlockKindLabel(block.Type)} '{block.ReplacementTag}' {requirement} "
                + "needs a non-blank name, description, and content. The description is what tells "
                + "the model the skill exists and when to use it."));
        }
    }

    private static string FormatFuseNumber(decimal value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The warning a save carries when it was accepted without the ceiling check: the runtime
    /// was unreachable (or the limits cache stale), and refusing the author's save over an
    /// outage would be worse than saving unchecked — the runtime's 400 at run start still
    /// refuses an over-ceiling fuse before any spend.
    /// </summary>
    public const string FUSES_UNCHECKED_WARNING =
        "The agent runtime could not be reached to check the agent blocks' fuse values against its "
        + "ceilings, so this save was accepted unchecked. A fuse above a runtime ceiling will be "
        + "refused when a run starts, before any spend.";

    public static bool DeclaresAnyFuse(Workflow workflow)
    {
        return workflow.GetAllBlocks().Any(static block =>
            block.Properties is IAgentRunProperties props
            && (props.DeadlineMinutes is not null
                || props.MaxBudgetUsd is not null
                || props.MaxTurns is not null
                || props.MaxRetainedOutputMib is not null));
    }

    /// <summary>
    /// The one model rule both gates share: the deployment's allowlist has a form of the model the
    /// block runs, its own or else its tier's (<see cref="AgentModelTiers"/>). Any provider's id of
    /// an allowlisted model passes (a Bedrock inference-profile id against a Foundry list, or the
    /// reverse), because a run resolves it to the deployment's own form
    /// (<see cref="AgentRuntimeModels"/>). The list in the message is provider-neutral, the ids an
    /// author should store.
    /// </summary>
    private static AgentBlockViolation? DescribeModelViolation(
        Block block,
        IAgentRunProperties props,
        AgentModelTiers modelTiers)
    {
        AgentRuntimeSettings agentRuntime = modelTiers.Runtime;
        string blockKind = BlockKindLabel(block.Type);
        string allowedModels = string.Join(", ", AgentRuntimeModels.NeutralAllowedModels(agentRuntime));

        if (AgentModelTiers.UsesTier(props))
        {
            string tierModel = modelTiers.TierModelFor(props.ShouldUseAdvancedModel);
            if (AgentRuntimeModels.RuntimeIdFor(agentRuntime, tierModel) is not null)
            {
                return null;
            }

            string tierName = props.ShouldUseAdvancedModel ? ADVANCED_TIER : BASIC_TIER;
            string settingName = props.ShouldUseAdvancedModel
                ? nameof(AgentRuntimeSettings.AdvancedModel)
                : nameof(AgentRuntimeSettings.BasicModel);
            return new AgentBlockViolation(
                AgentViolationKind.TierModelNotAllowed,
                $"{blockKind} '{block.ReplacementTag}' names no model of its own, so it runs the {tierName} agent "
                + $"model '{tierModel}' ({AgentRuntimeSettings.SectionName}:{settingName}, or the organization's "
                + $"override), which is not in the allowed model list ({allowedModels}). Select a specific "
                + "model, switch the tier, or configure the tier to an allowed model.");
        }

        if (AgentRuntimeModels.RuntimeIdFor(agentRuntime, props.Model) is null)
        {
            return new AgentBlockViolation(
                AgentViolationKind.ModelNotAllowed,
                $"{blockKind} '{block.ReplacementTag}' selects model '{props.Model}', which is not in the "
                + $"allowed model list ({allowedModels}).");
        }

        return null;
    }

    private const string ADVANCED_TIER = "advanced";
    private const string BASIC_TIER = "basic";

    private const string SUBMIT_REFUSED_BEFORE_SPEND =
        "The submission was refused before an execution was created, so nothing was spent";

    private const string BLOCK_RUN_REFUSED_BEFORE_SPEND =
        "The block run was refused before its documents were processed, so nothing was spent";

    /// <summary>
    /// The submit-time gate: a workflow containing agent-family blocks is refused before any
    /// execution row exists when the runtime is switched off, or when a block's model is not in
    /// the runtime's allowlist in any provider's form (a template saved against an older or
    /// another deployment's list), instead of failing mid-execution after the upstream blocks have already
    /// spent. Null means the submission may proceed.
    /// </summary>
    /// <param name="modelTiers">The submitting organization's agent model tiers; null uses the deployment's.</param>
    public static AgentBlockViolation? DescribeSubmitRefusal(
        Workflow workflow,
        AgentRuntimeSettings agentRuntime,
        AgentModelTiers? modelTiers = null)
    {
        List<Block> familyBlocks = [.. workflow.GetAllBlocks().Where(static block => block.Type.IsAgentFamily())];
        return familyBlocks.Count == 0
            ? null
            : DescribePreSpendRefusal(
                familyBlocks, modelTiers ?? AgentModelTiers.For(agentRuntime), SUBMIT_REFUSED_BEFORE_SPEND);
    }

    /// <summary>
    /// The editor's single-block run gate, the same rule scoped to the one block being run: the
    /// editor processes the block's documents before it executes, and a file upload can fill the
    /// editor without a save, so neither the save-time gate nor the submit-time gate bounds what
    /// reaches it. Null means the block may run; a non-agent block always may.
    /// </summary>
    /// <param name="modelTiers">The running organization's agent model tiers; null uses the deployment's.</param>
    public static AgentBlockViolation? DescribeBlockRunRefusal(
        Block block,
        AgentRuntimeSettings agentRuntime,
        AgentModelTiers? modelTiers = null)
    {
        if (!block.Type.IsAgentFamily())
        {
            return null;
        }

        // A loop body expands only when its section runs, so a single-block run would send the
        // template's unexpanded tags to a paid run.
        if (block.Section?.GetLoopBodyBlockIds().Contains(block.Id) is true)
        {
            return new AgentBlockViolation(
                AgentViolationKind.LoopScope,
                $"{BlockKindLabel(block.Type)} '{block.ReplacementTag}' sits inside a loop, and a loop "
                + $"expands only when its section runs. {BLOCK_RUN_REFUSED_BEFORE_SPEND}. Run the section instead.");
        }

        return DescribePreSpendRefusal(
            [block], modelTiers ?? AgentModelTiers.For(agentRuntime), BLOCK_RUN_REFUSED_BEFORE_SPEND);
    }

    private static AgentBlockViolation? DescribePreSpendRefusal(
        List<Block> familyBlocks,
        AgentModelTiers modelTiers,
        string refusedBeforeSpend)
    {
        if (!modelTiers.Runtime.Enabled)
        {
            string tags = string.Join(", ", familyBlocks.Select(static block => block.ReplacementTag));
            return new AgentBlockViolation(
                AgentViolationKind.RuntimeDisabled,
                $"This workflow contains agent-family blocks ({tags}), but the agent "
                + $"runtime is disabled on this environment ({AgentRuntimeSettings.SectionName}:"
                + $"{nameof(AgentRuntimeSettings.Enabled)}). {refusedBeforeSpend} — enable the "
                + "runtime or remove the agent-family blocks.");
        }

        foreach (Block block in familyBlocks)
        {
            if (block.Properties is IAgentRunProperties props
                && DescribeModelViolation(block, props, modelTiers) is { } modelViolation)
            {
                return modelViolation with
                {
                    Message = $"{modelViolation.Message} {refusedBeforeSpend} — select a model from "
                        + "the allowed model list and save the template again.",
                };
            }
        }

        return null;
    }

    /// <summary>
    /// The runtime's SDK compiles the output schema as strict draft-07 and refuses anything
    /// outside that dialect, so the same rules run at save — a schema the SDK would reject must
    /// never survive a save only to burn a paid run (<see cref="AgentOutputSchemaDraft07"/>
    /// holds the measured dialect contract).
    /// </summary>
    private static void ValidateOutputSchema(Block block, AgentProperties props, List<AgentBlockViolation> violations)
    {
        ErrorOr<JsonObject?> parsed = AgentOutputSchemas.Parse(props.OutputSchema);
        if (parsed.IsError)
        {
            violations.Add(new AgentBlockViolation(
                AgentViolationKind.OutputSchemaInvalid,
                $"Agent block '{block.ReplacementTag}' has an invalid output schema: {parsed.FirstError.Description}."));
            return;
        }

        if (parsed.Value is not JsonObject schema)
        {
            return;
        }

        List<AgentOutputSchemaDraft07.SchemaViolation> schemaProblems = AgentOutputSchemaDraft07.Validate(schema);
        if (schemaProblems.Count == 0)
        {
            return;
        }

        string detail = string.Join(
            "; ",
            schemaProblems
                .Take(MAX_REPORTED_SCHEMA_PROBLEMS)
                .Select(static problem => $"{problem.Path}: {problem.Message}"));
        string overflow = schemaProblems.Count > MAX_REPORTED_SCHEMA_PROBLEMS
            ? $" (+{schemaProblems.Count - MAX_REPORTED_SCHEMA_PROBLEMS} more)"
            : string.Empty;
        violations.Add(new AgentBlockViolation(
            AgentViolationKind.OutputSchemaInvalid,
            $"Agent block '{block.ReplacementTag}' has an output schema the agent runtime's SDK would reject "
            + $"(it enforces JSON Schema draft-07): {detail}{overflow}."));
    }

    private static void ValidateOutputFiles(Block block, AgentProperties props, List<AgentBlockViolation> violations)
    {
        if (AgentRunOutputFiles.DescribeFirstPathProblem(props.OutputFiles) is string problem)
        {
            violations.Add(new AgentBlockViolation(
                AgentViolationKind.OutputFilesInvalid,
                $"Agent block '{block.ReplacementTag}': {problem}."));
        }
    }

    internal static void ValidateSkillNames(
        Block block,
        IAgentRunProperties props,
        List<AgentBlockViolation> violations)
    {
        string blockKind = BlockKindLabel(block.Type);
        var identities = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (AgentWorkerSkill skill in props.WorkerSkills)
        {
            string? identity = AgentSkillNames.Normalize(skill.Name);

            if (!string.IsNullOrWhiteSpace(skill.Name) && !string.Equals(identity, skill.Name, StringComparison.Ordinal))
            {
                violations.Add(new AgentBlockViolation(
                    AgentViolationKind.SkillNameShape,
                    $"{blockKind} '{block.ReplacementTag}' names a skill '{skill.Name}', which is not a skill "
                    + $"identity. Use lower-case words joined by single hyphens, at most {AgentSkillNames.MAX_LENGTH} "
                    + $"characters and no file extension — '{identity ?? "income-statement"}', for instance."));
            }

            if (identity is not null)
            {
                if (identities.TryGetValue(identity, out string? priorName))
                {
                    violations.Add(new AgentBlockViolation(
                        AgentViolationKind.SkillNameCollision,
                        $"{blockKind} '{block.ReplacementTag}' has skills '{priorName}' and '{skill.Name}', which are "
                        + $"the same skill '{identity}'. Give them distinct names."));
                }
                else
                {
                    identities.Add(identity, skill.Name);
                }
            }

            if (skill.Description.Length > AgentSkillNames.MAX_DESCRIPTION_LENGTH)
            {
                violations.Add(new AgentBlockViolation(
                    AgentViolationKind.SkillDescriptionLength,
                    $"{blockKind} '{block.ReplacementTag}' gives skill '{skill.Name}' a description of "
                    + $"{skill.Description.Length} characters, over the runtime's "
                    + $"{AgentSkillNames.MAX_DESCRIPTION_LENGTH} character cap."));
            }
        }
    }

    /// <summary>
    /// Mirrors the runtime's skill-asset admission rules (<see cref="AgentSkillAssets"/> —
    /// per-skill count cap, staged-input name shape, per-skill uniqueness), refusing the save
    /// before a run can be refused with a 400. Blank content is a platform rule on top: an
    /// empty file is admissible to the runtime but is an authoring mistake here, exactly as a
    /// blank skill body is.
    /// </summary>
    internal static void ValidateSkillAssets(
        Block block,
        IAgentRunProperties props,
        List<AgentBlockViolation> violations)
    {
        string blockKind = BlockKindLabel(block.Type);

        foreach (AgentWorkerSkill skill in props.WorkerSkills)
        {
            List<AgentWorkerSkillAsset> assets = skill.Assets ?? [];
            if (AgentSkillAssets.DescribeFirstProblem(
                skill.Name,
                assets.Select(static asset => (asset.Name, asset.Content))) is { } problem)
            {
                violations.Add(new AgentBlockViolation(
                    problem.Kind switch
                    {
                        AgentSkillAssets.ProblemKind.CountOverCap => AgentViolationKind.SkillAssetCount,
                        AgentSkillAssets.ProblemKind.NameCollision => AgentViolationKind.SkillAssetNameCollision,
                        _ => AgentViolationKind.SkillAssetNameShape,
                    },
                    $"{blockKind} '{block.ReplacementTag}': {problem.Message}"));
            }

            foreach (AgentWorkerSkillAsset asset in assets.Where(
                static asset => string.IsNullOrWhiteSpace(asset.Content)))
            {
                violations.Add(new AgentBlockViolation(
                    AgentViolationKind.SkillAssetShape,
                    $"{blockKind} '{block.ReplacementTag}' gives skill '{skill.Name}' an asset "
                    + $"'{asset.Name}' with no content. Every asset needs the file's actual text."));
            }
        }
    }

    private const string PROMPT_FIELD = "prompt";
    private const string SYSTEM_PROMPT_FIELD = "system prompt";

    /// <summary>
    /// A skill is invoked from a prompt as <c>/skills:&lt;identity&gt;</c>, and the runtime's
    /// CLI resolves it host-side against the block's own registered skills — a name that
    /// misses never reaches the model, it just burns the run (a measured run settled as
    /// an empty success). The save is the cheapest place to refuse, so every prefixed
    /// invocation must resolve against this block's skill identities exactly, and a bare
    /// <c>/name</c> that maps onto one of them is refused toward the prefixed form rather than
    /// guessed at. <see cref="AgentSkillInvocations"/> owns what counts as an invocation;
    /// ordinary prose and paths are never flagged. Two shapes are deliberately skipped rather
    /// than refused, because they cannot be judged at save time and an unjudgeable name must
    /// never block a save: a name carrying a <c>{{tag}}</c> substitution (its value exists
    /// only at execution) and a placeholder-shaped name documenting the convention — both
    /// classes fall to the runtime's zero-turn terminal guard when they go wrong.
    /// </summary>
    internal static void ValidateSkillInvocations(
        Block block,
        IAgentRunProperties props,
        List<AgentBlockViolation> violations)
    {
        List<string> identities = [.. props.WorkerSkills
            .Select(static skill => AgentSkillNames.Normalize(skill.Name))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)];
        var identitySet = new HashSet<string>(identities, StringComparer.Ordinal);

        ReportInvocationProblems(block, props.PromptTemplate, PROMPT_FIELD, identities, identitySet, violations);
        ReportInvocationProblems(
            block, props.SystemPromptTemplate, SYSTEM_PROMPT_FIELD, identities, identitySet, violations);
    }

    private static void ReportInvocationProblems(
        Block block,
        string text,
        string fieldName,
        List<string> identities,
        IReadOnlySet<string> identitySet,
        List<AgentBlockViolation> violations)
    {
        string blockKind = BlockKindLabel(block.Type);
        foreach (AgentSkillInvocations.InvocationProblem problem
            in AgentSkillInvocations.FindProblems(text, identitySet))
        {
            violations.Add(problem.Kind switch
            {
                AgentSkillInvocations.ProblemKind.MissingName => new AgentBlockViolation(
                    AgentViolationKind.SkillInvocationUnknown,
                    $"{blockKind} '{block.ReplacementTag}' invokes "
                    + $"'{AgentSkillInvocations.INVOCATION_PREFIX}' with no skill name in its {fieldName}. "
                    + $"Name one of the block's skills ({InvocationList(identities)}), or write "
                    + $"'{AgentSkillInvocations.INVOCATION_PREFIX}<name>' to describe the form without "
                    + "invoking it."),
                AgentSkillInvocations.ProblemKind.BareForm => new AgentBlockViolation(
                    AgentViolationKind.SkillInvocationBareForm,
                    $"{blockKind} '{block.ReplacementTag}' invokes skill '{problem.ResolvableIdentity}' as "
                    + $"'{problem.Token}' in its {fieldName}. Skill invocations use the prefixed form — write "
                    + $"'{AgentSkillInvocations.INVOCATION_PREFIX}{problem.ResolvableIdentity}'."),
                _ => new AgentBlockViolation(
                    AgentViolationKind.SkillInvocationUnknown,
                    $"{blockKind} '{block.ReplacementTag}' invokes '{problem.Token}' in its {fieldName}, "
                    + "which no skill of this block resolves to at run time."
                    + (problem.ResolvableIdentity is string resolvable
                        ? $" Did you mean '{AgentSkillInvocations.INVOCATION_PREFIX}{resolvable}'?"
                        : $" The block's skills are ({InvocationList(identities)}).")),
            });
        }
    }

    private static string InvocationList(List<string> identities)
    {
        return identities.Count == 0
            ? "none defined yet"
            : string.Join(", ", identities.Select(static identity =>
                $"{AgentSkillInvocations.INVOCATION_PREFIX}{identity}"));
    }
}
