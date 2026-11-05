using System.Security.Cryptography;
using System.Text;
using AngleSharp.Dom;
using Bunit;
using IniaAi.Application.Workflows.EditorAgent;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Domain.Entities.ReportFeedback;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Configuration.Platform;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.WebApp.Components.WorkflowEditor.Agent;
using Microsoft.Extensions.DependencyInjection;
using IniaAi.Testing.DependencyInjection;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Agent;

public sealed class EditorAgentPanelTests : EditorAgentRenderTest
{
    private const string WORKFLOW_TYPE = "WT-EditorAgentPanel";
    private const string STABLE_ID = "stable-revenue";
    private const string BLOCK_NAME = "Revenue breakdown";
    private const string QUESTION = "Where does the summary get revenue from?";
    private const decimal COST_PER_RUN = 0.03m;
    private const string WORKING = "[data-testid=editor-agent-working]";
    private const string WAITING = "[data-testid=editor-agent-waiting]";

    private readonly Workflow _workflow;
    private readonly Block _revenue;
    private bool _holdRuns;
    private byte[] _trace = Encoding.UTF8.GetBytes("""{"type":"stage","stage":"started","ts":"2026-10-06T10:00:00Z"}""" + "\n");
    private string _answer = "It reads the revenue block.";

    public EditorAgentPanelTests()
        : base(enabled: true)
    {
        _workflow = new Workflow("Credit report") { Title = "Credit report", WorkflowTypeId = WORKFLOW_TYPE };
        var section = new Section("Analysis", _workflow) { Title = "Analysis" };
        _revenue = new Block(BlockTypes.AiQuery, section, "revenue") { Name = BLOCK_NAME, StableBlockId = STABLE_ID };
        RuntimeAnswers();
    }

    [Fact]
    public async Task ASentMessage_ShowsAtOnce_WithTheWorkingStateAndStop()
    {
        _holdRuns = true;
        IRenderedComponent<EditorAgentPanel> panel = await RenderPanelAsync(_ => { });

        Send(panel, QUESTION);

        panel.WaitForAssertion(() =>
        {
            Assert.Contains(QUESTION, panel.Find(".editor-agent-message--user").TextContent);
            Assert.NotNull(panel.Find(WORKING));
            Assert.Contains(panel.FindAll("button"), button => button.TextContent.Trim() == "Stop");
        });
    }

