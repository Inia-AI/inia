using System.Security.Cryptography;
using System.Text;
using AngleSharp.Dom;
using Bunit;
using IniaAi.Application.Workflows.EditorAgent;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Counterparties;
using Document = IniaAi.Domain.Entities.Documents.Document;
using DocumentInfo = IniaAi.Domain.Entities.Documents.DocumentInfo;
using FileMetadata = IniaAi.Domain.Entities.Documents.FileMetadata;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using IniaAi.WebApp.Components.WorkflowEditor.Agent;
using MudDialogProvider = MudBlazor.MudDialogProvider;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Agent;

/// <summary>
/// The composer's "Use reports as examples" button and its "Choose reports" dialog over real past
/// runs in the SQLite database: the dialog lists the organization's reports of the open workflow
/// with their workflow type, filters them, caps the choice at five, the choice shows as chips,
/// and the next message carries it to the agent, after which the chips are gone.
/// </summary>
public sealed class EditorAgentReportPickerTests : EditorAgentRenderTest
{
    private const string WORKFLOW_TYPE = "WT-EditorAgentReports";
    private const string WORKFLOW_TYPE_NAME = "Credit Part 1";
    private const string OTHER_ORGANIZATION_ID = "org-elsewhere";
    private const string ACME = "Acme Holdings";
    private const string GLOBEX = "Globex Bank";
    private const string REPORTS_BUTTON = "[data-testid=editor-agent-reports-button]";
    private const string ROW = "[data-testid=editor-agent-report-row]";
    private const string COUNT = "[data-testid=editor-agent-reports-count]";
    private const string USE = "[data-testid=editor-agent-reports-use]";
    private const string CHIP = "[data-testid=editor-agent-report-chip]";
    private const string SENT_REPORT = ".editor-agent-message--user [data-testid=editor-agent-sent-report]";

    private readonly Workflow _workflow;
    private IRenderedComponent<MudDialogProvider> _dialogs = null!;
    private string _userId = string.Empty;

    public EditorAgentReportPickerTests()
        : base(enabled: true)
    {
        _workflow = new Workflow("Credit report") { Title = "Credit report", WorkflowTypeId = WORKFLOW_TYPE };
        var section = new Section("Analysis", _workflow) { Title = "Analysis" };
        _ = new Block(BlockTypes.AiQuery, section, "revenue") { Name = "Revenue breakdown", StableBlockId = "stable-revenue" };
        RuntimeAnswers();
    }

    [Fact]
    public async Task TheEmptyConversation_ShowsNoExampleQuestions()
    {
        IRenderedComponent<EditorAgentPanel> panel = await RenderPanelAsync();

        Assert.NotNull(panel.Find(".editor-agent-empty-title"));
        Assert.Empty(panel.FindAll(".editor-agent-example"));
        Assert.Empty(panel.FindAll(".editor-agent-empty button"));
    }

    [Fact]
    public async Task ChosenReports_ShowAsChipsNamingTheirWorkflow_AndTheNextMessageCarriesThemToTheAgentOnce()
    {
        IRenderedComponent<EditorAgentPanel> panel = await RenderPanelAsync();
        await SeedAsync(new Organization { Id = OTHER_ORGANIZATION_ID, Name = "Elsewhere" });
        string acme = await SeedReportAsync(ORGANIZATION_ID, ACME, WorkflowState.Draft, daysAgo: 2);
        string globex = await SeedReportAsync(ORGANIZATION_ID, GLOBEX, WorkflowState.Failed, daysAgo: 1, "globex-q2.pdf");
        string theirs = await SeedReportAsync(OTHER_ORGANIZATION_ID, ACME, WorkflowState.Draft, daysAgo: 0);

        OpenDialog(panel);
        _dialogs.WaitForAssertion(() => Assert.Equal(2, _dialogs.FindAll(ROW).Count));
        List<string?> listed = [.. _dialogs.FindAll(ROW).Select(static row => row.GetAttribute("data-report-id"))];
        Assert.Equal([globex, acme], listed);
        Assert.DoesNotContain(theirs, listed);
        string globexRow = _dialogs.FindAll(ROW)[0].TextContent;
        Assert.Contains($"{WORKFLOW_TYPE_NAME} · ", globexRow, StringComparison.Ordinal);
        Assert.Contains("Globex Bank · Failed", globexRow, StringComparison.Ordinal);
        Assert.Contains("globex-q2.pdf", globexRow, StringComparison.Ordinal);

        Choose(acme);
        Choose(globex);
        _dialogs.WaitForAssertion(() => Assert.Contains("2 of 5 chosen", _dialogs.Find(COUNT).TextContent, StringComparison.Ordinal));
        Click(_dialogs.Find(USE));

        panel.WaitForAssertion(() => Assert.Equal(2, panel.FindAll(CHIP).Count));
        Assert.Contains($"{WORKFLOW_TYPE_NAME} · ", panel.FindAll(CHIP)[0].TextContent, StringComparison.Ordinal);
        Assert.Contains(ACME, panel.FindAll(CHIP)[0].TextContent, StringComparison.Ordinal);

        await panel.InvokeAsync(() => panel.FindAll(CHIP)[1].QuerySelector("button")!.Click());
        IElement chip = Assert.Single(panel.FindAll(CHIP));
        Assert.Equal(acme, chip.GetAttribute("data-report-id"));

        await panel.InvokeAsync(() => panel.Find("textarea").Input("Why is the summary so long?"));
        await panel.InvokeAsync(() => panel.FindAll("button").Single(button => button.TextContent.Trim() == "Send").Click());

        panel.WaitForAssertion(() => Assert.Single(Runtime.StartRequests), TimeSpan.FromSeconds(30));
        AgentRunStartRequest run = Runtime.StartRequests[0];
        Assert.Contains(run.Documents, document => document.Name == EditorAgentReports.HeaderDocumentName(acme));
        Assert.DoesNotContain(run.Documents, document => document.Name.Contains(globex, StringComparison.Ordinal));
        Assert.Empty(panel.FindAll(CHIP));
    }

