using System.Text;
using ErrorOr;
using IniaAi.Application.Workflows.EditorAgent;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Proposals;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace IniaAi.Application.UnitTests.Workflows.EditorAgent;

/// <summary>
/// The report-text check across a conversation: a report staged in any turn stays report text
/// for every later turn, the agent's own earlier answers do not, and the workflow's own run
/// evidence never counts as the workflow's text.
/// </summary>
public sealed partial class WorkflowEditorAgentServiceTests
{
    private const string OWN_EXPLANATION = "Ask the reader to name the segment that drove the change before it gives a total.";

    [Fact]
    public async Task InALaterTurn_AReportPassageFromAnEarlierTurnIsRefused_ButTheAgentsOwnExplanationIsNot()
    {
        string picked = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, ACME, WorkflowState.Draft, daysAgo: 1, "acme-2025.pdf");
        EditorAgentConversationView conversation = await StartAsync();
        Workflow workflow = EditorAgentWorkflows.Build();
        string draft = workflow.ToExportJson(includeRunTraces: true);
        string firstTurn = AgentRunIds.ForConversationTurn(WorkflowEditorAgentService.PURPOSE, conversation.Id, 1, AgentRunIds.FIRST_PASS);
        string secondTurn = AgentRunIds.ForConversationTurn(WorkflowEditorAgentService.PURPOSE, conversation.Id, 2, AgentRunIds.FIRST_PASS);
        AnswerWith(firstTurn, "The report says: " + PRODUCER_OUTPUT + " I suggest this change: " + OWN_EXPLANATION);
        JObject proposed = Edited(draft, EditorAgentWorkflows.PRODUCER_TAG, "UserPromptTemplate", "Break revenue down by segment in {Currency}. Like: " + PRODUCER_OUTPUT);
        Block(proposed, EditorAgentWorkflows.READER_TAG)["Properties"]!["UserPromptTemplate"] = "Summarize: {revenue.answer} " + OWN_EXPLANATION;
        AgentWrites(secondTurn, proposed, Summary(workflow, EditorAgentWorkflows.PRODUCER_TAG, EditorAgentWorkflows.READER_TAG));

        EditorAgentTurnView first = (await Service.SendMessageAsync(
            new EditorAgentMessage(ORG_ID, USER_ID, conversation.Id, "What does the report say about revenue?", SONNET, workflow)
            {
                ReportIds = [picked],
            },
            TestContext.Current.CancellationToken)).Value;
        await WaitForSettledAsync(conversation.Id, first.Number);
        EditorAgentTurnView second = (await Service.SendMessageAsync(
            new EditorAgentMessage(ORG_ID, USER_ID, conversation.Id, "Put that into the prompts", SONNET, workflow),
            TestContext.Current.CancellationToken)).Value;
        await WaitForSettledAsync(conversation.Id, second.Number);
        EditorAgentTurnView turn = await ReadTurnAsync(conversation.Id, second.Number);

        Assert.Equal(2, second.Number);
        Assert.DoesNotContain(_client.StartRequests[^1].Documents, document => document.Name.Contains(picked, StringComparison.Ordinal));
        Assert.Equal(2, turn.Proposal!.Changes.Count);
        EditorAgentChangeView copied = Assert.Single(turn.Proposal.Changes, change => change.Change.Refusal is not null);
        EditorAgentChangeView explained = Assert.Single(turn.Proposal.Changes, change => change.Change.Refusal is null);
        Assert.Equal(ProposalRefusal.ReportText, copied.Change.Refusal);
        Assert.Contains(OWN_EXPLANATION, explained.Change.NewJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATestRunsOutputInTheDraft_DoesNotMakeReportTextTheWorkflowsOwn()
    {
        string picked = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, ACME, WorkflowState.Draft, daysAgo: 1, "acme-2025.pdf");
        EditorAgentConversationView conversation = await StartAsync();
        Workflow workflow = EditorAgentWorkflows.Build();
        _ = new BlockResponse(EditorAgentWorkflows.Producer(workflow)) { RawResponse = PRODUCER_OUTPUT };
        string draft = workflow.ToExportJson(includeRunTraces: true);
        AgentWrites(
            RunId(conversation.Id, 1),
            Edited(draft, EditorAgentWorkflows.READER_TAG, "UserPromptTemplate", "Summarize: {revenue.answer} Like: " + PRODUCER_OUTPUT),
            Summary(workflow, EditorAgentWorkflows.READER_TAG));

        EditorAgentTurnView sent = (await Service.SendMessageAsync(
            new EditorAgentMessage(ORG_ID, USER_ID, conversation.Id, "Show an example answer", SONNET, workflow)
            {
                ReportIds = [picked],
            },
            TestContext.Current.CancellationToken)).Value;
        await WaitForSettledAsync(conversation.Id, sent.Number);
        EditorAgentTurnView turn = await ReadTurnAsync(conversation.Id, sent.Number);

        EditorAgentChangeView change = Assert.Single(turn.Proposal!.Changes);
        Assert.Equal(ProposalRefusal.ReportText, change.Change.Refusal);
    }

