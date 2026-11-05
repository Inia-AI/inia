using IniaAi.Common;
using IniaAi.Common.Constants;
using IniaAi.Domain.Constants;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using System.Text.RegularExpressions;

namespace IniaAi.WebApp.Services;

/// <summary>
/// Service for validating replacement tags in prompt templates.
/// </summary>
public static partial class ReplacementTagValidationService
{
    // Regex to match valid replacement tags according to grammar:
    // {identifier} or {identifier.identifier}
    // identifier: starts with letter/underscore/digit, followed by letters/digits/underscores
    // This allows patterns like {block_0}, {block_12.answer}, {_x}, and {1bad}
    private static readonly Regex _replacementTagRegex = GenerateReplacementTagRegex();

    /// <summary>
    /// Precomputed per-(workflow, block) inputs for <see cref="ValidateReplacementTags(string?, TagValidationContext)"/>.
    /// Building these sets walks all blocks and deserializes every section's
    /// ParameterizedConfig — build once per block and reuse across its string fields.
    /// </summary>
    public sealed class TagValidationContext
    {
        public required HashSet<string> AvailableReplacementTags { get; init; }
        public required HashSet<string> IgnoredBaseTags { get; init; }
        public required HashSet<string> WorkflowVariableTags { get; init; }
        public required List<SectionVariant> ApplicableVariants { get; init; }
        public required HashSet<string> OutOfScopeLoopListTitles { get; init; }
    }

    public static TagValidationContext BuildValidationContext(Workflow workflow, Block? currentBlock = null)
    {
        return new TagValidationContext
        {
            AvailableReplacementTags = GetAvailableBlockNames(workflow, currentBlock),
            IgnoredBaseTags = GetIgnoredBaseTags(workflow),
            WorkflowVariableTags = GetWorkflowVariableTags(workflow),
            ApplicableVariants = GetApplicableVariants(workflow, currentBlock),
            OutOfScopeLoopListTitles = GetOutOfScopeLoopListTitles(workflow, currentBlock),
        };
    }

    /// <summary>
    /// Validates replacement tags in a prompt template against available blocks in the workflow.
    /// </summary>
    /// <param name="promptTemplate">The prompt template to validate</param>
    /// <param name="workflow">The workflow containing available blocks</param>
    /// <param name="currentBlock">The current block (to exclude from validation)</param>
    /// <returns>List of invalid replacement tags</returns>
    public static List<string> ValidateReplacementTags(string? promptTemplate, Workflow workflow, Block? currentBlock = null)
    {
        if (string.IsNullOrWhiteSpace(promptTemplate))
        {
            return [];
        }

        return ValidateReplacementTags(promptTemplate, BuildValidationContext(workflow, currentBlock));
    }

    public static List<string> ValidateReplacementTags(string? promptTemplate, TagValidationContext context)
    {
        if (string.IsNullOrWhiteSpace(promptTemplate))
        {
            return [];
        }

        List<ReplacementTagInfo> extractedTags = ExtractReplacementTags(promptTemplate);
        HashSet<string> availableReplacementTags = context.AvailableReplacementTags;
        HashSet<string> ignoredBaseTags = context.IgnoredBaseTags;
        HashSet<string> workflowVariableTags = context.WorkflowVariableTags;

        var invalidTags = new List<string>();

        foreach (ReplacementTagInfo tag in extractedTags)
        {
            if (tag.FullTag?.Contains('.') == true
                && context.OutOfScopeLoopListTitles.Contains(tag.BaseTag))
            {
                invalidTags.Add(tag.FullTagWithBraces ?? $"{tag.FullTag}");

                continue;
            }

            // Skip system-defined tags and loop list titles
            if (ignoredBaseTags.Contains(tag.BaseTag))
            {
                continue;
            }

            // Skip workflow variables
            if (workflowVariableTags.Contains(tag.BaseTag))
            {
                continue;
            }

            // Check if the base tag (before the dot) exists as a replacement tag
            if (!availableReplacementTags.Contains(tag.BaseTag))
            {
                // Return with braces to align with UI/tests expectations
                invalidTags.Add(tag.FullTagWithBraces ?? $"{tag.FullTag}");
            }
        }

        // Nested parameterized references like {revenue_notes_{PERIOD_ID}.answer} are
        // skipped by the flat extractor above (the inner brace makes them "nested").
        // Validate them by replaying the section's variant expansion.
        invalidTags.AddRange(FindInvalidNestedParameterizedTags(
            promptTemplate, context.ApplicableVariants, availableReplacementTags, ignoredBaseTags, workflowVariableTags));

        return [.. invalidTags.Distinct()];
    }

