using IniaAi.Common.Serialization;
using Newtonsoft.Json.Linq;
using System.Globalization;

namespace IniaAi.Core.Workflows.Diff;

/// <summary>
/// Structured, block-level diff between two canonical workflow JSONs (as produced by
/// <c>WorkflowCanonicalizer</c> / stored on <c>WorkflowVersion.WorkflowJson</c>). Blocks are
/// matched tree-wide by <c>ReplacementTag</c>, variables by replacement tag, document classes
/// by key, and sections by greatest block overlap — so reordering or shifting same-identity
/// items reads as no change. A <c>ReplacementTag</c> that repeats is told apart per block (see
/// <c>KeyBlocks</c>), so no block's change is hidden behind a same-tag sibling. Stable ids ride in
/// payload JSON but are identity metadata, not content: they never emit field diffs, and sections
/// are still aligned by the blocks they contain rather than by id (pre-payload-profile payloads
/// carry no ids, and section names are routinely empty); otherwise deleting one section would
/// misreport every later section as edited.
/// </summary>
public class WorkflowDiffService
{
    private const string SECTIONS = "Sections";
    private const string BLOCKS = "Blocks";
    private const string VARIABLES = "Variables";
    private const string DOCUMENT_CLASSES = "DocumentClasses";
    private const string REPLACEMENT_TAG = "ReplacementTag";
    private const string ORDER = "Order";
    private const string NAME = "Name";
    private const string TITLE = "Title";
    private const string KEY = "Key";
    private const string TYPE = "Type";
    private const string DISPLAY_NAME = "DisplayName";
    private const string STABLE_BLOCK_ID = "StableBlockId";
    private const string STABLE_SECTION_ID = "StableSectionId";
    private const char REPEATED_TAG_KEY_SEPARATOR = '\u001f';

    private static readonly string[] _identityFields = [STABLE_BLOCK_ID, STABLE_SECTION_ID];

    public WorkflowDiff Compare(string canonicalBefore, string canonicalAfter)
    {
        var before = JsonText.ParseObject(canonicalBefore);
        var after = JsonText.ParseObject(canonicalAfter);

        return new WorkflowDiff(
            DiffSections(before, after),
            DiffBlocks(before, after),
            DiffVariables(before, after),
            DiffDocumentClasses(before, after))
        {
            WorkflowFields = DiffWorkflowFields(before, after),
        };
    }

    private static List<FieldDiff> DiffWorkflowFields(JObject before, JObject after)
    {
        return DiffFields(WithoutCollections(before), WithoutCollections(after), excludeTopLevel: null);
    }

    private static JObject WithoutCollections(JObject workflow)
    {
        return new JObject(workflow.Properties().Where(property =>
            property.Name is not (SECTIONS or VARIABLES or DOCUMENT_CLASSES)));
    }