    [Fact]
    public void ReportLeaks_ThreeFiguresFromAReport_AreRefused_ButTwo_OrTheWorkflowsOwn_AreNot()
    {
        var summary = new EditorAgentReportSummary("run-1", DateTime.UtcNow, ACME, "completed", "Report", []);
        var evidence = new EditorAgentReportEvidence(summary, [], ["Sales 1,234.5; margin 5.6%; debt 12.3m; capex 77.1; staff 2024."]);
        var leaks = new EditorAgentReportLeaks([evidence], []);
        List<string> workflowTexts = ["Flag a margin below 5.6%."];

        ProposedChange three = Change("Expect sales near 1234.5, debt of 12.3 m and capex at 77.1.");
        ProposedChange two = Change("Expect sales near 1,234.5 and debt of 12.3m.");
        ProposedChange ownFigure = Change("Expect sales near 1,234.5, a margin of 5.6% and debt of 12.3m.");
        ProposedChange years = Change("Compare 2023, 2024 and 2025 with 12 months before.");

        Assert.Equal([three], leaks.Find([three], workflowTexts).Select(static item => item.Change));
        Assert.Equal("figures from a past report", leaks.Find([three], workflowTexts)[0].What);
        Assert.Empty(leaks.Find([two], workflowTexts));
        Assert.Empty(leaks.Find([ownFigure], workflowTexts));
        Assert.Empty(leaks.Find([years], workflowTexts));
        Assert.Equal(2, leaks.Find([two, Change("Capex was 77.1 last year.")], workflowTexts).Count);
    }

    [Fact]
    public void EveryStringARunProduced_IsReportText_ButTheTemplatesOwnFieldsAreNot()
    {
        Workflow executed = Executed(Guid.NewGuid().ToString(), ORG_ID, EditorAgentWorkflows.TYPE);
        executed.ExecutionState = new WorkflowExecutionState(executed) { TextInput = "Workflow input as sent: Initech." };
        executed.Sections.First().ExecutionState = new WorkflowExecutionState(executed.Sections.First()) { TextInput = "Section input as sent." };
        ProducerOf(executed).BlockResponse!.JsonResponse = "{\"rev\": 41}";
        var summary = new EditorAgentReportSummary("run-1", DateTime.UtcNow, ACME, "completed", "Acme FY25 credit memo", []);

        EditorAgentReportEvidence evidence = EditorAgentReports.Evidence(summary, failure: "Timeout: page 4 of acme.pdf", executed, long.MaxValue);

        Assert.Contains("Workflow input as sent: Initech.", evidence.RunTexts);
        Assert.Contains("Section input as sent.", evidence.RunTexts);
        Assert.Contains("{\"rev\": 41}", evidence.RunTexts);
        Assert.Contains("Acme FY25 credit memo", evidence.RunTexts);
        Assert.Contains("Timeout: page 4 of acme.pdf", evidence.RunTexts);
        Assert.DoesNotContain("Break revenue down by segment in {Currency}.", evidence.RunTexts);
        Assert.DoesNotContain(EditorAgentWorkflows.PRODUCER_NAME, evidence.RunTexts);
    }

    [Fact]
    public void AConfidentialVariablesValue_IsMaskedInTheStagedRun_AndTheGateCatchesIt()
    {
        const string SECRET = "ZETA-7781";
        Workflow executed = Executed(Guid.NewGuid().ToString(), ORG_ID, EditorAgentWorkflows.TYPE);
        _ = new WorkflowVariable(executed, "ClientCode") { IsConfidential = true };
        _ = new WorkflowVariable(executed, "Desk") { IsConfidential = false };
        var properties = (AiQueryProperties)ProducerOf(executed).Properties;
        properties.ExecutionState!.UserPrompt = $"Client {SECRET} on desk Rates. " + properties.ExecutionState.UserPrompt;
        executed.ExecutionState = new WorkflowExecutionState(executed) { TextInput = $"Run for {SECRET}" };
        var summary = new EditorAgentReportSummary("run-1", DateTime.UtcNow, ACME, "completed", "Report", []);
        var variables = new Dictionary<string, object> { ["clientCode"] = SECRET, ["desk"] = "Rates" };

        EditorAgentReportEvidence evidence = EditorAgentReports.Evidence(summary, failure: null, executed, long.MaxValue, budget: null, variables);

        string run = Encoding.UTF8.GetString(Assert.Single(evidence.Documents, document => document.Name == EditorAgentReports.RunDocumentName("run-1")).Content.Span);
        Assert.DoesNotContain(SECRET, run, StringComparison.Ordinal);
        Assert.Contains("Client •••• on desk Rates.", run, StringComparison.Ordinal);
        Assert.DoesNotContain(evidence.RunTexts, text => text.Contains(SECRET, StringComparison.Ordinal));
        Assert.Contains("Rates", evidence.RunTexts);
        Assert.Single(new EditorAgentReportLeaks([evidence], []).Find([Change($"Use client {SECRET} as the example.")], []));
    }

