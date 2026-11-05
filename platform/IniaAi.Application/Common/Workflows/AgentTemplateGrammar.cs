using System.Globalization;
using IniaAi.Common;
using IniaAi.Common.Constants;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Application.Common.Workflows;

/// <summary>
/// Save-time checks over the agent-template grammar, shared by both save surfaces — the editor
/// sends <c>SaveWorkflowCommand</c> directly (bypassing API structural validation), so these
/// checks hang off the command pipeline, not the API validator. Unknown tags are a warning the
/// save handler returns, never a refusal; loop-scope violations are blocking rules on the command
/// validator and, through <see cref="AgentBlockGuard"/>, on <c>WorkflowSavePipeline</c> itself,
/// so shared-component fan-out, materialize, transfer import, and every other pipeline caller
/// enforces them too; rename refusal runs in the signature synchronizer. This is the seam the
/// validation-loop and verification-group batches extend.
/// </summary>
public static class AgentTemplateGrammar
{
    private const string UNKNOWN_TAGS_WARNING =
        "No block, variable or input supplies these agent tags, so they will read as no data when the workflow runs: {0}. Check the tag names.";

    public sealed record UnknownTagReference(string BlockTag, AgentTemplateLocation Location, string Tag);

    public sealed record LoopScopeViolation(string BlockTag, string SectionTitle, BlockTypes BlockType);

    /// <summary>A reference, from a skill or a prompt field, to a producer inside a looped or parameterized section.</summary>
    public sealed record LoopProducerViolation(
        string BlockTag, AgentTemplateLocation Location, string Tag, string ProducerTag, BlockTypes BlockType,
        bool ProducerIsParameterized = false)
    {
        public string? SkillName => Location.SkillName;
    }

    /// <summary>A skill or a prompt field that references a field.</summary>
    public sealed record SkillFieldReference(string BlockTag, AgentTemplateLocation Location)
    {
        public string? SkillName => Location.SkillName;
    }

    private sealed record AgentTemplateTag(Block Block, AgentTemplateLocation Location, string Tag);

    /// <summary>
    /// Agent tags the run cannot fill, as save warnings: tags no producer supplies, then tags an
    /// expansion cannot fill. They never stop a save: they read as no data at run time, and an
    /// author may add the producer later.
    /// </summary>
    public static List<string> SaveWarnings(Workflow workflow)
    {
        var warnings = new List<string>();
        List<UnknownTagReference> unknownTags = FindUnknownTags(workflow);
        if (unknownTags.Count > 0)
        {
            string details = string.Join("; ", unknownTags.Select(
                unknown => $"{{{{{unknown.Tag}}}}} in {unknown.Location.Description} of block '{unknown.BlockTag}'"));
            warnings.Add(string.Format(CultureInfo.InvariantCulture, UNKNOWN_TAGS_WARNING, details));
        }

        warnings.AddRange(LoopProducerWarnings(workflow));
        return warnings;
    }

    /// <summary>
    /// Tags in an agent or verification block that an expansion cannot fill: a field of a loop-body
    /// producer read from after the loop, or a parameterized producer read by its template tag. They
    /// read as no data at run time, like an unknown tag, so a save warns about them and goes ahead.
    /// </summary>
    public static List<string> LoopProducerWarnings(Workflow workflow)
    {
        var warnings = new List<string>();
        foreach (AgentTemplateGrammar.LoopProducerViolation violation
            in AgentTemplateGrammar.FindLoopProducerViolations(workflow))
        {
            string location = violation.Location.Description;
            string subject = $"{char.ToUpperInvariant(location[0])}{location[1..]} of "
                + $"{AgentBlockGuard.BlockKindLabel(violation.BlockType).ToLowerInvariant()} '{violation.BlockTag}' references "
                + $"'{{{{{violation.Tag}}}}}' supplied by '{violation.ProducerTag}'";
            string remedy = violation.BlockType == BlockTypes.Verification
                ? " inside a looped or parameterized section. Expansion renames that tag per iteration and never "
                    + "rewrites a verifier's templates. Reference a producer outside the loop instead."
                : violation.ProducerIsParameterized
                    ? " inside a parameterized section. After expansion only the variant tags exist "
                        + $"('{violation.ProducerTag}_<variant key>'). Reference one of them, or move the agent into that section."
                    : " inside a loop. After the loop only the whole output reaches a reader, one line per iteration, "
                        + $"so a field or an index is never rewritten. Reference '{{{{{violation.ProducerTag}}}}}', or move the agent into the loop.";
            warnings.Add(subject + remedy);
        }

        return warnings;
    }