    /// <summary>
    /// Validates nested parameterized references such as {base_{PARAM}.suffix} by
    /// replaying the same param substitution the runtime expander performs, once per
    /// variant, and checking the resolved flat tag against the available tags. A
    /// reference is flagged if it resolves to an unknown base tag in any variant
    /// (it must be valid for every variant the section expands into). Constructs
    /// whose inner brace is not a known parameter (e.g. embedded JSON) are left alone.
    /// </summary>
    private static List<string> FindInvalidNestedParameterizedTags(
        string promptTemplate,
        List<SectionVariant> variants,
        HashSet<string> availableReplacementTags,
        HashSet<string> ignoredBaseTags,
        HashSet<string> workflowVariableTags)
    {
        var invalid = new List<string>();

        if (variants.Count == 0)
        {
            return invalid;
        }

        var paramNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (SectionVariant variant in variants)
        {
            if (variant.Params is null)
            {
                continue;
            }

            foreach (string name in variant.Params.Keys)
            {
                _ = paramNames.Add(name);
            }
        }

        if (paramNames.Count == 0)
        {
            return invalid;
        }

        // No code-fence stripping: the runtime resolves placeholders everywhere, and embedded
        // JSON is filtered out below by requiring the inner brace to name a known parameter.
        foreach (Match outer in GenerateNestedTagRegex().Matches(promptTemplate))
        {
            string construct = outer.Value;

            // Only treat this as a parameterized reference if the inner brace names a
            // known parameter. Otherwise it could be embedded JSON or unrelated braces.
            Match inner = GenerateInnerBraceRegex().Match(construct);
            if (!inner.Success || !paramNames.Contains(inner.Groups[1].Value))
            {
                continue;
            }

            foreach (SectionVariant variant in variants)
            {
                string resolved = construct;
                if (variant.Params is not null)
                {
                    foreach ((string name, string value) in variant.Params)
                    {
                        resolved = resolved.Replace($"{{{name}}}", value, StringComparison.Ordinal);
                    }
                }

                // After substitution the reference must be a single flat tag. If it is
                // not (e.g. a param did not resolve, leaving nested braces), skip it.
                Match flat = _replacementTagRegex.Match(resolved);
                if (!flat.Success || flat.Index != 0 || flat.Length != resolved.Length)
                {
                    continue;
                }

                string baseTag = flat.Groups[1].Value.GetBaseReplacementTag();

                if (ignoredBaseTags.Contains(baseTag)
                    || workflowVariableTags.Contains(baseTag)
                    || availableReplacementTags.Contains(baseTag))
                {
                    continue;
                }

                // Unknown in this variant — flag the original reference once and stop.
                invalid.Add(construct);
                break;
            }
        }

        return invalid;
    }

    /// <summary>
    /// Returns the variants of the parameterized section that owns this template
    /// (the current block's section, falling back to the supplied workflow), or an
    /// empty list when neither is parameterized.
    /// </summary>
    private static List<SectionVariant> GetApplicableVariants(Workflow workflow, Block? currentBlock)
    {
        foreach (string? parameterizedConfig in new[] { currentBlock?.Section?.ParameterizedConfig, workflow.ParameterizedConfig })
        {
            if (string.IsNullOrWhiteSpace(parameterizedConfig))
            {
                continue;
            }

            try
            {
                ParameterizedSectionConfig? config = Newtonsoft.Json.JsonConvert.DeserializeObject<ParameterizedSectionConfig>(
                    parameterizedConfig);

                if (config?.Variants is { Count: > 0 })
                {
                    return config.Variants;
                }
            }
            catch { /* Malformed ParameterizedConfig — skip, validation should not crash */ }
        }

        return [];
    }

