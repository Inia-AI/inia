using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using System.Text.RegularExpressions;

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

/// <summary>
/// Editor-side helpers over the derived signature-field binding: a block references a
/// workflow-level field iff its templated strings contain the <c>{Name}</c> token.
/// Pure static logic (no component state) so it is unit-testable.
/// </summary>
public static partial class SignatureFieldReferences
{
    /// <summary>Blocks whose templates reference <paramref name="fieldName"/>, in (section, block) order.</summary>
    public static List<Block> GetReferencingBlocks(Workflow workflow, string fieldName)
    {
        return workflow.GetSignatureFieldReferences()
            .TryGetValue(fieldName, out List<Block>? blocks)
            ? blocks
            : [];
    }

    /// <summary>
    /// The workflow-level field names this block's templates reference, ordered by first
    /// appearance in the block's editor-ordered templates (unmatched-in-ordering names last,
    /// by name).
    /// </summary>
    public static List<string> GetReferencedFieldNames(Block block, Workflow workflow)
    {
        if (block.Properties is not IHasSignatureFields bearer || workflow.SignatureFields.Count == 0)
        {
            return [];
        }

        List<string> referenced = [.. workflow.SignatureFields
            .Select(f => f.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name)
                && block.Properties.HasPlaceholder(
                    Workflow.SignatureFieldToken(name), StringComparison.OrdinalIgnoreCase))];

        if (referenced.Count == 0)
        {
            return [];
        }

        List<string?> orderingTemplates = [.. bearer.GetSignatureOrderingTemplates()];

        return [.. referenced
            .OrderBy(name => FindFirstAppearance(orderingTemplates, name))
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Candidate tokens in the block's templates that match no signature field definition,
    /// workflow variable tag, or block replacement tag — surfaced as quick-create
    /// suggestions. Conservative: plain-identifier tokens only, <c>{{…}}</c> doubles and
    /// dotted references excluded.
    /// </summary>
    public static List<string> GetUnmatchedTokens(Block block, Workflow workflow)
    {
        if (block.Properties is not IHasSignatureFields)
        {
            return [];
        }

        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (WorkflowSignatureField field in workflow.SignatureFields)
        {
            _ = known.Add(field.Name);
        }

        foreach (WorkflowVariable variable in workflow.Variables)
        {
            if (!string.IsNullOrWhiteSpace(variable.ReplacementTag))
            {
                _ = known.Add(variable.ReplacementTag);
            }
        }

        foreach (Block other in workflow.GetAllBlocks())
        {
            if (!string.IsNullOrWhiteSpace(other.ReplacementTag))
            {
                _ = known.Add(other.ReplacementTag);
            }
        }

        AddParameterizedParams(block.Section, known);

        var unmatched = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string template in block.Properties.GetAllTemplateStrings())
        {
            foreach (Match match in TokenRegex().Matches(template))
            {
                string token = match.Groups[1].Value;
                if (!known.Contains(token) && seen.Add(token))
                {
                    unmatched.Add(token);
                }
            }
        }

        return unmatched;
    }

    /// <summary>
    /// Rewrites <c>{oldName}</c> to <c>{newName}</c> across every referencing block's
    /// templates. Returns the number of blocks touched.
    /// </summary>
    public static int RenameToken(Workflow workflow, string oldName, string newName)
    {
        int touched = 0;
        foreach (Block block in workflow.GetSignatureBearingBlocks())
        {
            if (!block.Properties.HasPlaceholder(
                    Workflow.SignatureFieldToken(oldName), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            block.Properties.RewriteToken(oldName, newName, StringComparison.OrdinalIgnoreCase);
            touched++;
        }

        return touched;
    }

    private static void AddParameterizedParams(Section? section, HashSet<string> known)
    {
        if (section is null || string.IsNullOrWhiteSpace(section.ParameterizedConfig))
        {
            return;
        }

        ParameterizedSectionConfig? config;
        try
        {
            config = Newtonsoft.Json.JsonConvert.DeserializeObject<ParameterizedSectionConfig>(
                section.ParameterizedConfig);
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return;
        }

        foreach (SectionVariant variant in config?.Variants ?? [])
        {
            foreach (string paramKey in variant.Params?.Keys ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(paramKey))
                {
                    _ = known.Add(paramKey);
                }
            }
        }
    }

    private static long FindFirstAppearance(IReadOnlyList<string?> orderingTemplates, string name)
    {
        string token = Workflow.SignatureFieldToken(name);
        for (int templateIndex = 0; templateIndex < orderingTemplates.Count; templateIndex++)
        {
            string? template = orderingTemplates[templateIndex];
            if (string.IsNullOrEmpty(template))
            {
                continue;
            }

            int position = template.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (position >= 0)
            {
                return ((long)templateIndex << 32) + position;
            }
        }

        return long.MaxValue;
    }

    // Single-brace plain identifiers only: `(?<!\{)` and `(?!\})` exclude `{{double}}` forms,
    // and the charset excludes dotted block references like {tag.answer}.
    [GeneratedRegex(@"(?<!\{)\{([A-Za-z0-9_-]+)\}(?!\})")]
    private static partial Regex TokenRegex();
}