    /// <summary>
    /// Double-brace tags in an agent block's skills, task prompt or system prompt override whose
    /// base segment no producer supplies — no block tag, workflow variable, document class key,
    /// signature field, reserved input, or text input of the referencing block itself. Such tags
    /// stay unresolved at dispatch; the save warns about them and goes ahead.
    /// </summary>
    public static List<UnknownTagReference> FindUnknownTags(Workflow workflow)
    {
        var unknown = new List<UnknownTagReference>();
        HashSet<string>? known = null;
        Dictionary<string, ExpansionScope>? scopes = null;

        foreach (AgentTemplateTag reference in TemplateTags(workflow))
        {
            if (known is null)
            {
                known = WorkflowKnownBaseTags.Collect(workflow, includeSignatureFields: true);
                scopes = ExpansionScopesByBlockId(workflow);
                known.UnionWith(scopes.Values.SelectMany(static scope => scope.VariantTags));
            }

            string baseTag = reference.Tag.GetBaseReplacementTag();

            if (!known.Contains(baseTag)
                && !reference.Block.TextInputs.Any(ti => string.Equals(ti.ReplacementTag, baseTag, ReplacementTags.Comparison))
                && !(scopes!.TryGetValue(reference.Block.Id, out ExpansionScope? scope) && scope.ScopeTags.Contains(baseTag)))
            {
                unknown.Add(new UnknownTagReference(BlockTag(reference.Block), reference.Location, reference.Tag));
            }
        }

        return unknown;
    }

    /// <summary>
    /// Verification blocks inside a looped or parameterized section. An Agent block may sit there:
    /// expansion rewrites its skills and prompts per iteration like any other field. A verifier
    /// may not, because its members and its verdict are bound by tag to one group.
    /// </summary>
    public static List<LoopScopeViolation> FindLoopScopeViolations(Workflow workflow)
    {
        return [.. ExpansionRewrittenBlocks(workflow)
            .Where(entry => entry.Block.Type == BlockTypes.Verification)
            .Select(entry => new LoopScopeViolation(
                BlockTag(entry.Block), entry.Section.Title ?? string.Empty, entry.Block.Type))];
    }

    /// <summary>
    /// References an expansion cannot rewrite. A verifier's templates are never rewritten, so it may
    /// not read any producer inside a looped or parameterized section. An Agent block's templates are
    /// rewritten like every other field: it may read a sibling inside its own loop body or its own
    /// parameterized section, and a loop-body producer's whole output from after the loop (it
    /// receives every iteration's output, one per line). It may not read a field or an index of a
    /// loop-body producer from after the loop, which the fan-in never rewrites, nor a parameterized
    /// producer from outside its section by the template tag, because after expansion only the
    /// variant tags (<c>tag_KEY</c>) exist.
    /// </summary>
    public static List<LoopProducerViolation> FindLoopProducerViolations(Workflow workflow)
    {
        var producerSections = new Dictionary<string, Section>(StringComparer.FromComparison(ReplacementTags.Comparison));
        var parameterizedProducers = new HashSet<string>(StringComparer.FromComparison(ReplacementTags.Comparison));

        foreach ((Section section, Block block) in ExpansionRewrittenBlocks(workflow))
        {
            if (!string.IsNullOrWhiteSpace(block.ReplacementTag))
            {
                producerSections[block.ReplacementTag] = section;
                if (!string.IsNullOrWhiteSpace(section.ParameterizedConfig))
                {
                    _ = parameterizedProducers.Add(block.ReplacementTag);
                }
            }
        }

        var violations = new List<LoopProducerViolation>();

        if (producerSections.Count == 0)
        {
            return violations;
        }

        Dictionary<string, Section> sectionByBlockId = SectionsByBlockId(workflow);

        foreach (AgentTemplateTag reference in TemplateTags(workflow))
        {
            string baseTag = reference.Tag.GetBaseReplacementTag();

            if (!producerSections.TryGetValue(baseTag, out Section? producerSection))
            {
                continue;
            }

            bool parameterized = parameterizedProducers.Contains(baseTag);
            bool inProducersScope = sectionByBlockId.TryGetValue(reference.Block.Id, out Section? ownSection)
                && ReferenceEquals(ownSection, producerSection)
                && (parameterized || producerSection.GetLoopBodyBlockIds().Contains(reference.Block.Id));
            bool readsAField = !string.Equals(reference.Tag, baseTag, ReplacementTags.Comparison);

            bool unfillable = reference.Block.Type != BlockTypes.Agent
                || (!inProducersScope && (parameterized || readsAField));

            if (unfillable)
            {
                violations.Add(new LoopProducerViolation(
                    BlockTag(reference.Block), reference.Location, reference.Tag, baseTag, reference.Block.Type, parameterized));
            }
        }

        return violations;
    }