    /// <summary>
    /// Returns suffixed variant tags for a base tag from parameterized sections.
    /// E.g., for "PL_Operating" returns ["PL_Operating_AR", "PL_Operating_QR", ...].
    /// </summary>
    public static List<string> GetParameterizedSuggestions(string invalidTag, Workflow workflow)
    {
        var fullTag = invalidTag.TrimStart('{').TrimEnd('}');
        var dotIndex = fullTag.IndexOf('.');
        var baseTag = dotIndex > 0 ? fullTag[..dotIndex] : fullTag;
        var dotSuffix = dotIndex > 0 ? fullTag[dotIndex..] : "";

        var suggestions = new List<string>();
        Workflow root = workflow;

        foreach (Section section in root.Sections)
        {
            if (string.IsNullOrWhiteSpace(section.ParameterizedConfig))
            {
                continue;
            }

            if (!section.Blocks.Any(b => b.ReplacementTag == baseTag))
            {
                continue;
            }

            try
            {
                ParameterizedSectionConfig? config = Newtonsoft.Json.JsonConvert.DeserializeObject<ParameterizedSectionConfig>(
                    section.ParameterizedConfig);

                if (config?.Variants is null)
                {
                    continue;
                }

                foreach (SectionVariant variant in config.Variants)
                {
                    suggestions.Add($"{{{baseTag}_{variant.Key}{dotSuffix}}}");
                }
            }
            catch { /* Malformed ParameterizedConfig — skip, validation should not crash */ }
        }

        return suggestions;
    }

    /// <summary>
    /// Extracts replacement tags from a prompt template using whitelist approach.
    /// Only extracts tags that match the replacement tag grammar:
    /// - {identifier} or {identifier.identifier}
    /// - identifier may start with a letter, underscore, or digit
    /// - identifier can contain letters, digits, underscores
    /// - No spaces, quotes, or other punctuation allowed
    /// </summary>
    /// <param name="promptTemplate">The prompt template to parse</param>
    /// <returns>List of replacement tag information</returns>
    private static List<ReplacementTagInfo> ExtractReplacementTags(string promptTemplate)
    {
        var tags = new List<ReplacementTagInfo>();

        // Same grammar as the runtime engine (GET_PLACEHOLDERS).
        foreach (string innerContent in promptTemplate.GetPlaceholders())
        {
            tags.Add(new ReplacementTagInfo
            {
                FullTag = innerContent, // Store without braces for compatibility
                BaseTag = innerContent.GetBaseReplacementTag(),
                FullTagWithBraces = $"{{{innerContent}}}" // Store with braces for user display
            });
        }

        return tags;
    }

    /// <summary>
    /// Gets all available replacement tags from the workflow.
    /// </summary>
    /// <param name="workflow">The workflow to scan</param>
    /// <param name="currentBlock">The current block to exclude</param>
    /// <returns>Set of available replacement tags</returns>
    private static HashSet<string> GetAvailableBlockNames(Workflow workflow, Block? currentBlock = null)
    {
        var replacementTags = new HashSet<string>(StringComparer.Ordinal);
        List<Block> allBlocks = workflow.GetAllBlocks();
        Workflow root = workflow;

        var parameterizedBlockTags = new HashSet<string>(StringComparer.Ordinal);
        Section? currentBlockSection = currentBlock?.Section;

        foreach (Section section in root.Sections)
        {
            if (string.IsNullOrWhiteSpace(section.ParameterizedConfig))
            {
                continue;
            }

            if (currentBlockSection is not null && section.Id == currentBlockSection.Id)
            {
                continue;
            }

            foreach (Block block in section.Blocks)
            {
                if (!string.IsNullOrWhiteSpace(block.ReplacementTag))
                {
                    _ = parameterizedBlockTags.Add(block.ReplacementTag);
                }
            }
        }

        foreach (Block block in allBlocks)
        {
            foreach (TextInput textInput in block.TextInputs)
            {
                if (!string.IsNullOrWhiteSpace(textInput.ReplacementTag))
                {
                    _ = replacementTags.Add(textInput.ReplacementTag);
                }
            }

            if (currentBlock is not null && block.Id == currentBlock.Id)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(block.ReplacementTag)
                && !parameterizedBlockTags.Contains(block.ReplacementTag))
            {
                _ = replacementTags.Add(block.ReplacementTag);
            }
        }