    private static List<BlockDiff> DiffBlocks(JObject before, JObject after)
    {
        List<BlockEntry> beforeEntries = CollectBlocks(before);
        List<BlockEntry> afterEntries = CollectBlocks(after);
        HashSet<string> repeatedTags = [.. RepeatedTags(beforeEntries).Union(RepeatedTags(afterEntries))];
        HashSet<string> stableIdKeyedTags =
            [.. repeatedTags.Where(tag => HasUniqueStableIds(beforeEntries, tag) && HasUniqueStableIds(afterEntries, tag))];

        Dictionary<string, BlockEntry> beforeBlocks = KeyBlocks(beforeEntries, repeatedTags, stableIdKeyedTags);
        Dictionary<string, BlockEntry> afterBlocks = KeyBlocks(afterEntries, repeatedTags, stableIdKeyedTags);

        var diffs = new List<BlockDiff>();
        foreach (string key in beforeBlocks.Keys.Union(afterBlocks.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            _ = beforeBlocks.TryGetValue(key, out BlockEntry? beforeEntry);
            _ = afterBlocks.TryGetValue(key, out BlockEntry? afterEntry);

            BlockDiff? diff = DiffBlock(beforeEntry, afterEntry);
            if (diff is not null)
            {
                diffs.Add(diff);
            }
        }

        return diffs;
    }

    private static BlockDiff? DiffBlock(BlockEntry? before, BlockEntry? after)
    {
        BlockEntry entry = after ?? before ?? throw new ArgumentException("A block diff needs at least one side.");
        JObject block = entry.Block;
        string name = DisplayNameOr(block, NAME, entry.Tag);

        BlockDiff Diff(string blockType, DiffChangeKind changeKind, IReadOnlyList<FieldDiff> fieldDiffs)
        {
            return new BlockDiff(entry.Tag, name, blockType, changeKind, fieldDiffs)
            {
                IsLibraryLinked = after is not null && IsLibraryLinked(block),
                SectionName = entry.SectionName,
                StableBlockId = entry.StableBlockId,
            };
        }

        if (before is null || after is null)
        {
            return Diff(TypeOf(block), before is null ? DiffChangeKind.Added : DiffChangeKind.Removed, []);
        }

        string beforeType = TypeOf(before.Block);
        string afterType = TypeOf(after.Block);
        if (!string.Equals(beforeType, afterType, StringComparison.Ordinal))
        {
            return Diff(afterType, DiffChangeKind.TypeChanged, [new FieldDiff(TYPE, beforeType, afterType)])
                with { IsLibraryLinked = false };
        }

        List<FieldDiff> fieldDiffs = DiffFields(before.Block, after.Block, excludeTopLevel: ORDER);

        return fieldDiffs.Count > 0 ? Diff(afterType, DiffChangeKind.Modified, fieldDiffs) : null;
    }

    private static IEnumerable<string> RepeatedTags(List<BlockEntry> entries)
    {
        return entries.GroupBy(entry => entry.Tag, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);
    }

    private static bool HasUniqueStableIds(List<BlockEntry> entries, string tag)
    {
        List<BlockEntry> tagged = [.. entries.Where(entry => string.Equals(entry.Tag, tag, StringComparison.Ordinal))];

        return tagged.All(entry => entry.StableBlockId is not null)
            && tagged.Select(entry => entry.StableBlockId).Distinct(StringComparer.Ordinal).Count() == tagged.Count;
    }

    /// <summary>
    /// A tag that occurs once on both sides is its own key. A tag that repeats on either side would
    /// otherwise collapse to its last block and hide the others' changes, so its blocks are told
    /// apart by stable id when every one of them carries a unique one, else by order of occurrence.
    /// </summary>
    private static Dictionary<string, BlockEntry> KeyBlocks(
        List<BlockEntry> entries,
        HashSet<string> repeatedTags,
        HashSet<string> stableIdKeyedTags)
    {
        var keyed = new Dictionary<string, BlockEntry>(StringComparer.Ordinal);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (BlockEntry entry in entries)
        {
            if (!repeatedTags.Contains(entry.Tag))
            {
                keyed[entry.Tag] = entry;
                continue;
            }

            int occurrence = occurrences.GetValueOrDefault(entry.Tag);
            occurrences[entry.Tag] = occurrence + 1;
            string discriminator = stableIdKeyedTags.Contains(entry.Tag) && entry.StableBlockId is not null
                ? entry.StableBlockId
                : occurrence.ToString(CultureInfo.InvariantCulture);
            keyed[$"{entry.Tag}{REPEATED_TAG_KEY_SEPARATOR}{discriminator}"] = entry;
        }

        return keyed;
    }

    private static List<SectionDiff> DiffSections(JObject before, JObject after)
    {
        List<SectionEntry> beforeSections = CollectSections(before);
        List<SectionEntry> afterSections = CollectSections(after);

        var beforeMatched = new bool[beforeSections.Count];
        var afterMatched = new bool[afterSections.Count];
        var diffs = new List<SectionDiff>();

        foreach ((int beforeIndex, int afterIndex) in MatchSectionsByBlockOverlap(beforeSections, afterSections))
        {
            beforeMatched[beforeIndex] = true;
            afterMatched[afterIndex] = true;
            AddIfScalarsDiffer(diffs, beforeSections[beforeIndex], afterSections[afterIndex]);
        }

        List<int> beforeLeftover = UnmatchedIndexes(beforeMatched);
        List<int> afterLeftover = UnmatchedIndexes(afterMatched);
        int pairedLeftovers = Math.Min(beforeLeftover.Count, afterLeftover.Count);

        for (int i = 0; i < pairedLeftovers; i++)
        {
            AddIfScalarsDiffer(diffs, beforeSections[beforeLeftover[i]], afterSections[afterLeftover[i]]);
        }

        for (int i = pairedLeftovers; i < afterLeftover.Count; i++)
        {
            SectionEntry added = afterSections[afterLeftover[i]];
            diffs.Add(new SectionDiff(SectionName(added.Section), added.Order, DiffChangeKind.Added)
            {
                IsLibraryLinked = IsLibraryLinked(added.Section),
                StableSectionId = StableSectionIdOf(added.Section),
            });
        }

        for (int i = pairedLeftovers; i < beforeLeftover.Count; i++)
        {
            SectionEntry removed = beforeSections[beforeLeftover[i]];
            diffs.Add(new SectionDiff(SectionName(removed.Section), removed.Order, DiffChangeKind.Removed)
            {
                StableSectionId = StableSectionIdOf(removed.Section),
            });
        }

        return diffs;
    }

    private static void AddIfScalarsDiffer(List<SectionDiff> diffs, SectionEntry before, SectionEntry after)
    {
        List<FieldDiff> fieldDiffs = DiffFields(
            WithoutBlocks(before.Section),
            WithoutBlocks(after.Section),
            excludeTopLevel: ORDER);
        if (fieldDiffs.Count > 0)
        {
            diffs.Add(new SectionDiff(SectionName(after.Section), after.Order, DiffChangeKind.Modified)
            {
                IsLibraryLinked = IsLibraryLinked(after.Section),
                FieldDiffs = fieldDiffs,
                StableSectionId = StableSectionIdOf(after.Section),
            });
        }
    }

    private static bool IsLibraryLinked(JObject node)
    {
        return !string.IsNullOrEmpty((string?)node[SharedComponentDiffFields.ID])
            && (string?)node[SharedComponentDiffFields.LINK_STATE] == SharedComponentDiffFields.LINKED;
    }

    private static List<int> UnmatchedIndexes(bool[] matched)
    {
        return [.. Enumerable.Range(0, matched.Length).Where(i => !matched[i])];
    }

    private static IEnumerable<(int BeforeIndex, int AfterIndex)> MatchSectionsByBlockOverlap(
        List<SectionEntry> before,
        List<SectionEntry> after)
    {
        var candidates = new List<(int Overlap, int BeforeIndex, int AfterIndex)>();
        for (int i = 0; i < before.Count; i++)
        {
            if (before[i].BlockTags.Count == 0)
            {
                continue;
            }

            for (int j = 0; j < after.Count; j++)
            {
                int overlap = before[i].BlockTags.Count(tag => after[j].BlockTags.Contains(tag));
                if (overlap > 0)
                {
                    candidates.Add((overlap, i, j));
                }
            }
        }

        var beforeUsed = new bool[before.Count];
        var afterUsed = new bool[after.Count];
        var pairs = new List<(int, int)>();
        foreach ((int _, int beforeIndex, int afterIndex) in candidates
            .OrderByDescending(candidate => candidate.Overlap)
            .ThenBy(candidate => candidate.BeforeIndex)
            .ThenBy(candidate => candidate.AfterIndex))
        {
            if (beforeUsed[beforeIndex] || afterUsed[afterIndex])
            {
                continue;
            }

            beforeUsed[beforeIndex] = true;
            afterUsed[afterIndex] = true;
            pairs.Add((beforeIndex, afterIndex));
        }

        return pairs;
    }

    private static List<VariableDiff> DiffVariables(JObject before, JObject after)
    {
        Dictionary<string, JObject> beforeVars = CollectByKey(before, VARIABLES, REPLACEMENT_TAG);
        Dictionary<string, JObject> afterVars = CollectByKey(after, VARIABLES, REPLACEMENT_TAG);

        var diffs = new List<VariableDiff>();
        foreach (string tag in OrdinalUnion(beforeVars, afterVars))
        {
            bool inBefore = beforeVars.TryGetValue(tag, out JObject? beforeVar);
            bool inAfter = afterVars.TryGetValue(tag, out JObject? afterVar);

            if (inAfter && !inBefore)
            {
                diffs.Add(new VariableDiff(tag, DisplayNameOr(afterVar!, DISPLAY_NAME, tag), DiffChangeKind.Added));
            }
            else if (inBefore && !inAfter)
            {
                diffs.Add(new VariableDiff(tag, DisplayNameOr(beforeVar!, DISPLAY_NAME, tag), DiffChangeKind.Removed));
            }
            else if (DiffFields(beforeVar!, afterVar!, excludeTopLevel: null) is { Count: > 0 } fieldDiffs)
            {
                diffs.Add(new VariableDiff(tag, DisplayNameOr(afterVar!, DISPLAY_NAME, tag), DiffChangeKind.Modified)
                {
                    FieldDiffs = fieldDiffs,
                });
            }
        }

        return diffs;
    }

    private static List<DocumentClassDiff> DiffDocumentClasses(JObject before, JObject after)
    {
        Dictionary<string, JObject> beforeClasses = CollectByKey(before, DOCUMENT_CLASSES, KEY);
        Dictionary<string, JObject> afterClasses = CollectByKey(after, DOCUMENT_CLASSES, KEY);

        var diffs = new List<DocumentClassDiff>();
        foreach (string key in OrdinalUnion(beforeClasses, afterClasses))
        {
            bool inBefore = beforeClasses.TryGetValue(key, out JObject? beforeClass);
            bool inAfter = afterClasses.TryGetValue(key, out JObject? afterClass);

            if (inAfter && !inBefore)
            {
                diffs.Add(new DocumentClassDiff(key, DisplayNameOr(afterClass!, DISPLAY_NAME, key), DiffChangeKind.Added));
            }
            else if (inBefore && !inAfter)
            {
                diffs.Add(new DocumentClassDiff(key, DisplayNameOr(beforeClass!, DISPLAY_NAME, key), DiffChangeKind.Removed));
            }
            else if (DiffFields(beforeClass!, afterClass!, excludeTopLevel: null) is { Count: > 0 } fieldDiffs)
            {
                diffs.Add(new DocumentClassDiff(key, DisplayNameOr(afterClass!, DISPLAY_NAME, key), DiffChangeKind.Modified)
                {
                    FieldDiffs = fieldDiffs,
                });
            }
        }

        return diffs;
    }

    private static JObject WithoutBlocks(JObject section)
    {
        var clone = (JObject)section.DeepClone();
        _ = clone.Remove(BLOCKS);

        return clone;
    }

    private static List<FieldDiff> DiffFields(JObject before, JObject after, string? excludeTopLevel)
    {
        Dictionary<string, string?> beforeFlat = Flatten(before);
        Dictionary<string, string?> afterFlat = Flatten(after);

        if (excludeTopLevel is not null)
        {
            _ = beforeFlat.Remove(excludeTopLevel);
            _ = afterFlat.Remove(excludeTopLevel);
        }

        foreach (string identityField in _identityFields)
        {
            _ = beforeFlat.Remove(identityField);
            _ = afterFlat.Remove(identityField);
        }

        var diffs = new List<FieldDiff>();
        foreach (string path in beforeFlat.Keys.Union(afterFlat.Keys).OrderBy(p => p, StringComparer.Ordinal))
        {
            _ = beforeFlat.TryGetValue(path, out string? beforeValue);
            _ = afterFlat.TryGetValue(path, out string? afterValue);

            if (!string.Equals(beforeValue, afterValue, StringComparison.Ordinal))
            {
                diffs.Add(new FieldDiff(path, beforeValue, afterValue));
            }
        }

        return diffs;
    }

    private static Dictionary<string, string?> Flatten(JObject obj)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        FlattenInto(obj, prefix: null, result);

        return result;
    }

