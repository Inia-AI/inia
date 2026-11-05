using IniaAi.WebApi.Contracts.Enums;
using IniaAi.WebApi.Contracts.Responses.SharedComponents;
using IniaAi.WebApp.Components.WorkflowEditor.State;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.State;

public class LibraryListFilterTests
{
    private static readonly SharedComponentSummaryDto[] _components =
    [
        Component("c1", SharedComponentKind.Block, "Market Data Reader", "Reads FX rates", categoryId: "extraction",
            consumingTypes: [new ConsumingWorkflowTypeDto("WT-1", "NS Testing", 1, "asset", "Asset Management")]),
        Component("c2", SharedComponentKind.Section, "Risk Summary", "Aggregates risk metrics", categoryId: "analysis",
            consumingTypes:
            [
                new ConsumingWorkflowTypeDto("WT-2", "CIS - Global", 1, "corporate", "Corporate"),
                new ConsumingWorkflowTypeDto("WT-3", "Web API Examples", 1, null, null),
            ]),
        Component("c3", SharedComponentKind.Block, "Legacy Extractor", "Old parsing block", SharedComponentState.Retired, categoryId: "extraction"),
        Component("c4", SharedComponentKind.Section, "Counterparty Overview", null),
    ];

    [Fact]
    public void Apply_NoCriteria_ReturnsActiveComponentsOnly()
    {
        var filter = new LibraryListFilter();

        IReadOnlyList<SharedComponentSummaryDto> result = filter.Apply(_components);

        Assert.Equal(["c1", "c2", "c4"], result.Select(c => c.Id));
    }

    [Fact]
    public void Apply_SearchMatchesNameCaseInsensitively()
    {
        var filter = new LibraryListFilter { SearchText = "risk" };

        IReadOnlyList<SharedComponentSummaryDto> result = filter.Apply(_components);

        Assert.Equal(["c2"], result.Select(c => c.Id));
    }

    [Fact]
    public void Apply_SearchMatchesDescription()
    {
        var filter = new LibraryListFilter { SearchText = "fx RATES" };

        IReadOnlyList<SharedComponentSummaryDto> result = filter.Apply(_components);

        Assert.Equal(["c1"], result.Select(c => c.Id));
    }

    [Fact]
    public void Apply_KindChip_FiltersByKind()
    {
        var filter = new LibraryListFilter { Kind = SharedComponentKind.Section };

        IReadOnlyList<SharedComponentSummaryDto> result = filter.Apply(_components);

        Assert.Equal(["c2", "c4"], result.Select(c => c.Id));
    }

    [Fact]
    public void Apply_ShowRetired_IncludesRetiredComponents()
    {
        var filter = new LibraryListFilter { ShowRetired = true };

        IReadOnlyList<SharedComponentSummaryDto> result = filter.Apply(_components);

        Assert.Equal(["c1", "c2", "c3", "c4"], result.Select(c => c.Id));
    }

    [Fact]
    public void Apply_CombinedCriteria_AllMustMatch()
    {
        var filter = new LibraryListFilter
        {
            SearchText = "legacy",
            Kind = SharedComponentKind.Block,
            ShowRetired = true,
        };

        IReadOnlyList<SharedComponentSummaryDto> result = filter.Apply(_components);

        Assert.Equal(["c3"], result.Select(c => c.Id));

        filter.ShowRetired = false;
        Assert.Empty(filter.Apply(_components));
    }

