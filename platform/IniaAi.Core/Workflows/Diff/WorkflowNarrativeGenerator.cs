using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Core.Workflows.Diff;

/// <summary>
/// Turns a <see cref="WorkflowDiff"/> into a deterministic, plain-language summary of what a
/// prompt engineer changed — no software-development jargon (no diff/commit/hash wording).
/// </summary>
public class WorkflowNarrativeGenerator
{
    public const string FirstVersionSummary = "First saved version";

    private const string PROMPT_TEMPLATE_MARKER = "PromptTemplate";
    private const string TYPE_FIELD = "Type";
    private const string TITLE_FIELD = "Title";
    private const string NAME_FIELD = "Name";
    private const string DESCRIPTION_FIELD = "Description";
    private const string WORKFLOW_SETTINGS_SUMMARY = "Updated workflow settings";

    public string Generate(WorkflowDiff diff)
    {
        return string.Join("; ", GenerateParts(diff));
    }

    public IReadOnlyList<string> GenerateParts(WorkflowDiff diff)
    {
        if (diff.IsEmpty)
        {
            return [];
        }

        var parts = new List<string>();

        foreach (VariableDiff variable in diff.Variables)
        {
            parts.Add(DescribeVariable(variable));
        }

        foreach (DocumentClassDiff documentClass in diff.DocumentClasses)
        {
            parts.Add(DescribeDocumentClass(documentClass));
        }

        foreach (SectionDiff section in diff.Sections)
        {
            parts.Add(DescribeSection(section));
        }

        foreach (BlockDiff block in diff.Blocks)
        {
            parts.Add(DescribeBlock(block));
        }

        parts.AddRange(DescribeWorkflowFields(diff.WorkflowFields));

        return [.. parts.Where(part => !string.IsNullOrEmpty(part))];
    }

    private static string DescribeSection(SectionDiff section)
    {
        bool named = !string.IsNullOrWhiteSpace(section.Name);

        return section.ChangeKind switch
        {
            DiffChangeKind.Added when section.IsLibraryLinked =>
                named ? $"Inserted section '{section.Name}' from the Library" : "Inserted a section from the Library",
            DiffChangeKind.Added => named ? $"Added section '{section.Name}'" : "Added a section",
            DiffChangeKind.Removed => named ? $"Removed section '{section.Name}'" : "Removed a section",
            DiffChangeKind.Modified when IsLinkOnlyChange(section.FieldDiffs) =>
                DescribeLinkChange($"section '{section.Name}'", section.FieldDiffs),
            DiffChangeKind.Modified when IsLibraryVersionAdoption(section.FieldDiffs) =>
                named
                    ? $"Updated section '{section.Name}' to the latest Library version"
                    : "Updated a section to the latest Library version",
            _ => named ? $"Updated section '{section.Name}'" : "Updated a section",
        };
    }

    private static bool IsLinkOnlyChange(IReadOnlyList<FieldDiff> fieldDiffs)
    {
        return fieldDiffs.Count > 0
            && fieldDiffs.All(f => f.FieldName.StartsWith(SharedComponentDiffFields.PREFIX, StringComparison.Ordinal));
    }

    private static bool IsLibraryVersionAdoption(IReadOnlyList<FieldDiff> fieldDiffs)
    {
        bool hashMoved = fieldDiffs.Any(f => f.FieldName == SharedComponentDiffFields.VERSION_HASH);
        bool contentChanged = fieldDiffs.Any(
            f => !f.FieldName.StartsWith(SharedComponentDiffFields.PREFIX, StringComparison.Ordinal));
        bool becameBrokenOut = fieldDiffs.Any(
            f => f.FieldName == SharedComponentDiffFields.LINK_STATE
                && f.After == SharedComponentDiffFields.BROKEN_OUT);

        return hashMoved && contentChanged && !becameBrokenOut;
    }

    private static string DescribeLinkChange(string subject, IReadOnlyList<FieldDiff> fieldDiffs)
    {
        FieldDiff? linkStateDiff = fieldDiffs.FirstOrDefault(f => f.FieldName == SharedComponentDiffFields.LINK_STATE);

        return linkStateDiff?.After switch
        {
            SharedComponentDiffFields.LINKED => $"Shared {subject} to the Library",
            SharedComponentDiffFields.BROKEN_OUT => $"Disconnected {subject} from the Library",
            _ => $"Synced {subject} with the Library",
        };
    }

    private static IEnumerable<string> DescribeWorkflowFields(IReadOnlyList<FieldDiff> fields)
    {
        var described = new List<string>();
        bool hasOtherSettings = false;

        foreach (FieldDiff field in fields)
        {
            string? phrase = TopSegment(field.FieldName) switch
            {
                TITLE_FIELD => "Changed the output title",
                NAME_FIELD => "Renamed the workflow",
                DESCRIPTION_FIELD => "Updated the description",
                _ => null,
            };

            if (phrase is null)
            {
                hasOtherSettings = true;
            }
            else if (!described.Contains(phrase))
            {
                described.Add(phrase);
            }
        }

        if (hasOtherSettings)
        {
            described.Add(WORKFLOW_SETTINGS_SUMMARY);
        }

        return described;
    }

    private static string TopSegment(string fieldName)
    {
        int dot = fieldName.IndexOf('.', StringComparison.Ordinal);

        return dot < 0 ? fieldName : fieldName[..dot];
    }

