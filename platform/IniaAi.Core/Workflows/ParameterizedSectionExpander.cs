using System.Text.RegularExpressions;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;

namespace IniaAi.Core.Workflows;

public static class ParameterizedSectionExpander
{
    private const string DOC_CLASS_ANY = DocumentClass.KEY_ANY;
    private const string DOC_CLASS_NONE = DocumentClass.KEY_NONE;

    public static List<Section> Expand(Section templateSection, ParameterizedSectionConfig config)
    {
        var expandedSections = new List<Section>();
        IReadOnlyList<Func<string, string>> rewriters = VariantRewriters(templateSection, config);

        for (int i = 0; i < config.Variants.Count; i++)
        {
            SectionVariant variant = config.Variants[i];
            Section section = CloneSection(templateSection, variant, i);
            ApplyVariantToSection(section, variant, rewriters[i]);
            expandedSections.Add(section);
        }

        return expandedSections;
    }

    /// <summary>
    /// The template-string rewrite each variant applies, one per variant in config order: sibling
    /// tags gain the variant suffix, then the variant's params substitute. The dependency map scans
    /// a variant through the same rewrite, so it cannot read a variant differently from the run.
    /// </summary>
    public static IReadOnlyList<Func<string, string>> VariantRewriters(
        Section templateSection,
        ParameterizedSectionConfig config)
    {
        List<(string Tag, Regex Pattern)> internalTagPatterns = [.. templateSection.Blocks
            .Where(b => !string.IsNullOrWhiteSpace(b.ReplacementTag))
            .Select(b => b.ReplacementTag)
            .Distinct()
            .Select(tag => (tag, new Regex(@"\{" + Regex.Escape(tag) + @"(\b[^}]*)?\}")))];

        return [.. config.Variants.Select(variant => VariantRewriter(variant, internalTagPatterns))];
    }

    /// <summary>
    /// A block's document classes as the variant sees them: each class mapped through the
    /// variant's <see cref="SectionVariant.ClassMap"/>, the Any/None sentinels passing through.
    /// </summary>
    public static List<string> MapDocumentClasses(IEnumerable<string> documentClasses, SectionVariant variant)
    {
        if (variant.ClassMap is not { Count: > 0 } classMap)
        {
            return [.. documentClasses];
        }

        return [.. documentClasses
            .SelectMany(c => MapClass(c, classMap))
            .Distinct()];
    }

    public static IReadOnlyList<Document> CollectDocumentsFromTree(Workflow liveRoot)
    {
        return liveRoot.Sections
            .SelectMany(s => s.Blocks)
            .SelectMany(b => b.Documents)
            .DistinctBy(d => d.Id)
            .ToList();
    }

    public static void DistributeDocumentsToVariants(
        IReadOnlyList<Section> expandedSections,
        IReadOnlyList<Document> allDocs)
    {
        foreach (Section expandedSection in expandedSections)
        {
            foreach (Block block in expandedSection.Blocks)
            {
                if (block.SupportedDocumentClasses.Count == 0
                    || block.SupportedDocumentClasses.Contains(DOC_CLASS_ANY))
                {
                    block.Documents.AddRange(allDocs);
                }
                else if (!block.SupportedDocumentClasses.Contains(DOC_CLASS_NONE))
                {
                    block.Documents.AddRange(
                        allDocs.Where(d => d.Class is not null && block.SupportedDocumentClasses.Contains(d.Class)));
                }
            }
        }
    }