    [Fact]
    public void Apply_WhitespaceSearch_TreatedAsNoSearch()
    {
        var filter = new LibraryListFilter { SearchText = "   " };

        IReadOnlyList<SharedComponentSummaryDto> result = filter.Apply(_components);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Apply_CategoryFilter_KeepsOnlyThatCategory()
    {
        var filter = new LibraryListFilter { CategoryId = "extraction", ShowRetired = true };

        IReadOnlyList<SharedComponentSummaryDto> result = filter.Apply(_components);

        Assert.Equal(["c1", "c3"], result.Select(c => c.Id));
    }

    [Fact]
    public void Apply_UncategorizedOnly_KeepsComponentsWithoutCategory()
    {
        var filter = new LibraryListFilter { UncategorizedOnly = true };

        IReadOnlyList<SharedComponentSummaryDto> result = filter.Apply(_components);

        Assert.Equal(["c4"], result.Select(c => c.Id));
    }

    [Fact]
    public void Apply_ConsumingWorkflowTypeFilter_KeepsComponentsUsedByThatType()
    {
        var filter = new LibraryListFilter { ConsumingWorkflowTypeId = "WT-2" };

        IReadOnlyList<SharedComponentSummaryDto> result = filter.Apply(_components);

        Assert.Equal(["c2"], result.Select(c => c.Id));
    }

    [Fact]
    public void Apply_ConsumingWorkflowTypeCategoryFilter_KeepsComponentsUsedByTypesInThatCategory()
    {
        var filter = new LibraryListFilter { ConsumingWorkflowTypeCategoryId = "asset" };

        IReadOnlyList<SharedComponentSummaryDto> result = filter.Apply(_components);

        Assert.Equal(["c1"], result.Select(c => c.Id));
    }

    [Fact]
    public void SecondaryFilterCount_CountsTypeCategoryAndRetired_NotSearchKindOrLibraryCategory()
    {
        Assert.Equal(0, new LibraryListFilter().SecondaryFilterCount);
        Assert.Equal(0, new LibraryListFilter
        {
            SearchText = "risk",
            Kind = SharedComponentKind.Block,
            CategoryId = "extraction",
        }.SecondaryFilterCount);
        Assert.Equal(1, new LibraryListFilter { ConsumingWorkflowTypeId = "WT-1" }.SecondaryFilterCount);
        Assert.Equal(3, new LibraryListFilter
        {
            ConsumingWorkflowTypeId = "WT-1",
            ConsumingWorkflowTypeCategoryId = "asset",
            ShowRetired = true,
        }.SecondaryFilterCount);
    }

    [Fact]
    public void ActiveFilterCount_CountsEveryNarrowingTheChipRowShows()
    {
        Assert.Equal(0, new LibraryListFilter { SearchText = "  " }.ActiveFilterCount);
        Assert.Equal(1, new LibraryListFilter { CategoryId = "extraction" }.ActiveFilterCount);
        Assert.Equal(1, new LibraryListFilter { UncategorizedOnly = true }.ActiveFilterCount);
        Assert.Equal(6, new LibraryListFilter
        {
            SearchText = "risk",
            Kind = SharedComponentKind.Block,
            CategoryId = "extraction",
            ConsumingWorkflowTypeId = "WT-1",
            ConsumingWorkflowTypeCategoryId = "asset",
            ShowRetired = true,
        }.ActiveFilterCount);
    }

    [Fact]
    public void ClearAll_ResetsEveryFilter_SoTheListShowsEveryActiveComponent()
    {
        var filter = new LibraryListFilter
        {
            SearchText = "risk",
            Kind = SharedComponentKind.Section,
            CategoryId = "analysis",
            UncategorizedOnly = true,
            ConsumingWorkflowTypeId = "WT-1",
            ConsumingWorkflowTypeCategoryId = "asset",
            ShowRetired = true,
        };

        filter.ClearAll();

        Assert.Equal(0, filter.ActiveFilterCount);
        Assert.Equal(["c1", "c2", "c4"], filter.Apply(_components).Select(c => c.Id));
    }

    private static SharedComponentSummaryDto Component(
        string id,
        SharedComponentKind kind,
        string name,
        string? description,
        SharedComponentState state = SharedComponentState.Active,
        string? categoryId = null,
        List<ConsumingWorkflowTypeDto>? consumingTypes = null)
    {
        return new SharedComponentSummaryDto(
            id, kind, name, description,
            CategoryId: categoryId,
            CategoryName: null,
            State: state,
            HeadVersionId: "v1",
            HeadContentHash: new string('a', 64),
            HeadCreatedAtUtc: DateTime.UtcNow,
            ConsumerCount: 0,
            ConsumingWorkflowTypes: consumingTypes ?? []);
    }
}