    private static string DescribeVariable(VariableDiff variable)
    {
        return variable.ChangeKind switch
        {
            DiffChangeKind.Added => $"Added input variable '{variable.DisplayName}'",
            DiffChangeKind.Removed => $"Removed input variable '{variable.DisplayName}'",
            _ => $"Changed input variable '{variable.DisplayName}'",
        };
    }

    private static string DescribeDocumentClass(DocumentClassDiff documentClass)
    {
        return documentClass.ChangeKind switch
        {
            DiffChangeKind.Added => $"Added document type '{documentClass.DisplayName}'",
            DiffChangeKind.Removed => $"Removed document type '{documentClass.DisplayName}'",
            _ => $"Changed document type '{documentClass.DisplayName}'",
        };
    }

    private static string DescribeBlock(BlockDiff block)
    {
        return block.ChangeKind switch
        {
            DiffChangeKind.Added when RefTransition(block.FieldDiffs) is { After.Length: > 0 } addedRef =>
                $"Added '{block.DisplayName}' referencing Library block '{addedRef.After}'",
            DiffChangeKind.Added when block.IsLibraryLinked => $"Inserted block '{block.DisplayName}' from the Library",
            DiffChangeKind.Added => $"Added block '{block.DisplayName}'",
            DiffChangeKind.Removed when RefTransition(block.FieldDiffs) is { Before.Length: > 0 } removedRef =>
                $"Removed '{block.DisplayName}', which referenced Library block '{removedRef.Before}'",
            DiffChangeKind.Removed => $"Removed block '{block.DisplayName}'",
            DiffChangeKind.TypeChanged => DescribeTypeChange(block),
            _ => DescribeBlockModification(block),
        };
    }

    private static FieldDiff? RefTransition(IReadOnlyList<FieldDiff> fieldDiffs)
    {
        if (!IsLinkOnlyChange(fieldDiffs)
            || fieldDiffs.Any(f => f.FieldName == SharedComponentDiffFields.LINK_STATE))
        {
            return null;
        }

        return fieldDiffs.FirstOrDefault(
            f => f.FieldName == SharedComponentDiffFields.ID
                && !string.Equals(f.Before, f.After, StringComparison.Ordinal));
    }

    private static string DescribeTypeChange(BlockDiff block)
    {
        FieldDiff? typeDiff = block.FieldDiffs.FirstOrDefault(field => field.FieldName == TYPE_FIELD);

        return $"Switched '{block.DisplayName}' from {FriendlyType(typeDiff?.Before)} to {FriendlyType(typeDiff?.After)}";
    }

    private static string DescribeBlockModification(BlockDiff block)
    {
        FieldDiff? reference = RefTransition(block.FieldDiffs);
        if (reference is not null)
        {
            if (string.IsNullOrEmpty(reference.Before))
            {
                return $"'{block.DisplayName}' now references Library block '{reference.After}'";
            }

            if (string.IsNullOrEmpty(reference.After))
            {
                return $"Inlined Library block '{reference.Before}' into '{block.DisplayName}'";
            }

            return $"'{block.DisplayName}' now references Library block '{reference.After}' (was '{reference.Before}')";
        }

        if (IsLinkOnlyChange(block.FieldDiffs))
        {
            return DescribeLinkChange($"'{block.DisplayName}'", block.FieldDiffs);
        }

        if (IsLibraryVersionAdoption(block.FieldDiffs))
        {
            return $"Updated '{block.DisplayName}' to the latest Library version";
        }

        FieldDiff? promptDiff = block.FieldDiffs
            .FirstOrDefault(field => field.FieldName.Contains(PROMPT_TEMPLATE_MARKER, StringComparison.Ordinal));

        if (promptDiff is null)
        {
            return $"Updated '{block.DisplayName}'";
        }

        int lines = ChangedLines(promptDiff.Before, promptDiff.After);

        return lines > 1
            ? $"Changed the prompt in '{block.DisplayName}' (~{lines} lines)"
            : $"Changed the prompt in '{block.DisplayName}'";
    }

    /// <summary>
    /// How many lines the change touched, not how long the prompt is: the larger of the lines
    /// only the old prompt has and the lines only the new one has.
    /// </summary>
    private static int ChangedLines(string? before, string? after)
    {
        List<string> removed = Lines(before);
        List<string> added = [];
        foreach (string line in Lines(after))
        {
            if (!removed.Remove(line))
            {
                added.Add(line);
            }
        }

        return Math.Max(removed.Count, added.Count);
    }

    private static List<string> Lines(string? text)
    {
        return string.IsNullOrEmpty(text) ? [] : [.. text.Split('\n')];
    }

    private static string FriendlyType(string? blockType)
    {
        return blockType switch
        {
            "AiQuery" => "AI query",
            "IntelligentSearch" => "web search",
            "WebApiCall" => "API call",
            "ExchangeRate" => "exchange rate",
            "DynamicChart" => "chart",
            "Merge" => "text merge",
            "MergeJson" => "JSON merge",
            "SimpleRag" => "document retrieval",
            "BuildTopicsForAllDocuments" => "topic builder",
            "CalculateNumber" => "calculation",
            "SwitchCase" => "switch",
            "StringManipulation" => "text manipulation",
            "Json2Xlsx" => "JSON to Excel",
            "LoopStart" => "loop start",
            "LoopEnd" => "loop end",
            null => "another type",
            _ => Enum.TryParse(blockType, out BlockTypes type) && BlockTypeCatalog.Find(type) is { } definition ? definition.Label : blockType,
        };
    }
}