    private static void FlattenInto(JToken token, string? prefix, Dictionary<string, string?> into)
    {
        switch (token)
        {
            case JObject obj:
                foreach (JProperty property in obj.Properties())
                {
                    string path = prefix is null ? property.Name : $"{prefix}.{property.Name}";
                    FlattenInto(property.Value, path, into);
                }

                break;
            case JArray array:
                List<string> keys = ElementKeys(array);
                for (int i = 0; i < array.Count; i++)
                {
                    FlattenInto(array[i], $"{prefix}[{keys[i]}]", into);
                }

                break;
            default:
                into[prefix ?? string.Empty] = token.Type == JTokenType.Null ? null : token.ToString();
                break;
        }
    }

    /// <summary>
    /// Positional keys make an insert or removal read as an edit of every later element. Elements
    /// that carry their own unique name are keyed by it, so a removal is one removal.
    /// </summary>
    private static List<string> ElementKeys(JArray array)
    {
        var names = new List<string>(array.Count);
        foreach (JToken item in array)
        {
            if (item is not JObject obj
                || obj.Property(NAME, StringComparison.OrdinalIgnoreCase)?.Value.ToString() is not string name
                || name.Length == 0)
            {
                return [.. Enumerable.Range(0, array.Count).Select(index => index.ToString())];
            }

            names.Add(name);
        }

        return names.Distinct(StringComparer.Ordinal).Count() == names.Count
            ? names
            : [.. Enumerable.Range(0, array.Count).Select(index => index.ToString())];
    }