    [Fact]
    public async Task TheReportsSentWithAMessage_ShowUnderIt_AndStillDoAfterTheConversationIsReadAgain()
    {
        const string QUESTION = "Why is the summary so long?";
        IRenderedComponent<EditorAgentPanel> panel = await RenderPanelAsync();
        string acme = await SeedReportAsync(ORGANIZATION_ID, ACME, WorkflowState.Draft, daysAgo: 2);
        string globex = await SeedReportAsync(ORGANIZATION_ID, GLOBEX, WorkflowState.Failed, daysAgo: 1);

        OpenDialog(panel);
        _dialogs.WaitForAssertion(() => Assert.Equal(2, _dialogs.FindAll(ROW).Count));
        Choose(globex);
        Choose(acme);
        Click(_dialogs.Find(USE));
        panel.WaitForAssertion(() => Assert.Equal(2, panel.FindAll(CHIP).Count));

        await panel.InvokeAsync(() => panel.Find("textarea").Input(QUESTION));
        await panel.InvokeAsync(() => panel.FindAll("button").Single(button => button.TextContent.Trim() == "Send").Click());

        panel.WaitForAssertion(() =>
        {
            Assert.Empty(panel.FindAll(CHIP));
            AssertSentWith(panel, QUESTION, globex, acme);
        });
        panel.WaitForAssertion(() => Assert.Single(panel.FindAll(".editor-agent-answer")), TimeSpan.FromSeconds(30));
        AssertSentWith(panel, QUESTION, globex, acme);

        IRenderedComponent<EditorAgentPanel> reloaded = RenderPanel();
        reloaded.WaitForAssertion(() => AssertSentWith(reloaded, QUESTION, globex, acme));
        IElement chip = reloaded.FindAll(SENT_REPORT)[0];
        Assert.Contains($"{WORKFLOW_TYPE_NAME} · ", chip.TextContent, StringComparison.Ordinal);
        Assert.Contains($"{GLOBEX} · Failed", chip.TextContent, StringComparison.Ordinal);
        Assert.Null(chip.QuerySelector("button"));
    }

    [Fact]
    public async Task TheDialog_StopsAtFiveChosen_AndTheSearchNarrowsTheList()
    {
        IRenderedComponent<EditorAgentPanel> panel = await RenderPanelAsync();
        List<string> ids = [];
        for (int index = 0; index < 6; index++)
        {
            ids.Add(await SeedReportAsync(ORGANIZATION_ID, index == 5 ? GLOBEX : $"{ACME} {index}", WorkflowState.Draft, daysAgo: 6 - index));
        }

        OpenDialog(panel);
        _dialogs.WaitForAssertion(() => Assert.Equal(6, _dialogs.FindAll(ROW).Count));
        foreach (string id in ids.Take(5))
        {
            Choose(id);
        }

        _dialogs.WaitForAssertion(() => Assert.Contains("5 of 5 chosen", _dialogs.Find(COUNT).TextContent, StringComparison.Ordinal));
        IElement sixth = _dialogs.FindAll(ROW).Single(row => row.GetAttribute("data-report-id") == ids[5]);
        Assert.Contains("editor-agent-report-row-blocked", sixth.ClassName, StringComparison.Ordinal);
        Choose(ids[5]);
        Assert.Contains("5 of 5 chosen", _dialogs.Find(COUNT).TextContent, StringComparison.Ordinal);

        await _dialogs.InvokeAsync(() => _dialogs.Find(".editor-agent-reports-search input").Input("globex"));
        _dialogs.WaitForAssertion(() => Assert.Equal(ids[5], Assert.Single(_dialogs.FindAll(ROW)).GetAttribute("data-report-id")), TimeSpan.FromSeconds(5));

        Click(_dialogs.Find(USE));
        panel.WaitForAssertion(() => Assert.Equal(5, panel.FindAll(CHIP).Count));
        Assert.DoesNotContain(panel.FindAll(CHIP), chip => chip.GetAttribute("data-report-id") == ids[5]);
    }

