using IniaAi.Application.Queries.SharedComponents.ScanSharedComponentBootstrap;
using IniaAi.Domain.Entities.SharedComponents.Enums;

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

public sealed record SuggestionFilterOption(string Value, string Label, int Count);

public sealed record SuggestionGroup(
    SharedComponentKind Kind,
    string Label,
    IReadOnlyList<SharedComponentBootstrapSuggestion> Suggestions);

public static class BootstrapSuggestionPlanner
{
    public const string UNCATEGORIZED = "Uncategorized";

    public static IReadOnlyList<SuggestionFilterOption> WorkflowTypeOptions(
        IEnumerable<SharedComponentBootstrapSuggestion> suggestions)
    {
        return Options(suggestions, node => node.WorkflowTypeId, node => node.WorkflowTypeName);
    }

    public static IReadOnlyList<SuggestionFilterOption> CategoryOptions(
        IEnumerable<SharedComponentBootstrapSuggestion> suggestions)
    {
        return Options(suggestions, CategoryOf, CategoryOf);
    }

    public static IReadOnlyList<SharedComponentBootstrapSuggestion> Filter(
        IEnumerable<SharedComponentBootstrapSuggestion> suggestions,
        SharedComponentKind? kind,
        IReadOnlySet<string> workflowTypeIds,
        IReadOnlySet<string> categories)
    {
        return
        [
            .. suggestions.Where(suggestion =>
                (kind is null || suggestion.Kind == kind)
                && (workflowTypeIds.Count == 0
                    || suggestion.Nodes.Any(node => workflowTypeIds.Contains(node.WorkflowTypeId)))
                && (categories.Count == 0
                    || suggestion.Nodes.Any(node => categories.Contains(CategoryOf(node)))))
        ];
    }

    public static IReadOnlyList<SuggestionGroup> GroupByKind(
        IEnumerable<SharedComponentBootstrapSuggestion> suggestions)
    {
        List<SharedComponentBootstrapSuggestion> ordered = [.. suggestions];

        return
        [
            .. new[] { SharedComponentKind.Section, SharedComponentKind.Block }
                .Select(kind => new SuggestionGroup(
                    kind,
                    kind == SharedComponentKind.Section ? "Sections" : "Blocks",
                    [.. ordered.Where(suggestion => suggestion.Kind == kind)]))
                .Where(group => group.Suggestions.Count > 0)
        ];
    }

    private static string CategoryOf(SharedComponentBootstrapNode node)
    {
        return string.IsNullOrWhiteSpace(node.WorkflowTypeCategoryName)
            ? UNCATEGORIZED
            : node.WorkflowTypeCategoryName;
    }

    private static IReadOnlyList<SuggestionFilterOption> Options(
        IEnumerable<SharedComponentBootstrapSuggestion> suggestions,
        Func<SharedComponentBootstrapNode, string> value,
        Func<SharedComponentBootstrapNode, string> label)
    {
        return
        [
            .. suggestions
                .SelectMany(suggestion => suggestion.Nodes
                    .GroupBy(value, StringComparer.Ordinal)
                    .Select(nodesForValue => nodesForValue.First()))
                .GroupBy(value, StringComparer.Ordinal)
                .Select(group => new SuggestionFilterOption(group.Key, label(group.First()), group.Count()))
                .OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase)
        ];
    }

    public static string NodeKey(SharedComponentBootstrapNode node)
    {
        return $"{node.WorkflowTypeId}::{node.TargetStableId ?? node.TargetPath}";
    }

    public static string DescribeLocation(SharedComponentBootstrapNode node)
    {
        string section = $"section {node.SectionOrder} “{Fallback(node.SectionTitle, "Untitled section")}”";

        return node.BlockOrder is null
            ? section
            : $"{section}, block {node.BlockOrder} “{Fallback(node.BlockName, "Untitled block")}”";
    }

    private static string Fallback(string? value, string whenBlank)
    {
        return string.IsNullOrWhiteSpace(value) ? whenBlank : value;
    }

    public static List<SharedComponentBootstrapNode> CheckedNodes(
        SharedComponentBootstrapSuggestion suggestion,
        IReadOnlySet<string> uncheckedKeys)
    {
        return [.. suggestion.Nodes.Where(node => !uncheckedKeys.Contains(NodeKey(node)))];
    }

    public static (SharedComponentBootstrapNode? PromoteTarget, IReadOnlyList<SharedComponentBootstrapNode> AttachNodes)
        SplitDispatch(
            SharedComponentBootstrapSuggestion suggestion,
            IReadOnlyList<SharedComponentBootstrapNode> checkedNodes)
    {
        if (checkedNodes.Count == 0)
        {
            return (null, []);
        }

        if (suggestion.ExistingComponentId is not null)
        {
            return (null, checkedNodes);
        }

        return (checkedNodes[0], [.. checkedNodes.Skip(1)]);
    }

    public static string ActionLabel(SharedComponentBootstrapSuggestion suggestion, int checkedCount)
    {
        if (checkedCount == 0)
        {
            return "Nothing selected";
        }

        if (suggestion.ExistingComponentId is not null)
        {
            return checkedCount == 1
                ? $"Attach to '{suggestion.ExistingComponentName}'"
                : $"Attach {checkedCount} to '{suggestion.ExistingComponentName}'";
        }

        return checkedCount == 1
            ? "Promote to Library"
            : $"Promote + attach {checkedCount - 1} more";
    }
}