    [Fact]
    public void ARunThatDoesNotFitTheMessagesBudget_IsNotStaged_AndTheHeaderSaysSo()
    {
        Workflow executed = Executed(Guid.NewGuid().ToString(), ORG_ID, EditorAgentWorkflows.TYPE);
        var summary = new EditorAgentReportSummary("run-1", DateTime.UtcNow, ACME, "completed", "Report", []);
        var budget = new EditorAgentReportBudget(bytes: 10_000, files: 10);

        EditorAgentReportEvidence evidence = EditorAgentReports.Evidence(summary, failure: null, executed, long.MaxValue, budget);

        AgentRunDocument header = Assert.Single(evidence.Documents);
        Assert.Contains(EditorAgentReportFiles.BUDGET_USED, Encoding.UTF8.GetString(header.Content.Span), StringComparison.Ordinal);
        Assert.Equal(9, budget.FilesLeft);
        Assert.True(budget.BytesLeft > 0);
    }

    /// <summary>
    /// A turn settled before the retention window keeps its row, but loses the step labels it
    /// showed and the text of the changes the gate refused as report text.
    /// </summary>
    [Fact]
    public async Task ATurnPastTheRetentionWindow_LosesItsStepLabelsAndItsRefusedReportText()
    {
        string picked = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, ACME, WorkflowState.Draft, daysAgo: 1, "acme-2025.pdf");
        EditorAgentConversationView conversation = await StartAsync();
        Workflow workflow = EditorAgentWorkflows.Build();
        string draft = workflow.ToExportJson(includeRunTraces: true);
        string copied = "Summarize: {revenue.answer} Like: " + PRODUCER_OUTPUT;
        for (int round = 1; round <= EditorAgentProposalGate.MAX_ROUNDS; round++)
        {
            AgentWrites(RunId(conversation.Id, round), Edited(draft, EditorAgentWorkflows.READER_TAG, "UserPromptTemplate", copied), Summary(workflow, EditorAgentWorkflows.READER_TAG));
        }

        EditorAgentTurnView sent = (await Service.SendMessageAsync(
            new EditorAgentMessage(ORG_ID, USER_ID, conversation.Id, "Show an example answer", SONNET, workflow)
            {
                ReportIds = [picked],
            },
            TestContext.Current.CancellationToken)).Value;
        await WaitForSettledAsync(conversation.Id, sent.Number);
        using (IServiceScope scope = _provider.CreateScope())
        {
            UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
            AgentConversationTurn row = await context.AgentConversationTurns.SingleAsync(TestContext.Current.CancellationToken);
            row.ActivityJson = "{\"steps\":[{\"label\":\"Read the Acme Holdings report\"}]}";
            row.CompletedAt = DateTime.UtcNow.AddDays(-(_editor.RetentionDays + 1));
            _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        _ = await _provider.GetRequiredService<IAgentConversationReaper>().SweepAsync(TestContext.Current.CancellationToken);

        using IServiceScope after = _provider.CreateScope();
        UserDataDbContext remaining = after.ServiceProvider.GetRequiredService<UserDataDbContext>();
        AgentConversationTurn turn = await remaining.AgentConversationTurns.SingleAsync(TestContext.Current.CancellationToken);
        EditorAgentProposal proposal = await remaining.EditorAgentProposals.SingleAsync(TestContext.Current.CancellationToken);
        ProposedChange refused = Assert.Single(proposal.Proposal.Changes);
        Assert.Null(turn.ActivityJson);
        Assert.NotNull(turn.AnswerMarkdown);
        Assert.Equal(ProposalRefusal.ReportText, refused.Refusal);
        Assert.Null(refused.NewJson);
        Assert.DoesNotContain(PRODUCER_OUTPUT, proposal.Proposal.ToJson(), StringComparison.Ordinal);
    }

    private static Block ProducerOf(Workflow executed)
    {
        return executed.Sections.SelectMany(static section => section.Blocks).Single(static block => block.ReplacementTag == EditorAgentWorkflows.PRODUCER_TAG);
    }

    /// <summary>The run answers with this text instead of the stub's default answer.</summary>
    private void AnswerWith(string runId, string answer)
    {
        Func<string, ErrorOr<AgentRunMonitor>> answers = _client.OnGetRun!;
        _client.OnGetRun = id => id == runId
            ? new AgentRunMonitor
            {
                ExecutionId = id,
                Status = AgentRunStatus.Succeeded,
                EstimatedCostUsd = COST_PER_RUN,
                Result = new AgentRunResult(answer, "session"),
            }
            : answers(id);
    }
}
