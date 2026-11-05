using IniaAi.Core.Workflows.Diff;

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

public enum SaveConflictChoice
{
    SaveMine,
    LoadLatest,
    KeepEditing,
}

public static class SaveConflictDisclosure
{
    public const int MAX_LINES = 12;

    public static List<string> BuildRevertLines(WorkflowDiff diff)
    {
        List<string> lines = [];
        foreach (SectionDiff section in diff.Sections)
        {
            string subject = DescribeSection(section);
            lines.Add(section.FieldDiffs.Count > 0
                ? $"{subject} — {Describe(section.ChangeKind)}: "
                    + string.Join(", ", section.FieldDiffs.Select(field => field.FieldName))
                : $"{subject} — {Describe(section.ChangeKind)}");
        }

        foreach (BlockDiff block in diff.Blocks)
        {
            lines.Add(block.FieldDiffs.Count > 0
                ? $"Block '{block.DisplayName}' — {Describe(block.ChangeKind)}: "
                    + string.Join(", ", block.FieldDiffs.Select(field => field.FieldName))
                : $"Block '{block.DisplayName}' — {Describe(block.ChangeKind)}");
        }

        foreach (VariableDiff variable in diff.Variables)
        {
            lines.Add($"Variable '{variable.DisplayName}' — {Describe(variable.ChangeKind)}");
        }

        foreach (DocumentClassDiff documentClass in diff.DocumentClasses)
        {
            lines.Add($"Document class '{documentClass.DisplayName}' — {Describe(documentClass.ChangeKind)}");
        }

        foreach (FieldDiff field in diff.WorkflowFields)
        {
            lines.Add($"Template field '{field.FieldName}' — changed");
        }

        if (lines.Count <= MAX_LINES)
        {
            return lines;
        }

        int shown = MAX_LINES - 1;
        int remainder = lines.Count - shown;
        List<string> capped = [.. lines.Take(shown)];
        capped.Add($"…and {remainder} more change{(remainder == 1 ? "" : "s")}");

        return capped;
    }

    private static string DescribeSection(SectionDiff section)
    {
        return string.IsNullOrWhiteSpace(section.Name)
            ? $"Section {section.Order + 1} (untitled)"
            : $"Section '{section.Name}'";
    }

    private static string Describe(DiffChangeKind kind)
    {
        return kind switch
        {
            DiffChangeKind.Added => "added",
            DiffChangeKind.Removed => "removed",
            DiffChangeKind.TypeChanged => "type changed",
            _ => "changed",
        };
    }
}