    [Fact]
    public async Task WhileATurnRuns_TheComposerKeepsItsInput_AndStopTakesThePlaceOfSend()
    {
        const string NEXT = "And which block writes the table?";
        _holdRuns = true;
        IRenderedComponent<EditorAgentPanel> panel = await RenderPanelAsync(_ => { });
        Assert.NotNull(panel.Find(".editor-agent-composer [data-testid=editor-agent-send]"));

        Send(panel, QUESTION);

        panel.WaitForAssertion(() =>
        {
            Assert.NotNull(panel.Find(WORKING));
            Assert.NotNull(panel.Find(".editor-agent-composer textarea:not([disabled])"));
            Assert.Equal("3", panel.Find(".editor-agent-composer textarea").GetAttribute("rows"));
            IElement stop = panel.Find(".editor-agent-composer [data-testid=editor-agent-composer-stop]");
            Assert.Contains("editor-agent-send", stop.ClassName, StringComparison.Ordinal);
            Assert.Empty(panel.FindAll(".editor-agent-composer [data-testid=editor-agent-send]"));
        });

        await panel.InvokeAsync(() => panel.Find(".editor-agent-composer textarea").Input(NEXT));
        IElement send = panel.Find(".editor-agent-composer [data-testid=editor-agent-send]");
        Assert.Contains("editor-agent-send", send.ClassName, StringComparison.Ordinal);
        Assert.Empty(panel.FindAll(".editor-agent-composer [data-testid=editor-agent-composer-stop]"));

        await panel.InvokeAsync(() => panel.Find(".editor-agent-composer textarea").Input(string.Empty));
        await panel.InvokeAsync(() => panel.Find(".editor-agent-composer [data-testid=editor-agent-composer-stop]").Click());

        panel.WaitForAssertion(() =>
        {
            Assert.Contains("Stopped.", panel.Markup);
            Assert.NotNull(panel.Find(".editor-agent-composer [data-testid=editor-agent-send]"));
            Assert.NotNull(panel.Find(".editor-agent-composer textarea:not([disabled])"));
        }, TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// While the agent works its steps show open, each a plain line, the latest one running and
    /// its remarks between them; once the answer arrives they fold into one quiet line.
    /// </summary>
    [Fact]
    public async Task WhileTheAgentWorks_ItsStepsShowLive_AndFoldIntoOneLineWhenTheAnswerArrives()
    {
        const string WF = "python3 \"$SKILL_ASSETS_DIR/workflow-guide/wf.py\"";
        _trace = Encoding.UTF8.GetBytes(string.Join('\n',
        [
            """{"uuid":"s1","at":"2026-10-06T10:00:00Z","type":"stage","event":"run.started"}""",
            ToolLine("t1", "2026-10-06T10:00:02Z", "cat docs/outline.txt"),
            """{"uuid":"m1","at":"2026-10-06T10:00:03Z","type":"message","message":{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"Checking the revenue prompt for a length rule."}]}}}""",
            ToolLine("t2", "2026-10-06T10:00:05Z", WF + " find \"word limit\""),
            ToolLine("t3", "2026-10-06T10:00:06Z", WF + " block revenue"),
        ]) + "\n");
        _holdRuns = true;
        IRenderedComponent<EditorAgentPanel> panel = await RenderPanelAsync(_ => { });

        Send(panel, QUESTION);

        panel.WaitForAssertion(() =>
        {
            IElement trail = panel.Find(WORKING + " [data-testid=editor-agent-trail]");
            Assert.True(trail.HasAttribute("open"));
            List<IElement> steps = [.. panel.FindAll(WORKING + " [data-testid=editor-agent-step]")];
            Assert.Equal(
                ["Read the outline of the workflow", "Searched the prompts for \"word limit\"", $"Opened {BLOCK_NAME}"],
                steps.Select(static step => step.TextContent.Trim()));
            Assert.Equal("running", steps[^1].GetAttribute("data-state"));
            Assert.Equal("done", steps[0].GetAttribute("data-state"));
            Assert.Equal("Checking the revenue prompt for a length rule.", panel.Find(WORKING + " [data-testid=editor-agent-remark]").TextContent.Trim());
        }, TimeSpan.FromSeconds(30));
        Assert.DoesNotContain("wf.py", panel.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("docs/", panel.Markup, StringComparison.Ordinal);

        _holdRuns = false;

        panel.WaitForAssertion(() =>
        {
            Assert.Empty(panel.FindAll(WORKING));
            IElement trail = panel.Find(".editor-agent-message--agent [data-testid=editor-agent-trail]");
            Assert.False(trail.HasAttribute("open"));
            Assert.Equal("3 steps", trail.QuerySelector("summary")!.TextContent.Trim());
            Assert.NotNull(panel.Find(".editor-agent-answer"));
        }, TimeSpan.FromSeconds(30));
    }

    private static string ToolLine(string id, string at, string command)
    {
        string input = System.Text.Json.JsonSerializer.Serialize(new { command });
        return System.Text.Json.JsonSerializer.Serialize(new { uuid = id, at, type = "tool_call", toolName = "Bash", inputTruncated = input });
    }

    [Fact]
    public async Task AFirstMessage_NeverSaysItIsWaitingForAnEarlierAnswer()
    {
        _holdRuns = true;
        IRenderedComponent<EditorAgentPanel> panel = await RenderPanelAsync(_ => { });
        bool saidWaiting = false;
        panel.OnMarkupUpdated += (_, _) => saidWaiting |= panel.Markup.Contains("Waiting to start", StringComparison.Ordinal);

        Send(panel, QUESTION);

        panel.WaitForAssertion(() => Assert.Single(Runtime.StartRequests));
        _ = panel.WaitForElement(WORKING);
        Assert.False(saidWaiting);
    }

    [Fact]
    public async Task ABlockLinkToABlockWithoutChanges_ShowsItsInstructionsAndModelSettings()
    {
        _revenue.Properties = new AiQueryProperties
        {
            UserPromptTemplate = "Break revenue down by segment.",
            Effort = ModelEffort.High,
            ShouldUseThinkingMode = true,
            ThinkingBudget = 2048,
            ShouldUseAdvancedModel = true,
        };
        _answer = $"The summary reads [{BLOCK_NAME}](block:{STABLE_ID}).";
        IRenderedComponent<EditorAgentPanel> panel = await RenderPanelAsync(_ => { });

        Send(panel, QUESTION);
        IElement link = panel.WaitForElement(".editor-agent-answer a[data-block-id]");
        await panel.InvokeAsync(() => panel.Instance.OpenBlockLinkAsync(link.GetAttribute("data-block-id")!));

        IElement drawer = panel.WaitForElement("[data-testid=editor-agent-detail]");
        Assert.DoesNotContain("has not proposed changes", drawer.TextContent);
        Assert.Equal("Break revenue down by segment.", drawer.QuerySelector("[data-testid=editor-agent-block-instructions]")!.TextContent.Trim());
        string model = drawer.QuerySelector("[data-testid=editor-agent-block-model]")!.TextContent;
        Assert.Contains("The advanced model (the default)", model);
        Assert.Contains("High", model);
        Assert.Contains("On, up to 2,048 tokens", model);
        Assert.Contains(drawer.QuerySelectorAll("button"), button => button.TextContent.Trim() == "Open in editor");
    }

    [Fact]
    public async Task Stop_EndsTheTurn_AndTheCostShowsOnlyAsTheConversationTotal()
    {
        _holdRuns = true;
        IRenderedComponent<EditorAgentPanel> panel = await RenderPanelAsync(_ => { });
        Send(panel, QUESTION);
        panel.WaitForAssertion(() => Assert.Single(Runtime.StartRequests));

        _ = panel.WaitForElement(WORKING);
        await panel.InvokeAsync(() => panel.Find(WORKING).QuerySelectorAll("button").Single(button => button.TextContent.Trim() == "Stop").Click());

        panel.WaitForAssertion(() => Assert.Contains("Stopped.", panel.Markup));
        panel.WaitForAssertion(() => Assert.Contains("$0.03", panel.Find("[data-testid=editor-agent-total]").TextContent));
        Assert.DoesNotContain("$0.03", panel.Find(".editor-agent-scroll").TextContent);
    }

    [Fact]
    public async Task ABlockLinkInTheAnswer_ShowsTheBlock_OpensItInTheEditor_AndNothingElseInTheAnswerIsLive()
    {
        _answer = $"The summary reads [{BLOCK_NAME}](block:{STABLE_ID}). See [the site](https://example.com/x) "
            + "![chart](https://example.com/chart.png) <img src=x onerror=alert(1)>";
        Block? opened = null;
        IRenderedComponent<EditorAgentPanel> panel = await RenderPanelAsync(p => p.Add(c => c.OpenBlock, block => opened = block));

        Send(panel, QUESTION);
        IElement link = panel.WaitForElement(".editor-agent-answer a[data-block-id]");
        await panel.InvokeAsync(() => panel.Instance.OpenBlockLinkAsync(link.GetAttribute("data-block-id")!));
        IElement drawer = panel.WaitForElement("[data-testid=editor-agent-detail]");
        Assert.Contains(BLOCK_NAME, drawer.TextContent);
        Assert.Null(opened);
        drawer.QuerySelectorAll("button").Single(button => button.TextContent.Trim() == "Open in editor").Click();

        Assert.Equal(BLOCK_NAME, link.TextContent.Trim());
        Assert.Same(_revenue, opened);
        Assert.Empty(panel.FindAll("[data-testid=editor-agent-detail]"));
        IElement answer = panel.Find(".editor-agent-answer");
        Assert.Single(answer.QuerySelectorAll("a"));
        Assert.Empty(answer.QuerySelectorAll("img"));
        Assert.Contains("the site", answer.TextContent);
        Assert.Contains("<img src=x onerror=alert(1)>", answer.TextContent);
    }

    public static TheoryData<string> EveryRoleButTheEditors => new(TestServiceProviderBuilder.EveryDefinedRole.Where(role => !Roles.EDITOR_ROLES.Split(',').Contains(role)));

    [Theory]
    [MemberData(nameof(EveryRoleButTheEditors))]
    public async Task AUserWithoutTheEditorRoles_CannotUseThePanel(string role)
    {
        User user = await SignInAsync("monitor@inia.ai", role);

        IRenderedComponent<EditorAgentPanel> panel = RenderInApp<EditorAgentPanel>(parameters => parameters
            .Add(c => c.OrganizationId, ORGANIZATION_ID)
            .Add(c => c.UserId, user.Id)
            .Add(c => c.WorkflowTypeId, WORKFLOW_TYPE)
            .Add(c => c.CurrentWorkflow, () => _workflow));

        panel.WaitForAssertion(() => Assert.Contains("The workflow agent is for workflow editors.", panel.Markup));
        Assert.NotNull(panel.Find("textarea").GetAttribute("disabled"));
        Assert.True(panel.FindAll("button").Single(button => button.TextContent.Trim() == "Send").HasAttribute("disabled"));
        Assert.Empty(Runtime.StartRequests);
    }

    [Fact]
    public async Task ASecondMessageWhileOneRuns_WaitsInTheConversation_AndStartsWhenTheFirstFinishes()
    {
        const string NEXT = "And which block writes the table?";
        _holdRuns = true;
        IRenderedComponent<EditorAgentPanel> panel = await RenderPanelAsync(_ => { });
        Send(panel, QUESTION);
        _ = panel.WaitForElement(WORKING);

        Send(panel, NEXT);

        panel.WaitForAssertion(() =>
        {
            IElement waiting = panel.Find(WAITING);
            Assert.Contains("Waiting to start", waiting.TextContent);
            Assert.Contains(panel.FindAll(".editor-agent-message--user"), message => message.TextContent.Contains(NEXT));
            Assert.Single(Runtime.StartRequests);
        });

        _holdRuns = false;

        panel.WaitForAssertion(() =>
        {
            Assert.Empty(panel.FindAll(WAITING));
            Assert.Empty(panel.FindAll(WORKING));
            Assert.Equal(2, panel.FindAll(".editor-agent-answer").Count);
            Assert.Equal(2, Runtime.StartRequests.Count);
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task OnceTodaysSpendIsUsed_SendWarns_ContinueUnlocksABatchAndSends()
    {
        IRenderedComponent<MudBlazor.MudDialogProvider> dialogs = Render<MudBlazor.MudDialogProvider>();
        IRenderedComponent<EditorAgentPanel> panel = await RenderPanelAsync(_ => { });
        await SeedEditorSpendAsync(new EditorAgentSettings().DailySpendBatchUsd);

        Send(panel, QUESTION);

        dialogs.WaitForAssertion(() => Assert.Contains(EditorAgentPanel.DAILY_SPEND_TITLE, dialogs.Markup));
        Assert.Contains("is $50, past its daily $50", dialogs.Markup);
        Assert.Empty(Runtime.StartRequests);

        await dialogs.InvokeAsync(() => dialogs.FindAll("button")
            .Single(button => button.TextContent.Trim() == EditorAgentPanel.DAILY_SPEND_UNLOCK).Click());

        panel.WaitForAssertion(() => Assert.Single(Runtime.StartRequests));
        using IServiceScope scope = Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        Assert.Single(scope.ServiceProvider.GetRequiredService<UserDataDbContext>().EditorAgentSpendUnlocks);
    }

    [Fact]
    public async Task OnceTodaysSpendIsUsed_NotNow_KeepsTheMessageInTheBox_AndSendsNothing()
    {
        IRenderedComponent<MudBlazor.MudDialogProvider> dialogs = Render<MudBlazor.MudDialogProvider>();
        IRenderedComponent<EditorAgentPanel> panel = await RenderPanelAsync(_ => { });
        await SeedEditorSpendAsync(new EditorAgentSettings().DailySpendBatchUsd);

        Send(panel, QUESTION);
        dialogs.WaitForAssertion(() => Assert.Contains(EditorAgentPanel.DAILY_SPEND_TITLE, dialogs.Markup));
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").Single(button => button.TextContent.Trim() == EditorAgentPanel.DAILY_SPEND_NOT_NOW).Click());

        panel.WaitForAssertion(() => Assert.Equal(QUESTION, panel.Find("textarea").GetAttribute("value") ?? panel.Find("textarea").TextContent));
        Assert.Empty(Runtime.StartRequests);
    }

    [Fact]
    public async Task UseFeedback_ShowsTheOpenCount_ChoosingAttachesToTheNextMessage_WhichSendsOnlyTheChosen()
    {
        IRenderedComponent<MudBlazor.MudDialogProvider> dialogs = Render<MudBlazor.MudDialogProvider>();
        IRenderedComponent<EditorAgentPanel> panel = await RenderPanelAsync(_ => { });
        Assert.Empty(panel.FindAll("[data-testid=editor-agent-feedback-button]"));
        string chosen = await SeedFeedbackAsync("The total should be in millions.");
        string other = await SeedFeedbackAsync("The segment table leaves out services.");
        await panel.Instance.RefreshAsync();
        panel.WaitForAssertion(() => Assert.Equal("Use feedback (2)", panel.Find("[data-testid=editor-agent-feedback-button]").TextContent.Trim()));

        await panel.InvokeAsync(() => panel.Find("[data-testid=editor-agent-feedback-button]").Click());
        dialogs.WaitForAssertion(() => Assert.Equal(2, dialogs.FindAll("[data-testid=editor-agent-feedback-row]").Count));
        Assert.True(dialogs.Find("[data-testid=editor-agent-feedback-use]").HasAttribute("disabled"));
        await dialogs.InvokeAsync(() => dialogs.Find($"[data-feedback-id='{chosen}'] .editor-agent-report-row-text").Click());
        await dialogs.InvokeAsync(() => dialogs.Find("[data-testid=editor-agent-feedback-use]").Click());

        panel.WaitForAssertion(() => Assert.Equal("1 feedback item", panel.Find("[data-testid=editor-agent-feedback-chip]").TextContent.Trim()));
        Assert.Empty(Runtime.StartRequests);
        Assert.Equal(EditorAgentPanel.USE_FEEDBACK_MESSAGE, panel.Find("textarea").GetAttribute("value") ?? panel.Find("textarea").TextContent);

        await panel.InvokeAsync(() => panel.Find("[data-testid=editor-agent-send]").Click());

        panel.WaitForAssertion(() =>
        {
            AgentRunStartRequest run = Assert.Single(Runtime.StartRequests);
            string list = Encoding.UTF8.GetString(run.Documents.Single(document => document.Name == EditorAgentFeedback.DOCUMENT).Content.Span);
            Assert.Contains(chosen, list);
            Assert.DoesNotContain(other, list);
            Assert.Contains(EditorAgentPanel.USE_FEEDBACK_MESSAGE, panel.Find(".editor-agent-message--user").TextContent);
            Assert.Contains("1 feedback item", panel.Find("[data-testid=editor-agent-sent-feedback]").TextContent);
            Assert.Empty(panel.FindAll("[data-testid=editor-agent-feedback-chip]"));
        });

        await SetFeedbackStatusAsync(chosen, ReportFeedbackStatus.Addressed);
        await SetFeedbackStatusAsync(other, ReportFeedbackStatus.Dismissed);
        await panel.Instance.RefreshAsync();
        panel.WaitForAssertion(() => Assert.Empty(panel.FindAll("[data-testid=editor-agent-feedback-button]")));
    }

    [Fact]
    public async Task ASuggestedDismissal_ShowsTheCommentAndReason_AndDismissDismissesIt()
    {
        const string REASON = "The source document itself shows the figure this way.";
        IRenderedComponent<MudBlazor.MudDialogProvider> dialogs = Render<MudBlazor.MudDialogProvider>();
        IRenderedComponent<EditorAgentPanel> panel = await RenderPanelAsync(_ => { });
        string item = await SeedFeedbackAsync("The total looks wrong.");
        byte[] dismiss = Encoding.UTF8.GetBytes($$"""{"dismiss": [{"id": "{{item}}", "reason": "{{REASON}}"}]}""");
        Runtime.OnListRunFiles = _ => ErrorOr.ErrorOrFactory.From<IReadOnlyList<AgentRunFileEntry>>(
        [
            new AgentRunFileEntry
            {
                Path = AgentRunFilePaths.ARTIFACT_PREFIX + EditorAgentFeedback.DISMISS_FILE,
                SizeBytes = dismiss.LongLength,
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(dismiss)),
                Kind = AgentRunFileKind.Artifact,
            },
        ]);
        Runtime.OnGetRunFile = (_, _) => dismiss;
        await panel.Instance.RefreshAsync();
        _ = panel.WaitForElement("[data-testid=editor-agent-feedback-button]");

        await panel.InvokeAsync(() => panel.Find("[data-testid=editor-agent-feedback-button]").Click());
        dialogs.WaitForAssertion(() => Assert.Single(dialogs.FindAll("[data-testid=editor-agent-feedback-row]")));
        await dialogs.InvokeAsync(() => dialogs.Find("[data-testid=editor-agent-feedback-all] .editor-agent-report-row-text").Click());
        await dialogs.InvokeAsync(() => dialogs.Find("[data-testid=editor-agent-feedback-use]").Click());
        _ = panel.WaitForElement("[data-testid=editor-agent-feedback-chip]");
        await panel.InvokeAsync(() => panel.Find("[data-testid=editor-agent-send]").Click());

        panel.WaitForAssertion(() =>
        {
            IElement suggestion = panel.Find("[data-testid=editor-agent-dismissal]");
            Assert.Contains("The total looks wrong.", suggestion.TextContent);
            Assert.Contains(REASON, suggestion.TextContent);
        });

        await panel.InvokeAsync(() => panel.Find("[data-testid=editor-agent-dismiss]").Click());

        panel.WaitForAssertion(() => Assert.Equal("Dismissed", panel.Find("[data-testid=editor-agent-dismissal-status]").TextContent.Trim()));
        using IServiceScope scope = Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        ReportFeedbackItem dismissed = scope.ServiceProvider.GetRequiredService<UserDataDbContext>().ReportFeedbackItems.Single(row => row.Id == item);
        Assert.Equal((ReportFeedbackStatus.Dismissed, REASON), (dismissed.Status, dismissed.DismissReason));
    }

    /// <summary>
    /// Deleting a chosen item in "Choose feedback" asks first, removes the row and the database
    /// row, and the panel's count and chip drop it even when the dialog is then cancelled.
    /// </summary>
    [Fact]
    public async Task DeletingFeedbackInTheDialog_AsksFirst_ThenRemovesTheRow_AndThePanelCountsDropIt()
    {
        IRenderedComponent<MudBlazor.MudDialogProvider> dialogs = Render<MudBlazor.MudDialogProvider>();
        IRenderedComponent<EditorAgentPanel> panel = await RenderPanelAsync(_ => { });
        string doomed = await SeedFeedbackAsync("The total should be in millions.");
        _ = await SeedFeedbackAsync("The segment table leaves out services.");
        await panel.Instance.RefreshAsync();
        panel.WaitForAssertion(() => Assert.Equal("Use feedback (2)", panel.Find("[data-testid=editor-agent-feedback-button]").TextContent.Trim()));
        await panel.InvokeAsync(() => panel.Find("[data-testid=editor-agent-feedback-button]").Click());
        dialogs.WaitForAssertion(() => Assert.Equal(2, dialogs.FindAll("[data-testid=editor-agent-feedback-row]").Count));
        await dialogs.InvokeAsync(() => dialogs.Find("[data-testid=editor-agent-feedback-all] .editor-agent-report-row-text").Click());
        await dialogs.InvokeAsync(() => dialogs.Find("[data-testid=editor-agent-feedback-use]").Click());
        panel.WaitForAssertion(() => Assert.Equal("2 feedback items", panel.Find("[data-testid=editor-agent-feedback-chip]").TextContent.Trim()));

        await panel.InvokeAsync(() => panel.Find("[data-testid=editor-agent-feedback-button]").Click());
        dialogs.WaitForAssertion(() => Assert.Equal(2, dialogs.FindAll("[data-testid=editor-agent-feedback-row]").Count));
        IElement delete = dialogs.Find($"[data-feedback-id='{doomed}'] [data-testid=editor-agent-feedback-delete]");
        Assert.Equal(EditorAgentFeedbackDialog.DELETE_LABEL, delete.GetAttribute("aria-label"));
        await dialogs.InvokeAsync(() => delete.Click());
        dialogs.WaitForAssertion(() => Assert.Contains(EditorAgentFeedbackDialog.DELETE_CONFIRM_TITLE, dialogs.Markup));
        Assert.Equal(2, CountFeedbackRows());
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").Single(button => button.TextContent.Trim() == EditorAgentFeedbackDialog.DELETE_CONFIRM_YES).Click());

        dialogs.WaitForAssertion(() =>
        {
            Assert.Single(dialogs.FindAll("[data-testid=editor-agent-feedback-row]"));
            Assert.Empty(dialogs.FindAll($"[data-feedback-id='{doomed}']"));
            Assert.Equal("1 of 30 chosen", dialogs.Find("[data-testid=editor-agent-feedback-count]").TextContent.Trim());
        });
        Assert.Equal(1, CountFeedbackRows());
        panel.WaitForAssertion(() =>
        {
            Assert.Equal("Use feedback (1)", panel.Find("[data-testid=editor-agent-feedback-button]").TextContent.Trim());
            Assert.Equal("1 feedback item", panel.Find("[data-testid=editor-agent-feedback-chip]").TextContent.Trim());
        });

        await dialogs.InvokeAsync(() => dialogs.FindAll("button").Single(button => button.TextContent.Trim() == "Cancel").Click());
        panel.WaitForAssertion(() => Assert.Equal("1 feedback item", panel.Find("[data-testid=editor-agent-feedback-chip]").TextContent.Trim()));
    }

    private int CountFeedbackRows()
    {
        using IServiceScope scope = Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        return scope.ServiceProvider.GetRequiredService<UserDataDbContext>().ReportFeedbackItems.Count();
    }

    /// <summary>An open feedback item on a report of the panel's workflow type in the signed-in organization.</summary>
    private async Task<string> SeedFeedbackAsync(string comment)
    {
        using IServiceScope scope = Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        var execution = new WorkflowExecution
        {
            WorkflowTypeId = WORKFLOW_TYPE,
            OrganizationId = ORGANIZATION_ID,
            Title = "Credit report",
            WorkflowState = WorkflowState.Draft,
        };
        var item = ReportFeedbackItem.Create(
            ORGANIZATION_ID, execution.Id, WORKFLOW_TYPE, quotedText: null, ReportFeedbackCategory.WrongFigure, comment, "reader");
        _ = context.WorkflowExecutions.Add(execution);
        _ = context.ReportFeedbackItems.Add(item);
        _ = await context.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);
        return item.Id;
    }

    private async Task SetFeedbackStatusAsync(string id, ReportFeedbackStatus status)
    {
        using IServiceScope scope = Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        context.ReportFeedbackItems.Single(row => row.Id == id).SetStatus(status, dismissReason: null, "reviewer");
        _ = await context.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);
    }

    private async Task SeedEditorSpendAsync(decimal costUsd)
    {
        using IServiceScope scope = Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        _ = context.TokenUsages.Add(new TokenUsage
        {
            OrganizationId = ORGANIZATION_ID,
            UsageType = UsageType.LlmEditorAgentChat,
            ModelId = SONNET,
            Components = [new TokenUsageComponent { ComponentType = PricingComponentType.AgentRunSpend, Units = 1, Cost = costUsd }],
        });
        _ = await context.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheModelPicker_ShowsTheDefaultModelsPrice()
    {
        using IServiceScope scope = Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        CoreDbContext core = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var pricing = new ModelPricing
        {
            Provider = "aws-bedrock",
            ModelId = "claude-sonnet-5",
            DisplayName = "Claude Sonnet 5 (AWS Bedrock)",
            PricingType = PricingType.Llm,
            IsActive = true,
        };
        pricing.Components.Add(new ModelPricingComponent { ComponentType = PricingComponentType.Input, Cost = 3m, Unit = PricingUnit.PerMillion });
        pricing.Components.Add(new ModelPricingComponent { ComponentType = PricingComponentType.Output, Cost = 15m, Unit = PricingUnit.PerMillion });
        _ = core.Add(pricing);
        _ = await core.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        IRenderedComponent<EditorAgentPanel> panel = await RenderPanelAsync(_ => { });

        OpenModelMenu(panel);

        Popovers.WaitForAssertion(() => Assert.Contains(
            Popovers.FindAll("[data-testid=editor-agent-model-option]"),
            option => option.TextContent.Contains("$3.00 reading, $15.00 writing, per million tokens", StringComparison.Ordinal)
                && option.QuerySelector(".editor-agent-model-name")!.TextContent.Trim() == "Claude Sonnet 5 · Recommended"));
        Assert.Equal("Claude Sonnet 5", panel.Find("[data-testid=editor-agent-model-button]").TextContent.Trim());
    }

    /// <summary>
    /// The open conversation is on a model that is not the default; New conversation starts the
    /// next one on the default and does not carry the old model over.
    /// </summary>
    [Fact]
    public async Task NewConversation_StartsOnTheDefaultModel_NotTheOldConversationsModel()
    {
        User user = await SignInAsync("engineer@inia.ai", Roles.WORKFLOW_EDITOR);
        await SeedWorkflowTypeAsync(WORKFLOW_TYPE);
        IWorkflowEditorAgentService service = Services.GetRequiredService<IWorkflowEditorAgentService>();
        _ = await service.StartNewConversationAsync(
            ORGANIZATION_ID, user.Id, WORKFLOW_TYPE, HAIKU, Xunit.TestContext.Current.CancellationToken);
        IRenderedComponent<EditorAgentPanel> panel = RenderInApp<EditorAgentPanel>(parameters => parameters
            .Add(c => c.OrganizationId, ORGANIZATION_ID)
            .Add(c => c.UserId, user.Id)
            .Add(c => c.WorkflowTypeId, WORKFLOW_TYPE)
            .Add(c => c.CurrentWorkflow, () => _workflow));
        _ = panel.WaitForElement("textarea:not([disabled])");
        string before = panel.Find("[data-testid=editor-agent-model-button]").TextContent.Trim();

        await panel.InvokeAsync(() => panel.FindAll("button").Single(button => button.TextContent.Trim() == "New conversation").Click());

        EditorAgentConversationView fresh = (await service.GetOrStartConversationAsync(
            ORGANIZATION_ID, user.Id, WORKFLOW_TYPE, Xunit.TestContext.Current.CancellationToken)).Value;
        Assert.Equal(SONNET, fresh.ModelId);
        panel.WaitForAssertion(() => Assert.NotEqual(before, panel.Find("[data-testid=editor-agent-model-button]").TextContent.Trim()));
    }

    private async Task<IRenderedComponent<EditorAgentPanel>> RenderPanelAsync(
        Action<ComponentParameterCollectionBuilder<EditorAgentPanel>> extra)
    {
        User user = await SignInAsync("engineer@inia.ai", Roles.WORKFLOW_EDITOR);
        await SeedWorkflowTypeAsync(WORKFLOW_TYPE);

        IRenderedComponent<EditorAgentPanel> panel = RenderInApp<EditorAgentPanel>(parameters =>
        {
            _ = parameters
                .Add(c => c.OrganizationId, ORGANIZATION_ID)
                .Add(c => c.UserId, user.Id)
                .Add(c => c.WorkflowTypeId, WORKFLOW_TYPE)
                .Add(c => c.CurrentWorkflow, () => _workflow);
            extra(parameters);
        });
        _ = panel.WaitForElement("textarea:not([disabled])");

        return panel;
    }

    private static void OpenModelMenu(IRenderedComponent<EditorAgentPanel> panel)
    {
        panel.FindAll("button").Single(button => button.GetAttribute("aria-label")?.StartsWith("Model:", StringComparison.Ordinal) == true).Click();
    }

    private static void Send(IRenderedComponent<EditorAgentPanel> panel, string text)
    {
        // Live progress re-renders the panel at any moment: find and act inside one dispatch.
        panel.InvokeAsync(() => panel.Find("textarea").Input(text)).GetAwaiter().GetResult();
        panel.InvokeAsync(() => panel.FindAll("button").Single(button => button.TextContent.Trim() == "Send").Click())
            .GetAwaiter().GetResult();
    }

    private void RuntimeAnswers()
    {
        Runtime.OnStartRun = request => Running(request.ExecutionId);
        Runtime.OnGetRun = id => _holdRuns
            ? Running(id)
            : new AgentRunMonitor
            {
                ExecutionId = id,
                Status = AgentRunStatus.Succeeded,
                EstimatedCostUsd = COST_PER_RUN,
                Result = new AgentRunResult(_answer, "session"),
            };
        Runtime.OnCancelRun = id =>
        {
            _holdRuns = false;
            return new AgentRunMonitor { ExecutionId = id, Status = AgentRunStatus.Cancelled, EstimatedCostUsd = COST_PER_RUN };
        };
        Runtime.OnCompleteRun = id => new AgentRunMonitor { ExecutionId = id, Status = AgentRunStatus.Succeeded };
        Runtime.OnListRunFiles = _ => ErrorOr.ErrorOrFactory.From<IReadOnlyList<AgentRunFileEntry>>(
        [
            new AgentRunFileEntry
            {
                Path = "trace/trace.ndjson",
                SizeBytes = _trace.LongLength,
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(_trace)),
                Kind = AgentRunFileKind.Trace,
            },
        ]);
        Runtime.OnGetRunFile = (_, _) => _trace;
        Runtime.OnGetRunCheckpoint = id =>
        {
            string stamp = $$"""{"bundleFormat":"1","sdk":"2.0.44","model":"{{SONNET}}","run":"{{id}}"}""";
            byte[] bundle = IniaAi.Domain.Serialization.WorkflowJsonCompression.Compress(
                $$"""{"sessionId":"s","files":{},"version":{{stamp}}}""");
            return new AgentRunCheckpoint(bundle, Convert.ToHexStringLower(SHA256.HashData(bundle)));
        };
    }

    private static AgentRunMonitor Running(string id)
    {
        return new AgentRunMonitor
        {
            ExecutionId = id,
            Status = AgentRunStatus.Running,
            PollInterval = TimeSpan.FromMilliseconds(5),
        };
    }
}
