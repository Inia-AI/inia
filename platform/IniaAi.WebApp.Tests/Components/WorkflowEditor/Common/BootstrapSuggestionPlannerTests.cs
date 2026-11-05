using IniaAi.Application.Queries.SharedComponents.ScanSharedComponentBootstrap;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.WebApp.Components.WorkflowEditor.Common;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Common;

public class BootstrapSuggestionPlannerTests
{
    [Fact]
    public void SplitDispatch_NoExistingComponent_PromotesFirstCheckedAndAttachesRest()
    {
        SharedComponentBootstrapSuggestion suggestion = Suggestion(existingComponentId: null);
        List<SharedComponentBootstrapNode> checkedNodes =
            BootstrapSuggestionPlanner.CheckedNodes(suggestion, new HashSet<string>(StringComparer.Ordinal));

        (SharedComponentBootstrapNode? promoteTarget, IReadOnlyList<SharedComponentBootstrapNode> attaches) =
            BootstrapSuggestionPlanner.SplitDispatch(suggestion, checkedNodes);

        Assert.NotNull(promoteTarget);
        Assert.Equal("WT-A", promoteTarget.WorkflowTypeId);
        Assert.Equal(["WT-B"], attaches.Select(node => node.WorkflowTypeId));
    }

    [Fact]
    public void SplitDispatch_ExistingComponent_AttachesEveryCheckedNode()
    {
        SharedComponentBootstrapSuggestion suggestion = Suggestion(existingComponentId: "c-kyc");
        List<SharedComponentBootstrapNode> checkedNodes =
            BootstrapSuggestionPlanner.CheckedNodes(suggestion, new HashSet<string>(StringComparer.Ordinal));

        (SharedComponentBootstrapNode? promoteTarget, IReadOnlyList<SharedComponentBootstrapNode> attaches) =
            BootstrapSuggestionPlanner.SplitDispatch(suggestion, checkedNodes);

        Assert.Null(promoteTarget);
        Assert.Equal(2, attaches.Count);
    }

    [Fact]
    public void CheckedNodes_UncheckedKeysExcludeNodes()
    {
        SharedComponentBootstrapSuggestion suggestion = Suggestion(existingComponentId: null);
        var uncheckedKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            BootstrapSuggestionPlanner.NodeKey(suggestion.Nodes[0]),
        };

        List<SharedComponentBootstrapNode> checkedNodes =
            BootstrapSuggestionPlanner.CheckedNodes(suggestion, uncheckedKeys);