        // Signature field tokens resolve against the workflow-level definitions at execution,
        // but only for blocks whose properties implement IHasSignatureFields — a token in any
        // other block would never be injected, so it must still be flagged.
        if (currentBlock?.Properties is IHasSignatureFields)
        {
            foreach (WorkflowSignatureField signatureField in root.SignatureFields)
            {
                if (!string.IsNullOrWhiteSpace(signatureField.Name))
                {
                    _ = replacementTags.Add(signatureField.Name);
                }
            }
        }

        // Add suffixed tags from parameterized sections (e.g., PL_Operating_AR, PL_Operating_QR)
        foreach (Section section in root.Sections)
        {
            AddSuffixedTagsFromParameterizedConfig(section.ParameterizedConfig, section.Blocks, replacementTags);
        }

        return replacementTags;
    }

    private static void AddSuffixedTagsFromParameterizedConfig(
        string? parameterizedConfig,
        IEnumerable<Block> blocks,
        HashSet<string> tags)
    {
        if (string.IsNullOrWhiteSpace(parameterizedConfig))
        {
            return;
        }

        try
        {
            ParameterizedSectionConfig? config = Newtonsoft.Json.JsonConvert.DeserializeObject<ParameterizedSectionConfig>(
                parameterizedConfig);

            if (config?.Variants is null)
            {
                return;
            }

            foreach (SectionVariant variant in config.Variants)
            {
                foreach (Block block in blocks)
                {
                    if (!string.IsNullOrWhiteSpace(block.ReplacementTag))
                    {
                        _ = tags.Add($"{block.ReplacementTag}_{variant.Key}");
                    }
                }
            }
        }
        catch { /* Malformed ParameterizedConfig — skip, validation should not crash */ }
    }

    /// <summary>
    /// Gets all workflow variable replacement tags, including parent company variables.
    /// </summary>
    /// <param name="workflow">The workflow to scan</param>
    /// <returns>Set of workflow variable replacement tags</returns>
    private static HashSet<string> GetWorkflowVariableTags(Workflow workflow)
    {
        var tags = new HashSet<string>(StringComparer.Ordinal);

        if (workflow.Variables is not null)
        {
            foreach (WorkflowVariable variable in workflow.Variables)
            {
                if (!string.IsNullOrWhiteSpace(variable.ReplacementTag))
                {
                    _ = tags.Add(variable.ReplacementTag);
                }
            }
        }

        foreach (string tag in DefaultWorkflowVariables.AllParentCompanyTags)
        {
            _ = tags.Add(tag);
        }

        return tags;
    }

    // Note: System-ignored tags are handled via GetIgnoredBaseTags(workflow)

    /// <summary>
    /// Returns a set of base tags to ignore globally during validation.
    /// Includes reserved tags and LoopStart list titles (e.g., loopItems).
    /// </summary>
    private static HashSet<string> GetIgnoredBaseTags(Workflow workflow)
    {
        var ignored = new HashSet<string>(SharedInputKeys.ReservedReplacementTags, StringComparer.Ordinal);

        // Add LoopStart "ListTitle" values when present (property exists on all blocks, non-empty only for LoopStart)
        foreach (Block b in workflow.GetAllBlocks().Where(b => b.Type is BlockTypes.LoopStart))
        {
            string? value = (b.Properties as LoopStartProperties)?.ListTitle;

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            _ = ignored.Add(value);
        }

        // Add parameterized section param names (resolved at runtime by the expander)
        CollectParameterizedParamNames(workflow.ParameterizedConfig, ignored);
        foreach (Section section in workflow.Sections)
        {
            CollectParameterizedParamNames(section.ParameterizedConfig, ignored);
        }

        return ignored;
    }

    /// <summary>
    /// Loop list titles the given block cannot bind a dotted reference to. Loop expansion rewrites
    /// <c>{Funds.reportingStyle}</c> to a specific iteration only inside the loop body; anywhere else
    /// it would survive into the prompt verbatim. A title stays in scope when any loop containing the
    /// block declares it, so per-manager forks that iterate the same list all validate.
    /// </summary>
    private static HashSet<string> GetOutOfScopeLoopListTitles(Workflow workflow, Block? currentBlock)
    {
        var listTitles = new HashSet<string>(StringComparer.Ordinal);

        if (currentBlock is null)
        {
            return listTitles;
        }

        var inScopeListTitles = new HashSet<string>(StringComparer.Ordinal);

        foreach (Section section in workflow.Sections)
        {
            Block? loopStart = section.Blocks.FirstOrDefault(b => b.Type is BlockTypes.LoopStart);
            Block? loopEnd = section.Blocks.FirstOrDefault(b => b.Type is BlockTypes.LoopEnd);
            string? listTitle = (loopStart?.Properties as LoopStartProperties)?.ListTitle;

            if (loopStart is null || loopEnd is null || string.IsNullOrWhiteSpace(listTitle))
            {
                continue;
            }

            _ = listTitles.Add(listTitle);

            if (section.Blocks.Any(b => b.Id == currentBlock.Id)
                && currentBlock.Order > loopStart.Order
                && currentBlock.Order < loopEnd.Order)
            {
                _ = inScopeListTitles.Add(listTitle);
            }
        }

        listTitles.ExceptWith(inScopeListTitles);

        return listTitles;
    }

    private static void CollectParameterizedParamNames(string? parameterizedConfig, HashSet<string> ignored)
    {
        if (string.IsNullOrWhiteSpace(parameterizedConfig))
        {
            return;
        }

        try
        {
            ParameterizedSectionConfig? config = Newtonsoft.Json.JsonConvert.DeserializeObject<ParameterizedSectionConfig>(
                parameterizedConfig);

            if (config?.Variants is null)
            {
                return;
            }

            foreach (SectionVariant variant in config.Variants)
            {
                if (variant.Params is not null)
                {
                    foreach (var paramName in variant.Params.Keys)
                    {
                        _ = ignored.Add(paramName);
                    }
                }
            }
        }
        catch { /* Malformed ParameterizedConfig — skip, validation should not crash */ }
    }

    /// <summary>
    /// Information about a replacement tag found in a prompt.
    /// </summary>
    private class ReplacementTagInfo
    {
        public string FullTag { get; set; } = string.Empty; // Without braces, for compatibility
        public string BaseTag { get; set; } = string.Empty; // Part before dot
        public string? FullTagWithBraces { get; set; } // With braces, for user display
    }

    [GeneratedRegex(@"\{([A-Za-z0-9_][A-Za-z0-9_]*(?:\.[A-Za-z0-9_][A-Za-z0-9_]*)?)\}", RegexOptions.Compiled)]
    private static partial Regex GenerateReplacementTagRegex();

    // Matches an outer brace that contains exactly one nested brace, e.g.
    // {revenue_notes_{PERIOD_ID}.answer}. Used to locate parameterized references
    // the flat extractor skips.
    [GeneratedRegex(@"\{[^{}]*\{[^{}]+\}[^{}]*\}", RegexOptions.Compiled)]
    private static partial Regex GenerateNestedTagRegex();

    // Matches the first non-nested {identifier} brace, used to read the inner
    // parameter name out of a nested construct.
    [GeneratedRegex(@"\{([A-Za-z0-9_]+)\}", RegexOptions.Compiled)]
    private static partial Regex GenerateInnerBraceRegex();
}