    private static List<BlockEntry> CollectBlocks(JObject workflow)
    {
        var blocks = new List<BlockEntry>();
        if (workflow[SECTIONS] is not JArray sections)
        {
            return blocks;
        }

        foreach (JToken section in sections)
        {
            if (section[BLOCKS] is not JArray sectionBlocks)
            {
                continue;
            }

            string sectionName = section is JObject sectionObject ? SectionName(sectionObject) : string.Empty;
            foreach (JToken block in sectionBlocks)
            {
                if (block is JObject blockObject)
                {
                    blocks.Add(new BlockEntry(
                        (string?)blockObject[REPLACEMENT_TAG] ?? string.Empty,
                        blockObject,
                        sectionName,
                        NullIfEmpty((string?)blockObject[STABLE_BLOCK_ID])));
                }
            }
        }

        return blocks;
    }

    private static List<SectionEntry> CollectSections(JObject workflow)
    {
        var result = new List<SectionEntry>();
        if (workflow[SECTIONS] is not JArray array)
        {
            return result;
        }

        foreach (JToken section in array)
        {
            if (section is not JObject sectionObject)
            {
                continue;
            }

            var blockTags = new HashSet<string>(StringComparer.Ordinal);
            if (sectionObject[BLOCKS] is JArray blocks)
            {
                foreach (JToken block in blocks)
                {
                    if (block is JObject blockObject)
                    {
                        _ = blockTags.Add((string?)blockObject[REPLACEMENT_TAG] ?? string.Empty);
                    }
                }
            }

            result.Add(new SectionEntry(sectionObject, (int?)sectionObject[ORDER] ?? 0, blockTags));
        }

        return result;
    }