    /// <summary>
    /// Skills and prompt fields referencing <paramref name="fieldName"/> as a double-brace tag.
    /// Case-insensitive like the signature-rename subsystem itself — a rename must refuse for any
    /// casing of the reference, because renames never rewrite agent templates.
    /// </summary>
    public static List<SkillFieldReference> FindSkillsReferencingField(Workflow workflow, string fieldName)
    {
        return [.. TemplateTags(workflow)
            .Where(reference => string.Equals(reference.Tag.GetBaseReplacementTag(), fieldName, StringComparison.OrdinalIgnoreCase))
            .Select(reference => new SkillFieldReference(BlockTag(reference.Block), reference.Location))
            .Distinct()];
    }

    /// <summary>
    /// Every double-brace tag an agent-family block renders under the agent-template grammar, with
    /// where it sits: a skill's content, a skill's description (a tag its content already names is
    /// reported once), a skill file opted in to tag filling, the task prompt and the system prompt
    /// override, named as the editor labels them. An unflagged skill file is never scanned.
    /// </summary>
    private static IEnumerable<AgentTemplateTag> TemplateTags(Workflow workflow)
    {
        foreach (Block block in workflow.GetAllBlocks())
        {
            if (block.Properties is not IAgentRunProperties runProps)
            {
                continue;
            }

            foreach (AgentWorkerSkill skill in runProps.WorkerSkills)
            {
                List<string> contentTags = [.. AgentTemplateTags.ExtractEngineTags(skill.Content)];

                foreach (string tag in contentTags)
                {
                    yield return new AgentTemplateTag(block, AgentTemplateLocation.Skill(skill.Name), tag);
                }

                foreach (string tag in AgentTemplateTags.ExtractEngineTags(skill.Description)
                    .Where(tag => !contentTags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
                {
                    yield return new AgentTemplateTag(block, AgentTemplateLocation.Skill($"{skill.Name} (description)"), tag);
                }

                foreach (AgentWorkerSkillAsset asset in skill.TagFilledAssets())
                {
                    foreach (string tag in AgentTemplateTags.ExtractEngineTags(asset.Content))
                    {
                        yield return new AgentTemplateTag(
                            block, AgentTemplateLocation.Skill($"{skill.Name} (file {asset.Name})"), tag);
                    }
                }
            }

            (string promptLabel, string systemPromptLabel) = block.Type == BlockTypes.Verification
                ? (AgentPromptFieldLabels.EXTRA_JUDGING_INSTRUCTIONS, AgentPromptFieldLabels.EXTRA_SYSTEM_PROMPT)
                : (AgentPromptFieldLabels.TASK_PROMPT, AgentPromptFieldLabels.SYSTEM_PROMPT_OVERRIDE);

            foreach (string tag in AgentTemplateTags.ExtractEngineTags(runProps.PromptTemplate))
            {
                yield return new AgentTemplateTag(block, AgentTemplateLocation.Prompt(PromptFieldName(promptLabel)), tag);
            }

            foreach (string tag in AgentTemplateTags.ExtractEngineTags(runProps.SystemPromptTemplate))
            {
                yield return new AgentTemplateTag(block, AgentTemplateLocation.Prompt(PromptFieldName(systemPromptLabel)), tag);
            }
        }
    }

    private static string PromptFieldName(string label)
    {
        return $"the {label.ToLowerInvariant()}";
    }

    /// <summary>
    /// Blocks that loop or parameterized expansion rewrites per iteration/variant: every block
    /// of a parameterized section (the expander clones the whole section), but only the loop
    /// BODY of a looped one (<see cref="Section.GetLoopBodyBlockIds"/> — order-bounded between
    /// LoopStart and LoopEnd), so blocks placed before or after the loop in the same section
    /// stay expansion-free.
    /// </summary>
    private static IEnumerable<(Section Section, Block Block)> ExpansionRewrittenBlocks(Workflow workflow)
    {
        foreach (Section section in workflow.Sections)
        {
            if (!string.IsNullOrWhiteSpace(section.ParameterizedConfig))
            {
                foreach (Block block in section.Blocks)
                {
                    yield return (section, block);
                }

                continue;
            }

            IReadOnlySet<string> loopBodyIds = section.GetLoopBodyBlockIds();

            if (loopBodyIds.Count == 0)
            {
                continue;
            }

            foreach (Block block in section.Blocks)
            {
                if (loopBodyIds.Contains(block.Id))
                {
                    yield return (section, block);
                }
            }
        }
    }

    /// <summary>
    /// What an expansion makes resolvable for one block: its parameterized section's parameter
    /// names, or its loop's list title (the loop item). <see cref="VariantTags"/> are the
    /// <c>tag_KEY</c> names a parameterized section's blocks carry after expansion, which any
    /// block may reference.
    /// </summary>
    private sealed record ExpansionScope(HashSet<string> ScopeTags, List<string> VariantTags);

    private static Dictionary<string, ExpansionScope> ExpansionScopesByBlockId(Workflow workflow)
    {
        var scopes = new Dictionary<string, ExpansionScope>(StringComparer.Ordinal);
        var comparer = StringComparer.FromComparison(ReplacementTags.Comparison);

        foreach (Section section in workflow.Sections)
        {
            string? listTitle = section.Blocks
                .Select(static block => block.Properties)
                .OfType<LoopStartProperties>()
                .Select(static loopStart => loopStart.ListTitle)
                .FirstOrDefault(static title => !string.IsNullOrWhiteSpace(title));

            if (!string.IsNullOrWhiteSpace(section.ParameterizedConfig))
            {
                List<SectionVariant> variants = ParameterizedSectionNormalizer.Plan(section)?.Config?.Variants ?? [];
                var scopeTags = new HashSet<string>(
                    variants.SelectMany(static variant => variant.Params?.Keys ?? Enumerable.Empty<string>()),
                    comparer);
                if (listTitle is not null)
                {
                    _ = scopeTags.Add(listTitle);
                }

                List<string> variantTags = [.. section.Blocks
                    .Where(static block => !string.IsNullOrWhiteSpace(block.ReplacementTag))
                    .SelectMany(block => variants.Select(variant => $"{block.ReplacementTag}_{variant.Key}"))];
                var scope = new ExpansionScope(scopeTags, variantTags);

                foreach (Block block in section.Blocks)
                {
                    scopes[block.Id] = scope;
                }

                continue;
            }

            IReadOnlySet<string> loopBodyIds = section.GetLoopBodyBlockIds();
            if (loopBodyIds.Count == 0)
            {
                continue;
            }

            var loopScope = new ExpansionScope(new HashSet<string>(comparer), []);
            if (listTitle is not null)
            {
                _ = loopScope.ScopeTags.Add(listTitle);
            }

            foreach (Block block in section.Blocks.Where(block => loopBodyIds.Contains(block.Id)))
            {
                scopes[block.Id] = loopScope;
            }
        }

        return scopes;
    }

    private static Dictionary<string, Section> SectionsByBlockId(Workflow workflow)
    {
        var sections = new Dictionary<string, Section>(StringComparer.Ordinal);
        foreach (Section section in workflow.Sections)
        {
            foreach (Block block in section.Blocks)
            {
                sections[block.Id] = section;
            }
        }

        return sections;
    }

    private static string BlockTag(Block block)
    {
        return string.IsNullOrWhiteSpace(block.ReplacementTag) ? block.Id : block.ReplacementTag;
    }
}
