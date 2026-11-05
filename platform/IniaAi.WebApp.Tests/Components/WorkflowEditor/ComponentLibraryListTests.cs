using AngleSharp.Dom;
using Bunit;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.WebApp.Components.WorkflowEditor.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

public class ComponentLibraryListTests : SignedInRenderTest
{
    private const string EDITOR = "editor@inia.ai";
    private const string COMPONENT_NAME = "Net asset value reader";
    private const string RETIRED_COMPONENT_NAME = "Old net asset value reader";
    private const string EMPTY_CATEGORY = "Extraction";
    private const string USED_CATEGORY = "Utilities";
    private const string CLEAR_FILTERS = "Clear filters";

    [Fact]
    public async Task AnEmptyCategory_CountsTheOneComponentInTheSingular_AndOffersToClearIt()
    {
        IRenderedComponent<ComponentLibrary> library = await OpenLibraryAsync();

        CategoryOption(library, EMPTY_CATEGORY).Click();

        library.WaitForAssertion(() => Assert.Contains(
            "No components match the current filters. Clear them to see all 1 component.",
            Squash(library.Find("[role=status]").TextContent),
            StringComparison.Ordinal));

        Button(library, CLEAR_FILTERS).Click();

        library.WaitForAssertion(() => Assert.Equal([COMPONENT_NAME], ComponentLinks(library).Select(link => link.TextContent.Trim())));
        Assert.Equal("true", CategoryOption(library, "All components").GetAttribute("aria-selected"));
    }

    [Fact]
    public async Task TheSelectedCategory_ShowsAmongTheActiveFilters_AndItsChipClearsIt()
    {
        IRenderedComponent<ComponentLibrary> library = await OpenLibraryAsync();

        CategoryOption(library, EMPTY_CATEGORY).Click();

        IElement chip = library.WaitForElement(".library-active-filters .mud-chip");
        Assert.Contains($"Category: {EMPTY_CATEGORY}", chip.TextContent, StringComparison.Ordinal);

        library.Find(".library-active-filters .mud-chip-close-button").Click();

        library.WaitForAssertion(() => Assert.Single(ComponentLinks(library)));
        Assert.Empty(library.FindAll(".library-active-filters"));
    }

    [Fact]
    public async Task EachCategory_IsAnOptionThatSaysWhetherItIsChosen()
    {
        IRenderedComponent<ComponentLibrary> library = await OpenLibraryAsync();

        CategoryOption(library, EMPTY_CATEGORY).Click();
        library.WaitForAssertion(() => Assert.Empty(ComponentLinks(library)));

        CategoryOption(library, USED_CATEGORY).Click();

        library.WaitForAssertion(() => Assert.Single(ComponentLinks(library)));
        Assert.Equal("true", CategoryOption(library, USED_CATEGORY).GetAttribute("aria-selected"));
        Assert.Equal("false", CategoryOption(library, EMPTY_CATEGORY).GetAttribute("aria-selected"));
    }

    [Fact]
    public async Task TheKindChips_SayWhichIsChosen_AndTheToggles_SayWhetherTheirPanelIsOpen()
    {
        IRenderedComponent<ComponentLibrary> library = await OpenLibraryAsync();

        Button(library, "Blocks").Click();
        Button(library, "Filters").Click();

        library.WaitForAssertion(() => Assert.Equal("true", Button(library, "Blocks").GetAttribute("aria-pressed")));
        Assert.Equal("false", Button(library, "All").GetAttribute("aria-pressed"));
        Assert.Equal("true", Button(library, "Filters").GetAttribute("aria-expanded"));
        Assert.Equal("false", Button(library, "Suggestions").GetAttribute("aria-expanded"));
    }

    [Fact]
    public async Task ACategoryCount_IsTheNumberOfComponentsItOpens_SoARetiredOneCountsOnlyWhileShown()
    {
        IRenderedComponent<ComponentLibrary> library = await OpenLibraryAsync(withRetiredComponent: true);

        Assert.Equal("1", CountOf(library, "All components"));
        Assert.Equal("1", CountOf(library, USED_CATEGORY));

        Button(library, "Filters").Click();
        library.FindAll("label").Single(label => label.TextContent.Contains("Show retired", StringComparison.Ordinal))
            .QuerySelector("input")!.Change(true);

        library.WaitForAssertion(() => Assert.Equal("2", CountOf(library, "All components")));
        Assert.Equal("2", CountOf(library, USED_CATEGORY));
        Assert.Equal(2, ComponentLinks(library).Count);
    }

    [Fact]
    public async Task EachComponentRow_HoldsALinkToTheComponentsPage()
    {
        IRenderedComponent<ComponentLibrary> library = await OpenLibraryAsync();

        IElement link = Assert.Single(ComponentLinks(library));

        Assert.Equal(COMPONENT_NAME, link.TextContent.Trim());
        Assert.StartsWith("/library/", link.GetAttribute("href"), StringComparison.Ordinal);
    }

    private async Task<IRenderedComponent<ComponentLibrary>> OpenLibraryAsync(bool withRetiredComponent = false)
    {
        _ = await SignInAsync(EDITOR, Roles.USER, Roles.WORKFLOW_EDITOR);
        using (IServiceScope scope = Services.GetRequiredService<IServiceScopeFactory>().CreateScope())
        {
            CoreDbContext core = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            var used = new SharedComponentCategory { Name = USED_CATEGORY, DisplayOrder = 2 };
            _ = core.Add(new SharedComponentCategory { Name = EMPTY_CATEGORY, DisplayOrder = 1 });
            _ = core.Add(used);
            _ = core.Add(new SharedComponent { Kind = SharedComponentKind.Block, Name = COMPONENT_NAME, CategoryId = used.Id });
            if (withRetiredComponent)
            {
                _ = core.Add(new SharedComponent
                {
                    Kind = SharedComponentKind.Block,
                    Name = RETIRED_COMPONENT_NAME,
                    CategoryId = used.Id,
                    State = SharedComponentState.Retired,
                });
            }

            _ = await core.SaveChangesAsync();
        }

        IRenderedComponent<ComponentLibrary> library = RenderInApp<ComponentLibrary>();
        library.WaitForAssertion(() => Assert.NotEmpty(library.FindAll("[role=option]")));

        return library;
    }

    private static IElement CategoryOption(IRenderedComponent<ComponentLibrary> library, string name)
    {
        return library.FindAll("[role=option]").Single(option => option.QuerySelector("span")?.TextContent.Trim() == name);
    }

    private static string CountOf(IRenderedComponent<ComponentLibrary> library, string category)
    {
        return CategoryOption(library, category).QuerySelectorAll("span")[1].TextContent.Trim();
    }

    private static string Squash(string text)
    {
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static IReadOnlyList<IElement> ComponentLinks(IRenderedComponent<ComponentLibrary> library)
    {
        return library.FindAll("td a[href^='/library/']");
    }
}
