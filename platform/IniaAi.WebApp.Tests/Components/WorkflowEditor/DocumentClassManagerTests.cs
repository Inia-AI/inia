using Bunit;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApp.Components.WorkflowEditor.Components;
using MudPopoverProvider = MudBlazor.MudPopoverProvider;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

public class DocumentClassManagerTests : SignedInRenderTest
{
    private static readonly PageOverviewPrompts _prompts = new() { Pages = new PageOverviewPrompt { UserPrompt = "Summarise page {0}." } };

    [Fact]
    public async Task AClassWithoutAnOverviewPrompt_ThatABlockSearches_IsFlagged()
    {
        _ = await SignInAsync("editor@inia.ai", Roles.ADMIN);
        Workflow workflow = WorkflowWithASearch();
        _ = new DocumentClass(workflow, "report") { DisplayName = "Annual Report", PageOverviews = _prompts };
        _ = new DocumentClass(workflow, "deck") { DisplayName = "Presentation" };

        _ = Render<MudPopoverProvider>();
        IRenderedComponent<DocumentClassManager> manager = Render<DocumentClassManager>(p => p.Add(c => c.Workflow, workflow));

        manager.WaitForAssertion(() => Assert.Contains(
            "1 block reads page overviews, but Presentation documents get none. Give each an overview prompt, or set a default.",
            manager.Markup));
        Assert.Equal(["Own", "None"], OverviewPromptCells(manager));
    }

    [Fact]
    public async Task TheWorkflowDefault_CoversTheClassesWithoutTheirOwn()
    {
        _ = await SignInAsync("editor@inia.ai", Roles.ADMIN);
        Workflow workflow = WorkflowWithASearch();
        workflow.Configuration = new WorkflowConfiguration { PageOverviews = _prompts };
        _ = new DocumentClass(workflow, "deck") { DisplayName = "Presentation" };

        _ = Render<MudPopoverProvider>();
        IRenderedComponent<DocumentClassManager> manager = Render<DocumentClassManager>(p => p.Add(c => c.Workflow, workflow));

        manager.WaitForAssertion(() => Assert.Equal(["Workflow default"], OverviewPromptCells(manager)));
        Assert.DoesNotContain("documents get none", manager.Markup);
        Assert.Contains("Prompt: Set", manager.Markup);
    }

    [Fact]
    public async Task TheSettingColumns_UseTheEditDialogsNames_AndSayYesOrNoInWords()
    {
        _ = await SignInAsync("editor@inia.ai", Roles.ADMIN);
        Workflow workflow = WorkflowWithASearch();
        _ = new DocumentClass(workflow, "report") { DisplayName = "Annual Report", ShouldBeSingular = true };

        _ = Render<MudPopoverProvider>();
        IRenderedComponent<DocumentClassManager> manager = Render<DocumentClassManager>(p => p.Add(c => c.Workflow, workflow));

        manager.WaitForAssertion(() => Assert.Equal("Yes", manager.Find("td[data-label='Limit to one document']").TextContent.Trim()));
        Assert.Equal("No", manager.Find("td[data-label='Is for output documents']").TextContent.Trim());
        string[] headers = [.. manager.FindAll("th").Select(header => header.TextContent.Trim())];
        Assert.Contains("Limit to one document", headers);
        Assert.Contains("Is for output documents", headers);
    }

    private static Workflow WorkflowWithASearch()
    {
        var workflow = new Workflow("root");
        var section = new Section("Analysis", workflow) { Title = "Analysis" };
        Block search = new(BlockTypes.AiQuery, section, "search") { Name = "Find the revenue", Properties = new AiQueryProperties() };
        _ = new RagSettings(search) { Type = RagTypes.TopNPagesPerFilterWithTwoNeighbors };

        return workflow;
    }

    private static List<string> OverviewPromptCells(IRenderedComponent<DocumentClassManager> manager)
    {
        return [.. manager.FindAll("td[data-label='Overview prompt']").Select(cell => cell.TextContent.Trim())];
    }
}