    private static void AssertSentWith(IRenderedComponent<EditorAgentPanel> panel, string text, params string[] reportIds)
    {
        IElement message = Assert.Single(panel.FindAll(".editor-agent-message--user"));
        Assert.Equal(text, message.QuerySelector(".editor-agent-message-text")!.TextContent);
        Assert.Equal(reportIds, message.QuerySelectorAll("[data-testid=editor-agent-sent-report]").Select(static chip => chip.GetAttribute("data-report-id")));
    }

    private void OpenDialog(IRenderedComponent<EditorAgentPanel> panel)
    {
        panel.InvokeAsync(() => panel.Find(REPORTS_BUTTON).Click()).GetAwaiter().GetResult();
    }

    private void Choose(string reportId)
    {
        Click(_dialogs.FindAll(ROW)
            .Single(row => row.GetAttribute("data-report-id") == reportId)
            .QuerySelector(".editor-agent-report-row-text")!);
    }

    private void Click(IElement element)
    {
        _dialogs.InvokeAsync(() => element.Click()).GetAwaiter().GetResult();
    }

    private async Task<string> SeedReportAsync(
        string organizationId,
        string counterpartyName,
        WorkflowState state,
        int daysAgo,
        params string[] documentNames)
    {
        var counterparty = new Counterparty(organizationId, counterpartyName);
        DateTime createdAt = DateTime.UtcNow.AddDays(-daysAgo);
        var execution = new WorkflowExecution
        {
            WorkflowTypeId = WORKFLOW_TYPE,
            OrganizationId = organizationId,
            Title = $"{counterpartyName} report",
            WorkflowState = state,
            Counterparty = counterparty,
            CounterpartyId = counterparty.Id,
            CreatedAt = createdAt,
            CompletedAt = createdAt.AddMinutes(20),
        };
        execution.JobId = Guid.NewGuid().ToString("N");
        await SeedAsync(counterparty, execution);
        foreach (string name in documentNames)
        {
            var file = new FileMetadata(organizationId, name, 10, StorageProvider.Database);
            var document = new Document(file, MediaType.ApplicationPdf);
            await SeedAsync(file, document, new DocumentInfo(document.Id) { JobId = execution.JobId });
        }

        return execution.Id;
    }

    private async Task<IRenderedComponent<EditorAgentPanel>> RenderPanelAsync()
    {
        User user = await SignInAsync("engineer@inia.ai", Roles.WORKFLOW_EDITOR);
        _userId = user.Id;
        await SeedWorkflowTypeAsync(WORKFLOW_TYPE, WORKFLOW_TYPE_NAME);

        _dialogs = Render<MudDialogProvider>();
        return RenderPanel();
    }

    /// <summary>A panel opened afresh, as after a page reload: it reads the conversation again.</summary>
    private IRenderedComponent<EditorAgentPanel> RenderPanel()
    {
        IRenderedComponent<EditorAgentPanel> panel = RenderInApp<EditorAgentPanel>(parameters => parameters
            .Add(c => c.OrganizationId, ORGANIZATION_ID)
            .Add(c => c.UserId, _userId)
            .Add(c => c.WorkflowTypeId, WORKFLOW_TYPE)
            .Add(c => c.CurrentWorkflow, () => _workflow));
        _ = panel.WaitForElement("textarea:not([disabled])");

        return panel;
    }

    private void RuntimeAnswers()
    {
        byte[] trace = Encoding.UTF8.GetBytes("""{"type":"stage","stage":"started","ts":"2026-10-06T10:00:00Z"}""" + "\n");

        Runtime.OnStartRun = request => new AgentRunMonitor
        {
            ExecutionId = request.ExecutionId,
            Status = AgentRunStatus.Running,
            PollInterval = TimeSpan.FromMilliseconds(5),
        };
        Runtime.OnGetRun = id => new AgentRunMonitor
        {
            ExecutionId = id,
            Status = AgentRunStatus.Succeeded,
            EstimatedCostUsd = 0.03m,
            Result = new AgentRunResult("The summary block asks for five paragraphs.", "session"),
        };
        Runtime.OnCompleteRun = id => new AgentRunMonitor { ExecutionId = id, Status = AgentRunStatus.Succeeded };
        Runtime.OnListRunFiles = _ => ErrorOr.ErrorOrFactory.From<IReadOnlyList<AgentRunFileEntry>>(
        [
            new AgentRunFileEntry
            {
                Path = "trace/trace.ndjson",
                SizeBytes = trace.LongLength,
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(trace)),
                Kind = AgentRunFileKind.Trace,
            },
        ]);
        Runtime.OnGetRunFile = (_, _) => trace;
    }
}
