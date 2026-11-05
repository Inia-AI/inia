using Bunit;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.WebApp.Components.WorkflowEditor.Components;
using System.Net;
using MudPopoverProvider = MudBlazor.MudPopoverProvider;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

public class PageOverviewPromptsEditorTests : SignedInRenderTest
{
    private const string NO_USER_PROMPT = "Without a user prompt, pages get no overview.";

    [Fact]
    public async Task ASystemPromptWithoutAUserPrompt_SaysPagesGetNoOverview()
    {
        _ = await SignInAsync("editor@inia.ai", Roles.ADMIN);
        _ = Render<MudPopoverProvider>();

        IRenderedComponent<PageOverviewPromptsEditor> editor = Render<PageOverviewPromptsEditor>(p => p
            .Add(c => c.Prompts, new PageOverviewPrompts { Pages = new PageOverviewPrompt { SystemPrompt = "You summarise pages." } })
            .Add(c => c.EmptyMeaning, "Empty."));

        Assert.Contains(NO_USER_PROMPT, editor.Markup);
    }

    [Fact]
    public async Task APromptWithAUserPrompt_HasNoWarning()
    {
        _ = await SignInAsync("editor@inia.ai", Roles.ADMIN);
        _ = Render<MudPopoverProvider>();

        IRenderedComponent<PageOverviewPromptsEditor> editor = Render<PageOverviewPromptsEditor>(p => p
            .Add(c => c.Prompts, new PageOverviewPrompts { Pages = new PageOverviewPrompt { SystemPrompt = "You summarise pages.", UserPrompt = "Summarise page {0}: <answer>[summary]</answer>" } })
            .Add(c => c.EmptyMeaning, "Empty."));

        Assert.DoesNotContain(NO_USER_PROMPT, editor.Markup);
        Assert.DoesNotContain("The answer format", editor.Markup);
    }

    [Fact]
    public async Task AnAnswerFormatThatNeverClosesTheTag_IsShownUnderThePrompt()
    {
        _ = await SignInAsync("editor@inia.ai", Roles.ADMIN);
        _ = Render<MudPopoverProvider>();

        var unclosed = new PageOverviewPrompt { UserPrompt = "Summarise page {0}:\n<answer>\nPage {0}: [summary]\n<answer>" };

        IRenderedComponent<PageOverviewPromptsEditor> editor = Render<PageOverviewPromptsEditor>(p => p
            .Add(c => c.Prompts, new PageOverviewPrompts { Pages = unclosed })
            .Add(c => c.EmptyMeaning, "Empty."));

        Assert.Contains(WebUtility.HtmlEncode(unclosed.AnswerFormatProblem!), editor.Markup);
    }
}