        SharedComponentBootstrapNode remaining = Assert.Single(checkedNodes);
        Assert.Equal("WT-B", remaining.WorkflowTypeId);
    }

    [Fact]
    public void ActionLabel_CoversAllArms()
    {
        SharedComponentBootstrapSuggestion promote = Suggestion(existingComponentId: null);
        SharedComponentBootstrapSuggestion attach = Suggestion(existingComponentId: "c-kyc");

        Assert.Equal("Nothing selected", BootstrapSuggestionPlanner.ActionLabel(promote, 0));
        Assert.Equal("Promote to Library", BootstrapSuggestionPlanner.ActionLabel(promote, 1));
        Assert.Equal("Promote + attach 2 more", BootstrapSuggestionPlanner.ActionLabel(promote, 3));
        Assert.Equal("Attach to 'KYC Check'", BootstrapSuggestionPlanner.ActionLabel(attach, 1));
        Assert.Equal("Attach 2 to 'KYC Check'", BootstrapSuggestionPlanner.ActionLabel(attach, 2));
    }

    [Fact]
    public void DescribeLocation_BlockNode_NamesTheSectionAndTheBlock()
    {
        SharedComponentBootstrapNode node = Suggestion(existingComponentId: null).Nodes[1];

        Assert.Equal("section 1 “Diligence”, block 2 “KYC Check”", BootstrapSuggestionPlanner.DescribeLocation(node));
    }

    [Fact]
    public void DescribeLocation_SectionNode_NamesOnlyTheSection()
    {
        SharedComponentBootstrapNode node = new(
            "WT-A", "Report A", "Sections[3]", "stable-a", null, null,
            "Diligence", 3, BlockName: null, BlockOrder: null, "Analysis");

        Assert.Equal("section 3 “Diligence”", BootstrapSuggestionPlanner.DescribeLocation(node));
    }

    [Fact]
    public void DescribeLocation_BlankTitles_FallBackToReadableText()
    {
        SharedComponentBootstrapNode node = new(
            "WT-A", "Report A", "Sections[0].Blocks[1]", "stable-a", null, null,
            "   ", 0, "", 1, null);

        Assert.Equal(
            "section 0 “Untitled section”, block 1 “Untitled block”",
            BootstrapSuggestionPlanner.DescribeLocation(node));
    }

    [Fact]
    public void GroupByKind_OrdersSectionsBeforeBlocksAndDropsEmptyGroups()
    {
        IReadOnlyList<SuggestionGroup> groups = BootstrapSuggestionPlanner.GroupByKind(
            [Suggestion(existingComponentId: null), SectionSuggestion()]);

        Assert.Equal(["Sections", "Blocks"], groups.Select(group => group.Label));

        IReadOnlyList<SuggestionGroup> blocksOnly = BootstrapSuggestionPlanner.GroupByKind(
            [Suggestion(existingComponentId: null)]);

        Assert.Equal(["Blocks"], blocksOnly.Select(group => group.Label));
    }

    [Fact]
    public void Filter_NoSelections_KeepsEverything()
    {
        List<SharedComponentBootstrapSuggestion> all = [Suggestion(existingComponentId: null), SectionSuggestion()];

        IReadOnlyList<SharedComponentBootstrapSuggestion> filtered = BootstrapSuggestionPlanner.Filter(
            all, kind: null, Empty(), Empty());

        Assert.Equal(2, filtered.Count);
    }

    [Fact]
    public void Filter_ByKind_KeepsOnlyThatKind()
    {
        List<SharedComponentBootstrapSuggestion> all = [Suggestion(existingComponentId: null), SectionSuggestion()];

        IReadOnlyList<SharedComponentBootstrapSuggestion> filtered = BootstrapSuggestionPlanner.Filter(
            all, SharedComponentKind.Section, Empty(), Empty());

        Assert.Equal([SharedComponentKind.Section], filtered.Select(suggestion => suggestion.Kind));
    }

    [Fact]
    public void Filter_ByWorkflowType_KeepsSuggestionsTouchingIt()
    {
        List<SharedComponentBootstrapSuggestion> all = [Suggestion(existingComponentId: null), SectionSuggestion()];

        IReadOnlyList<SharedComponentBootstrapSuggestion> filtered = BootstrapSuggestionPlanner.Filter(
            all, kind: null, new HashSet<string>(["WT-B"], StringComparer.Ordinal), Empty());

        Assert.Single(filtered);
        Assert.Equal(SharedComponentKind.Block, filtered[0].Kind);
    }

    [Fact]
    public void Filter_ByCategory_TreatsMissingCategoryAsUncategorized()
    {
        List<SharedComponentBootstrapSuggestion> all = [Suggestion(existingComponentId: null)];

        IReadOnlyList<SharedComponentBootstrapSuggestion> filtered = BootstrapSuggestionPlanner.Filter(
            all,
            kind: null,
            Empty(),
            new HashSet<string>([BootstrapSuggestionPlanner.UNCATEGORIZED], StringComparer.Ordinal));

        Assert.Single(filtered);
    }

    [Fact]
    public void Options_CountSuggestionsPerValueAndLabelThemReadably()
    {
        List<SharedComponentBootstrapSuggestion> all = [Suggestion(existingComponentId: null), SectionSuggestion()];

        IReadOnlyList<SuggestionFilterOption> types = BootstrapSuggestionPlanner.WorkflowTypeOptions(all);
        IReadOnlyList<SuggestionFilterOption> categories = BootstrapSuggestionPlanner.CategoryOptions(all);

        Assert.Equal(["Report A", "Report B"], types.Select(option => option.Label));
        Assert.Equal([2, 1], types.Select(option => option.Count));
        Assert.Contains(categories, option => option.Label == BootstrapSuggestionPlanner.UNCATEGORIZED);
        Assert.Contains(categories, option => option.Label == "Extraction");
    }

    private static HashSet<string> Empty()
    {
        return new HashSet<string>(StringComparer.Ordinal);
    }

    private static SharedComponentBootstrapSuggestion SectionSuggestion()
    {
        return new SharedComponentBootstrapSuggestion(
            SharedComponentKind.Section,
            new string('b', 64),
            "Onboarding",
            CrossType: false,
            ExistingComponentId: null,
            ExistingComponentName: null,
            [
                new SharedComponentBootstrapNode(
                    "WT-A", "Report A", "Sections[0]", "stable-s", null, null,
                    "Onboarding", 0, BlockName: null, BlockOrder: null, "Extraction"),
            ]);
    }

    private static SharedComponentBootstrapSuggestion Suggestion(string? existingComponentId)
    {
        return new SharedComponentBootstrapSuggestion(
            SharedComponentKind.Block,
            new string('a', 64),
            "KYC Check",
            CrossType: true,
            existingComponentId,
            existingComponentId is null ? null : "KYC Check",
            [
                new SharedComponentBootstrapNode(
                    "WT-A", "Report A", "Sections[0].Blocks[0]", "stable-a", null, null,
                    "Onboarding", 0, "KYC Check", 0, "Extraction"),
                new SharedComponentBootstrapNode(
                    "WT-B", "Report B", "Sections[1].Blocks[2]", "stable-b", null, null,
                    "Diligence", 1, "KYC Check", 2, null),
            ]);
    }
}
