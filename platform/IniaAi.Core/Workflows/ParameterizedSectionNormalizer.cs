using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using Newtonsoft.Json;

namespace IniaAi.Core.Workflows;

/// <summary>
/// Converts legacy parameterized-section configs (per-variant replace lists) into ClassMap
/// form: every block gets the canonical base class, each variant a map from that base to its
/// legacy list. The legacy list itself is retained untouched so that a code rollback after
/// the conversion migration still behaves exactly as before it. The migration applies the
/// same rule to the Core database; this covers the remaining legacy sources (restored
/// versions, imports, raw API saves).
/// </summary>
public static class ParameterizedSectionNormalizer
{
    private const string DOC_CLASS_ANY = DocumentClass.KEY_ANY;
    private const string DOC_CLASS_NONE = DocumentClass.KEY_NONE;

    /// <summary>
    /// Normalizes the section in place when its config is legacy-shaped and returns the
    /// parsed (post-normalization) config. Returns null when there is no config or it
    /// does not parse.
    /// </summary>
    public static ParameterizedSectionConfig? Normalize(Section section)
    {
        if (Plan(section) is not { } plan)
        {
            return null;
        }

        if (plan.BlockClasses is not null)
        {
            foreach (Block block in section.Blocks)
            {
                block.SupportedDocumentClasses = [.. plan.BlockClasses];
            }

            section.ParameterizedConfig = JsonConvert.SerializeObject(plan.Config);
        }

        return plan.Config;
    }

    /// <summary>
    /// What <see cref="Normalize"/> would do, without changing the section: the post-normalization
    /// config, and the class list every block takes (null when the blocks keep their own). Returns
    /// null when there is no config or it does not parse.
    /// </summary>
    public static ParameterizedSectionPlan? Plan(Section section)
    {
        if (string.IsNullOrWhiteSpace(section.ParameterizedConfig))
        {
            return null;
        }

        ParameterizedSectionConfig? config;
        try
        {
            config = JsonConvert.DeserializeObject<ParameterizedSectionConfig>(section.ParameterizedConfig);
        }
        catch (JsonException)
        {
            return null;
        }

        if (config?.Variants is not { Count: > 0 } variants)
        {
            return new ParameterizedSectionPlan(config, BlockClasses: null);
        }

        if (variants.Any(v => v.ClassMap is not null))
        {
            return new ParameterizedSectionPlan(config, BlockClasses: null);
        }

        string? baseClass = variants
            .SelectMany(v => v.SectionSupportedDocumentClasses ?? [])
            .FirstOrDefault(c => c is not (null or DOC_CLASS_ANY or DOC_CLASS_NONE));

        if (baseClass is null)
        {
            // No variant names a real class. When no variant says "None" either, every
            // variant meant "all documents": resetting the blocks reproduces that. A mix
            // involving "None" is not expressible as a map and is left untouched.
            bool anyNone = variants
                .SelectMany(v => v.SectionSupportedDocumentClasses ?? [])
                .Contains(DOC_CLASS_NONE);
            if (anyNone)
            {
                return new ParameterizedSectionPlan(config, BlockClasses: null);
            }

            return new ParameterizedSectionPlan(Rewrite(variants, v => []), BlockClasses: []);
        }

        return new ParameterizedSectionPlan(
            Rewrite(variants, v => BuildClassMap(v.SectionSupportedDocumentClasses, baseClass)),
            BlockClasses: [baseClass]);
    }

    private static ParameterizedSectionConfig Rewrite(
        List<SectionVariant> variants,
        Func<SectionVariant, Dictionary<string, List<string>>> buildMap)
    {
        return new ParameterizedSectionConfig(
            [.. variants.Select(v => v with { ClassMap = buildMap(v) })]);
    }

    private static Dictionary<string, List<string>> BuildClassMap(List<string>? legacyClasses, string baseClass)
    {
        // Legacy null/empty meant "all documents", spelled "Any" in map form.
        List<string> targets = legacyClasses is { Count: > 0 }
            ? [.. legacyClasses]
            : [DOC_CLASS_ANY];

        bool isIdentity = targets.Count == 1 && targets[0] == baseClass;
        return isIdentity ? [] : new Dictionary<string, List<string>> { [baseClass] = targets };
    }
}

/// <param name="Config">The config after normalization.</param>
/// <param name="BlockClasses">The class list every block of the section takes, or null when they keep their own.</param>
public sealed record ParameterizedSectionPlan(ParameterizedSectionConfig? Config, IReadOnlyList<string>? BlockClasses);