    private static string SectionName(JObject section)
    {
        return DisplayNameOr(section, TITLE, DisplayNameOr(section, NAME, string.Empty));
    }

    private static Dictionary<string, JObject> CollectByKey(JObject workflow, string arrayName, string keyField)
    {
        var result = new Dictionary<string, JObject>(StringComparer.Ordinal);
        if (workflow[arrayName] is not JArray array)
        {
            return result;
        }

        foreach (JToken item in array)
        {
            if (item is JObject itemObject)
            {
                result[(string?)itemObject[keyField] ?? string.Empty] = itemObject;
            }
        }

        return result;
    }

    private static string? StableSectionIdOf(JObject section)
    {
        return NullIfEmpty((string?)section[STABLE_SECTION_ID]);
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string TypeOf(JObject block)
    {
        return (string?)block[TYPE] ?? string.Empty;
    }

    private static string DisplayNameOr(JObject obj, string field, string fallback)
    {
        string? value = (string?)obj[field];

        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static IEnumerable<string> OrdinalUnion(
        Dictionary<string, JObject> before,
        Dictionary<string, JObject> after)
    {
        return before.Keys.Union(after.Keys).OrderBy(k => k, StringComparer.Ordinal);
    }

    private sealed record SectionEntry(JObject Section, int Order, HashSet<string> BlockTags);

    private sealed record BlockEntry(string Tag, JObject Block, string SectionName, string? StableBlockId);
}