    private static Section CloneSection(Section template, SectionVariant variant, int variantIndex)
    {
        var section = new Section
        {
            Id = Guid.NewGuid().ToString(),
            Title = $"{template.Title} - {variant.Key}",
            Description = template.Description,
            GroupColor = template.GroupColor,
            Properties = template.Properties.Clone(),
            Configuration = template.Configuration?.Clone(),
            Order = template.Order + variantIndex,
            TextInputTemplate = variant.SectionStopGate,
            ExpandedFromParameterizedKey = variant.Key,
            StableSectionId = template.StableSectionId,
            SharedComponentId = template.SharedComponentId,
            SharedComponentLinkState = template.SharedComponentLinkState,
            SharedComponentVersionHash = template.SharedComponentVersionHash,
            SharedComponentBindings = template.SharedComponentBindings is null
                ? null
                : new Dictionary<string, string>(template.SharedComponentBindings),
            SharedComponentSlotStates = template.SharedComponentSlotStates is null
                ? null
                : new Dictionary<string, SharedComponentSlotState>(template.SharedComponentSlotStates),
        };

        section.SuffixStableSectionId(variant.Key);

        foreach (Block block in template.Blocks.OrderBy(b => b.Order))
        {
            string? suffixedTag = string.IsNullOrWhiteSpace(block.ReplacementTag)
                ? block.ReplacementTag
                : $"{block.ReplacementTag}_{variant.Key}";
            Block clone = block.CloneToSection(section, replacementTag: suffixedTag);
            clone.SuffixStableBlockId(variant.Key);
            clone.SuffixDataOutputName(variant.Key);
        }

        return section;
    }

    private static void ApplyVariantToSection(
        Section section,
        SectionVariant variant,
        Func<string, string> rewrite)
    {
        foreach (Block block in section.Blocks)
        {
            ApplyVariantToBlock(block, variant, rewrite);
        }
    }

    /// <summary>
    /// One block as the variant sees it: its templates through the variant rewrite, and every
    /// document class it reads, scopes to or creates mapped through the variant's class map, so
    /// each variant reads and writes its own classes.
    /// </summary>
    public static void ApplyVariantToBlock(Block block, SectionVariant variant, Func<string, string> rewrite)
    {
        block.TransformAllTemplateStrings(rewrite);

        if (variant.ClassMap is not { Count: > 0 })
        {
            return;
        }

        block.SupportedDocumentClasses = MapDocumentClasses(block.SupportedDocumentClasses, variant);

        if (block.Properties is IProducesDocumentBlockProperties producer)
        {
            producer.CreatesDocumentClass = MapSingleClass(producer.CreatesDocumentClass, variant.ClassMap);
        }

        if (block.Properties is Json2XlsxProperties json2Xlsx)
        {
            json2Xlsx.InputDocumentClass = MapSingleClass(json2Xlsx.InputDocumentClass, variant.ClassMap);
        }
    }

    // A single-class field takes the first mapped class: it names one document, not a set.
    private static string? MapSingleClass(string? documentClass, Dictionary<string, List<string>> classMap)
    {
        if (string.IsNullOrWhiteSpace(documentClass))
        {
            return documentClass;
        }

        return MapClass(documentClass, classMap).First();
    }

    private static Func<string, string> VariantRewriter(
        SectionVariant variant,
        List<(string Tag, Regex Pattern)> internalTagPatterns)
    {
        List<(Regex Pattern, string Replacement)> tagSuffixes = [.. internalTagPatterns
            .Select(tp => (tp.Pattern, "{" + tp.Tag + "_" + variant.Key + "$1}"))];
        List<(string Placeholder, string Value)> parameters = [.. (variant.Params ?? [])
            .Select(p => ("{" + p.Key + "}", p.Value))];

        return text => RewriteForVariant(text, tagSuffixes, parameters);
    }

    private static IEnumerable<string> MapClass(string documentClass, Dictionary<string, List<string>> classMap)
    {
        // The Any/None sentinels always pass through, even if a hand-authored map lists them.
        // An explicitly empty target list is pass-through too: emptying a block's classes
        // would mean "all documents" at distribution, the opposite of any plausible intent.
        if (documentClass is not (DOC_CLASS_ANY or DOC_CLASS_NONE)
            && classMap.TryGetValue(documentClass, out List<string>? mapped)
            && mapped is { Count: > 0 })
        {
            return mapped;
        }

        return [documentClass];
    }

    // Sibling suffixing runs before params, so a param value that names a sibling tag is not suffixed.
    private static string RewriteForVariant(
        string text,
        List<(Regex Pattern, string Replacement)> tagSuffixes,
        List<(string Placeholder, string Value)> parameters)
    {
        foreach ((Regex pattern, string replacement) in tagSuffixes)
        {
            text = pattern.Replace(text, replacement);
        }

        foreach ((string placeholder, string value) in parameters)
        {
            text = text.Replace(placeholder, value);
        }

        return text;
    }
}
