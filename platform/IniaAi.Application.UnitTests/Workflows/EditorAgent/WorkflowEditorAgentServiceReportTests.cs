using System.Globalization;
using System.Text;
using ErrorOr;
using IniaAi.Application.Workflows.EditorAgent;
using IniaAi.Common.Enums;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.ExecutionStates.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Domain.Enums;
using IniaAi.Domain.Proposals;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Application.UnitTests.Workflows.EditorAgent;

/// <summary>
/// Past reports as the agent's evidence: the list every turn stages, the reports the user picks
/// for a message, and the reports the agent opens mid-turn. Each is bounded to the caller's
/// organization and the open workflow type, against real rows in the SQLite UserData database.
/// </summary>
public sealed partial class WorkflowEditorAgentServiceTests
{
    private const string TYPE_NAME = "Credit Part 1";
    private const string OTHER_TYPE_NAME = "Credit Part 2";
    private const string HIDDEN_TYPE = "WT-Hidden";
    private const string ACME = "Acme Holdings";
    private const string INITECH = "Initech";
    private const string GLOBEX = "Globex Bank";
    private const string PRODUCER_OUTPUT = "Revenue grew 12% to 4.1bn, driven by retail.";
    private const string RESOLVED_PRODUCER_PROMPT = "Break revenue down by segment in USD.";
    private const string SOURCE_PAGE_TEXT = "CONFIDENTIAL PAGE TEXT of the annual report. ";
    private const string PERIOD_TAG = "assets";
    private const int READER_OUTPUT_CHARS = 12_000;
    private const string PERIOD_STABLE_ID = "stable-assets";
    private const string PERIOD_OUTPUT = "Right-of-use assets for {0} sit inside property, plant and equipment.";

    [Fact]
    public async Task ListReportsAsync_ListsOnlyTheOrganizationsSettledRunsOfThisWorkflow_NewestFirst()
    {
        string older = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, ACME, WorkflowState.Draft, daysAgo: 3, "acme-2025.pdf");
        string newer = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, GLOBEX, WorkflowState.Failed, daysAgo: 1, "globex-q2.pdf", "globex-notes.docx");
        _ = await SeedReportAsync(OTHER_ORG_ID, EditorAgentWorkflows.TYPE, ACME, WorkflowState.Draft, daysAgo: 0, "theirs.pdf");
        _ = await SeedReportAsync(ORG_ID, OTHER_TYPE, ACME, WorkflowState.Draft, daysAgo: 0, "other-type.pdf");
        _ = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, ACME, WorkflowState.Running, daysAgo: 0, "running.pdf");
        _ = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, ACME, WorkflowState.Draft, daysAgo: 0, deleted: true);

        EditorAgentReportPage page = (await Service.ListReportsAsync(
            ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, cancellationToken: TestContext.Current.CancellationToken)).Value;
        IReadOnlyList<EditorAgentReportSummary> reports = page.Reports;
        ErrorOr<EditorAgentReportPage> noRole = await Service.ListReportsAsync(
            ORG_ID, PLAIN_USER_ID, EditorAgentWorkflows.TYPE, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([newer, older], reports.Select(static report => report.Id));
        Assert.Equal(GLOBEX, reports[0].Counterparty);
        Assert.Equal("failed", reports[0].Status);
        Assert.Equal(["globex-notes.docx", "globex-q2.pdf"], reports[0].DocumentNames);
        Assert.Equal("completed", reports[1].Status);
        Assert.All(reports, static report => Assert.Equal(EditorAgentWorkflows.TYPE, report.WorkflowTypeId));
        Assert.All(reports, static report => Assert.Equal(TYPE_NAME, report.WorkflowTypeName));
        Assert.False(page.HasMore);
        Assert.Equal(EditorAgentErrors.FORBIDDEN_CODE, noRole.FirstError.Code);
    }

    [Fact]
    public async Task ListReportsAsync_FiltersBySearchStatusAndDate_AndPages()
    {
        string oldAcme = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, ACME, WorkflowState.Draft, daysAgo: 10, "acme-annual.pdf");
        string globex = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, GLOBEX, WorkflowState.Failed, daysAgo: 5, "globex-q2.pdf");
        string initech = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, INITECH, WorkflowState.Draft, daysAgo: 2, "INITECH-Notes.pdf");
        string newAcme = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, ACME, WorkflowState.Confirmed, daysAgo: 1);
        _ = await SeedReportAsync(OTHER_ORG_ID, EditorAgentWorkflows.TYPE, GLOBEX, WorkflowState.Failed, daysAgo: 1, "globex-notes.pdf");

        async Task<EditorAgentReportPage> List(EditorAgentReportQuery query) => (await Service.ListReportsAsync(
            ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, query, TestContext.Current.CancellationToken)).Value;
        static IEnumerable<string> Ids(EditorAgentReportPage page) => page.Reports.Select(static report => report.Id);

        Assert.Equal([globex], Ids(await List(new EditorAgentReportQuery(Search: " GLOBEX "))));
        Assert.Equal([initech], Ids(await List(new EditorAgentReportQuery(Search: "notes"))));
        Assert.Equal([newAcme, oldAcme], Ids(await List(new EditorAgentReportQuery(Search: "acme"))));
        Assert.Equal([globex], Ids(await List(new EditorAgentReportQuery(Status: EditorAgentReports.FAILED))));
        Assert.Equal([newAcme, initech, globex], Ids(await List(new EditorAgentReportQuery(From: DateTime.UtcNow.AddDays(-6).Date))));
        Assert.Equal([globex, oldAcme], Ids(await List(new EditorAgentReportQuery(To: DateTime.UtcNow.AddDays(-3).Date))));

        EditorAgentReportPage first = await List(new EditorAgentReportQuery(Take: 3));
        EditorAgentReportPage second = await List(new EditorAgentReportQuery(Skip: 3, Take: 3));
        Assert.Equal([newAcme, initech, globex], Ids(first));
        Assert.True(first.HasMore);
        Assert.Equal([oldAcme], Ids(second));
        Assert.False(second.HasMore);
    }

    [Fact]
    public async Task AReportOfAnotherTypeTheOrganizationCanUse_IsListedAndPicked_AndNamesItsType()
    {
        await SeedHiddenTypeAsync();
        string partTwo = await SeedReportAsync(ORG_ID, OTHER_TYPE, GLOBEX, WorkflowState.Draft, daysAgo: 1, "globex-q2.pdf");
        string theirs = await SeedReportAsync(OTHER_ORG_ID, OTHER_TYPE, GLOBEX, WorkflowState.Draft, daysAgo: 0);
        _ = await SeedReportAsync(ORG_ID, HIDDEN_TYPE, GLOBEX, WorkflowState.Draft, daysAgo: 0);

        IReadOnlyList<EditorAgentWorkflowTypeOption> types = (await Service.ListReportWorkflowTypesAsync(
            ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, TestContext.Current.CancellationToken)).Value;
        EditorAgentReportPage listed = (await Service.ListReportsAsync(
            ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, new EditorAgentReportQuery(WorkflowTypeId: OTHER_TYPE), TestContext.Current.CancellationToken)).Value;
        ErrorOr<EditorAgentReportPage> hidden = await Service.ListReportsAsync(
            ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, new EditorAgentReportQuery(WorkflowTypeId: HIDDEN_TYPE), TestContext.Current.CancellationToken);

        Assert.Equal(new EditorAgentWorkflowTypeOption(EditorAgentWorkflows.TYPE, TYPE_NAME), types[0]);
        Assert.Contains(new EditorAgentWorkflowTypeOption(OTHER_TYPE, OTHER_TYPE_NAME), types);
        Assert.DoesNotContain(types, static type => type.Id == HIDDEN_TYPE);
        EditorAgentReportSummary report = Assert.Single(listed.Reports);
        Assert.Equal(partTwo, report.Id);
        Assert.Equal(OTHER_TYPE_NAME, report.WorkflowTypeName);
        Assert.Equal(EditorAgentErrors.FORBIDDEN_CODE, hidden.FirstError.Code);

        EditorAgentConversationView conversation = await StartAsync();
        ErrorOr<EditorAgentTurnView> anotherOrganization = await Service.SendMessageAsync(
            new EditorAgentMessage(ORG_ID, USER_ID, conversation.Id, "Compare with Part 2", SONNET, EditorAgentWorkflows.Build())
            {
                ReportIds = [theirs],
            },
            TestContext.Current.CancellationToken);
        EditorAgentTurnView turn = (await Service.SendMessageAsync(
            new EditorAgentMessage(ORG_ID, USER_ID, conversation.Id, "Compare with Part 2", SONNET, EditorAgentWorkflows.Build())
            {
                ReportIds = [partTwo],
            },
            TestContext.Current.CancellationToken)).Value;
        await WaitForSettledAsync(conversation.Id, turn.Number);

        Assert.Equal(EditorAgentErrors.REPORT_NOT_FOUND_CODE, anotherOrganization.FirstError.Code);
        AgentRunStartRequest run = Assert.Single(_client.StartRequests);
        Assert.Contains($"{EditorAgentReports.HeaderDocumentName(partTwo)}: {OTHER_TYPE_NAME}, not the open workflow: ", run.Prompt, StringComparison.Ordinal);
        Assert.Contains($"- Workflow type: {OTHER_TYPE_NAME}, not the open workflow: ", DocumentText(run, EditorAgentReports.HeaderDocumentName(partTwo)), StringComparison.Ordinal);
        Assert.Equal(OTHER_TYPE, (string?)JObject.Parse(DocumentText(run, EditorAgentReports.RunDocumentName(partTwo)))["WorkflowTypeId"]);
        Assert.DoesNotContain(partTwo, DocumentText(run, EditorAgentReports.LIST_DOCUMENT), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PickedReport_IsStagedAsItsHeaderAndItsWholeRun_WithPeriodCopiesLinkedToTheirTemplateBlock()
    {
        string picked = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, ACME, WorkflowState.Draft, daysAgo: 2, "acme-2025.pdf");
        string listedOnly = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, GLOBEX, WorkflowState.Confirmed, daysAgo: 1);
        string theirs = await SeedReportAsync(OTHER_ORG_ID, EditorAgentWorkflows.TYPE, ACME, WorkflowState.Draft, daysAgo: 0);
        EditorAgentConversationView conversation = await StartAsync();

        EditorAgentTurnView turn = (await Service.SendMessageAsync(
            new EditorAgentMessage(ORG_ID, USER_ID, conversation.Id, "How are right-of-use assets handled?", SONNET, EditorAgentWorkflows.Build())
            {
                ReportIds = [picked],
            },
            TestContext.Current.CancellationToken)).Value;
        await WaitForSettledAsync(conversation.Id, turn.Number);

        AgentRunStartRequest run = Assert.Single(_client.StartRequests);
        Assert.Contains($"{EditorAgentReports.HeaderDocumentName(picked)}: ", run.Prompt, StringComparison.Ordinal);
        Assert.Contains("The user picked these past reports", run.Prompt, StringComparison.Ordinal);

        string list = DocumentText(run, EditorAgentReports.LIST_DOCUMENT);
        Assert.StartsWith($"# Past reports of {TYPE_NAME}, newest first", list, StringComparison.Ordinal);
        Assert.Contains($"id {listedOnly}:", list, StringComparison.Ordinal);
        Assert.Contains($"id {picked}:", list, StringComparison.Ordinal);
        Assert.Contains("acme-2025.pdf", list, StringComparison.Ordinal);
        Assert.DoesNotContain(theirs, list, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Documents, document => document.Name.Contains(listedOnly, StringComparison.Ordinal));

        string header = DocumentText(run, EditorAgentReports.HeaderDocumentName(picked));
        Assert.Contains($"- Workflow type: {TYPE_NAME}", header, StringComparison.Ordinal);
        Assert.Contains($"- Counterparty: {ACME}", header, StringComparison.Ordinal);
        Assert.Contains("- Documents: acme-2025.pdf", header, StringComparison.Ordinal);
        Assert.Contains($"`{EditorAgentReports.RunDocumentName(picked)}`", header, StringComparison.Ordinal);

        JObject executed = JObject.Parse(DocumentText(run, EditorAgentReports.RunDocumentName(picked)));
        List<JObject> blocks = [.. executed.SelectTokens("Sections[*].Blocks[*]").OfType<JObject>()];
        JObject producer = blocks.Single(block => (string?)block["ReplacementTag"] == EditorAgentWorkflows.PRODUCER_TAG);
        Assert.Equal(PRODUCER_OUTPUT, (string?)producer.SelectToken("BlockResponse.RawResponse"));
        Assert.Equal("Completed", (string?)producer.SelectToken("ExecutionState.Status"));
        Assert.StartsWith(RESOLVED_PRODUCER_PROMPT, (string?)producer.SelectToken("Properties.ExecutionState.UserPrompt"), StringComparison.Ordinal);
        Assert.Null(producer[EditorAgentReports.TEMPLATE_STABLE_ID_KEY]);

        JObject reader = blocks.Single(block => (string?)block["ReplacementTag"] == EditorAgentWorkflows.READER_TAG);
        Assert.Equal(READER_OUTPUT_CHARS, ((string?)reader.SelectToken("BlockResponse.RawResponse"))!.Length);
        Assert.Contains(SOURCE_PAGE_TEXT, (string?)blocks.Single(block => (string?)block["Type"] == nameof(BlockTypes.SimpleRag)).SelectToken("BlockResponse.RawResponse"), StringComparison.Ordinal);

        List<JObject> copies = [.. blocks.Where(block => (string?)block[EditorAgentReports.TEMPLATE_STABLE_ID_KEY] == PERIOD_STABLE_ID)];
        Assert.Equal(["AR", "QR"], copies.Select(block => (string?)block[EditorAgentReports.VARIANT_KEY]));
        Assert.Equal(["assets_AR", "assets_QR"], copies.Select(block => (string?)block["ReplacementTag"]));
        Assert.All(copies, block => Assert.Equal(PERIOD_TAG, (string?)block[EditorAgentReports.TEMPLATE_TAG_KEY]));
        Assert.Equal(
            [string.Format(CultureInfo.InvariantCulture, PERIOD_OUTPUT, "AR"), string.Format(CultureInfo.InvariantCulture, PERIOD_OUTPUT, "QR")],
            copies.Select(block => (string?)block.SelectToken("BlockResponse.RawResponse")));
    }

    [Fact]
    public void ARunPastTheLimit_IsNotStaged_AndTheHeaderSaysSo_AndALargeOneIsCompressed()
    {
        Workflow executed = Executed(Guid.NewGuid().ToString(), ORG_ID, EditorAgentWorkflows.TYPE);
        var summary = new EditorAgentReportSummary("run-1", DateTime.UtcNow, ACME, "failed", "Big report", ["acme.pdf"]);
        long size = Encoding.UTF8.GetByteCount(JObject.Parse(executed.ToExportJson(includeRunTraces: true)).ToString());

        EditorAgentReportEvidence tooLarge = EditorAgentReports.Evidence(summary, "Timeout: the run stopped", executed, maxRunBytes: size / 2);
        EditorAgentReportEvidence staged = EditorAgentReports.Evidence(summary, failure: null, executed, maxRunBytes: long.MaxValue);

        AgentRunDocument header = Assert.Single(tooLarge.Documents);
        string text = Encoding.UTF8.GetString(header.Content.Span);
        Assert.Equal(EditorAgentReports.HeaderDocumentName("run-1"), header.Name);
        Assert.Contains("- Failure: Timeout: the run stopped", text, StringComparison.Ordinal);
        Assert.Contains("over the ", text, StringComparison.Ordinal);
        Assert.Contains("so it is not staged", text, StringComparison.Ordinal);
        Assert.DoesNotContain(PRODUCER_OUTPUT, tooLarge.RunTexts);
        Assert.Equal([EditorAgentReports.HeaderDocumentName("run-1"), EditorAgentReports.RunDocumentName("run-1")], staged.Documents.Select(static document => document.Name));
        Assert.Contains(PRODUCER_OUTPUT, staged.RunTexts);
        Assert.True(size < EditorAgentReports.COMPRESS_ABOVE_BYTES, "the seeded run is small enough to stay plain");
    }

    [Fact]
    public async Task SendMessageAsync_RefusesAReportOfAnotherOrganizationOrOfATypeWithoutAccess_AndMoreThanFive()
    {
        await SeedHiddenTypeAsync();
        string theirs = await SeedReportAsync(OTHER_ORG_ID, EditorAgentWorkflows.TYPE, ACME, WorkflowState.Draft, daysAgo: 0);
        string otherType = await SeedReportAsync(ORG_ID, HIDDEN_TYPE, ACME, WorkflowState.Draft, daysAgo: 0);
        string mine = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, ACME, WorkflowState.Draft, daysAgo: 0);
        EditorAgentConversationView conversation = await StartAsync();

        Task<ErrorOr<EditorAgentTurnView>> Send(params string[] ids) => Service.SendMessageAsync(
            new EditorAgentMessage(ORG_ID, USER_ID, conversation.Id, "Check this report", SONNET, EditorAgentWorkflows.Build())
            {
                ReportIds = ids,
            },
            TestContext.Current.CancellationToken);

        ErrorOr<EditorAgentTurnView> anotherOrganization = await Send(mine, theirs);
        ErrorOr<EditorAgentTurnView> anotherWorkflow = await Send(otherType);
        ErrorOr<EditorAgentTurnView> tooMany = await Send(mine, "b", "c", "d", "e", "f");

        Assert.Equal(EditorAgentErrors.REPORT_NOT_FOUND_CODE, anotherOrganization.FirstError.Code);
        Assert.Equal(EditorAgentErrors.REPORT_NOT_FOUND_CODE, anotherWorkflow.FirstError.Code);
        Assert.Equal(EditorAgentErrors.TOO_MANY_REPORTS_CODE, tooMany.FirstError.Code);
        Assert.Empty(_client.StartRequests);

        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        Assert.Empty(context.AgentConversationTurns);
        Assert.Empty(context.EditorAgentTurnReports);
    }

    [Fact]
    public async Task AgentOpensAReportMidTurn_TheSameTurnGoesOnWithOnlyTheOrganizationsReportStaged()
    {
        string mine = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, ACME, WorkflowState.Draft, daysAgo: 1);
        string theirs = await SeedReportAsync(OTHER_ORG_ID, EditorAgentWorkflows.TYPE, ACME, WorkflowState.Draft, daysAgo: 0);
        string otherType = await SeedReportAsync(ORG_ID, OTHER_TYPE, ACME, WorkflowState.Draft, daysAgo: 0);
        EditorAgentConversationView conversation = await StartAsync();
        string request = $$"""{"ids": ["{{mine}}", "{{theirs}}", "{{otherType}}", "../etc/passwd"]}""";
        AgentWritesOut(RunId(conversation.Id, 1), new() { [EditorAgentReportFetch.OPEN_REPORTS_FILE] = request });
        AgentWritesOut(RunId(conversation.Id, 2), new() { [EditorAgentReportFetch.OPEN_REPORTS_FILE] = request });

        EditorAgentTurnView turn = await SendAndSettleAsync(conversation.Id, EditorAgentWorkflows.Build());

        Assert.Equal(2, _client.StartRequests.Count);
        AgentRunStartRequest second = _client.StartRequests[1];
        Assert.Contains($"staged {EditorAgentReports.HeaderDocumentName(mine)}", second.Prompt, StringComparison.Ordinal);
        Assert.Contains("3 of the ids are not reports of this workflow in your organization", second.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(theirs, second.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(second.Documents, document => document.Name.Contains(otherType, StringComparison.Ordinal));
        Assert.DoesNotContain("../etc/passwd", second.Prompt, StringComparison.Ordinal);
        Assert.Contains(second.Documents, document => document.Name == EditorAgentReports.HeaderDocumentName(mine));
        Assert.Contains(second.Documents, document => document.Name == EditorAgentReports.RunDocumentName(mine));
        Assert.DoesNotContain(second.Documents, document => document.Name.Contains(theirs, StringComparison.Ordinal));
        Assert.DoesNotContain(_client.StartRequests[0].Documents, document => document.Name.Contains(mine, StringComparison.Ordinal));
        Assert.Equal(1, turn.Number);
        Assert.Equal($"answer of {RunId(conversation.Id, 2)}", turn.AnswerMarkdown);
        Assert.Equal(AgentConversationTurnStatus.Completed, turn.Status);
    }

    [Fact]
    public async Task PickedReport_OfADuplicatedType_IsMarkedAsACopyWhoseBlockIdsMatch_AndOneOfTheOpenTypeAsItsRun()
    {
        using (IServiceScope scope = _provider.CreateScope())
        {
            CoreDbContext core = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            WorkflowType copy = await core.Set<WorkflowType>().SingleAsync(type => type.Id == THIRD_TYPE, TestContext.Current.CancellationToken);
            copy.DuplicatedFromWorkflowTypeId = EditorAgentWorkflows.TYPE;
            _ = await core.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        string copied = await SeedReportAsync(ORG_ID, THIRD_TYPE, ACME, WorkflowState.Draft, daysAgo: 1);
        string own = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, GLOBEX, WorkflowState.Draft, daysAgo: 0);
        EditorAgentConversationView conversation = await StartAsync();

        EditorAgentTurnView turn = (await Service.SendMessageAsync(
            new EditorAgentMessage(ORG_ID, USER_ID, conversation.Id, "Compare the two runs", SONNET, EditorAgentWorkflows.Build())
            {
                ReportIds = [copied, own],
            },
            TestContext.Current.CancellationToken)).Value;
        await WaitForSettledAsync(conversation.Id, turn.Number);

        AgentRunStartRequest run = Assert.Single(_client.StartRequests);
        Assert.Contains(", copied from or into the open workflow, directly or through other copies: its block ids match this workflow's where the blocks still exist",
            DocumentText(run, EditorAgentReports.HeaderDocumentName(copied)), StringComparison.Ordinal);
        Assert.Contains($"- Workflow type: {TYPE_NAME}, the open workflow: this report is a run of it",
            DocumentText(run, EditorAgentReports.HeaderDocumentName(own)), StringComparison.Ordinal);
        Assert.Contains($"{EditorAgentReports.HeaderDocumentName(own)}: {TYPE_NAME}, the open workflow: ", run.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheOpenTypesLine_HoldsItsSourceSiblingsAndCopiesOfCopies_AndNothingElse()
    {
        using IServiceScope scope = _provider.CreateScope();
        CoreDbContext core = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        WorkflowType open = await core.Set<WorkflowType>().SingleAsync(type => type.Id == EditorAgentWorkflows.TYPE, TestContext.Current.CancellationToken);
        open.DuplicatedFromWorkflowTypeId = "WT-Root";
        _ = core.Add(new WorkflowType { Id = "WT-Root", Name = "Root" });
        _ = core.Add(new WorkflowType { Id = "WT-Sibling", Name = "Sibling", DuplicatedFromWorkflowTypeId = "WT-Root" });
        _ = core.Add(new WorkflowType { Id = "WT-Grandchild", Name = "Grandchild", DuplicatedFromWorkflowTypeId = "WT-Sibling" });
        _ = core.Add(new WorkflowType { Id = "WT-Child", Name = "Child", DuplicatedFromWorkflowTypeId = EditorAgentWorkflows.TYPE });
        _ = core.Add(new WorkflowType { Id = "WT-LoopA", Name = "Loop A", DuplicatedFromWorkflowTypeId = "WT-LoopB" });
        _ = core.Add(new WorkflowType { Id = "WT-LoopB", Name = "Loop B", DuplicatedFromWorkflowTypeId = "WT-LoopA" });
        _ = await core.SaveChangesAsync(TestContext.Current.CancellationToken);

        EditorAgentOpenWorkflowType line = await scope.ServiceProvider.GetRequiredService<EditorAgentReports>()
            .OpenWorkflowTypeAsync(EditorAgentWorkflows.TYPE, TestContext.Current.CancellationToken);
        EditorAgentOpenWorkflowType loop = await scope.ServiceProvider.GetRequiredService<EditorAgentReports>()
            .OpenWorkflowTypeAsync("WT-LoopA", TestContext.Current.CancellationToken);

        Assert.Equal(["WT-Child", "WT-Grandchild", "WT-Root", "WT-Sibling"], line.CopyTypeIds.Order(StringComparer.Ordinal));
        Assert.Equal(["WT-LoopB"], loop.CopyTypeIds);
    }

    /// <summary>A workflow type that exists but that the organization has no access to.</summary>
    private async Task SeedHiddenTypeAsync()
    {
        using IServiceScope scope = _provider.CreateScope();
        CoreDbContext core = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        _ = core.Add(new WorkflowType { Id = HIDDEN_TYPE, Name = "Hidden workflow" });
        _ = await core.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static string DocumentText(AgentRunStartRequest run, string name)
    {
        AgentRunDocument document = Assert.Single(run.Documents, document => document.Name == name);
        return Encoding.UTF8.GetString(document.Content.Span);
    }

    /// <summary>
    /// A past run with its counterparty, input documents and executed workflow: a document-text
    /// block, a producer with its resolved prompt, a reader with a long output, and a section
    /// that ran once per period.
    /// </summary>
    private async Task<string> SeedReportAsync(
        string organizationId,
        string workflowTypeId,
        string counterpartyName,
        WorkflowState state,
        int daysAgo,
        params string[] documentNames)
    {
        return await SeedReportAsync(organizationId, workflowTypeId, counterpartyName, state, daysAgo, deleted: false, documentNames);
    }

    private async Task<string> SeedReportAsync(
        string organizationId,
        string workflowTypeId,
        string counterpartyName,
        WorkflowState state,
        int daysAgo,
        bool deleted,
        params string[] documentNames)
    {
        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        var counterparty = new Counterparty(organizationId, counterpartyName);
        string jobId = Guid.NewGuid().ToString("N");
        DateTime createdAt = DateTime.UtcNow.AddDays(-daysAgo);
        var execution = new WorkflowExecution
        {
            WorkflowTypeId = workflowTypeId,
            OrganizationId = organizationId,
            Title = $"{counterpartyName} report",
            WorkflowState = state,
            Counterparty = counterparty,
            CounterpartyId = counterparty.Id,
            JobId = jobId,
            CreatedAt = createdAt,
            CompletedAt = state == WorkflowState.Running ? null : createdAt.AddMinutes(20),
            IsDeleted = deleted,
        };
        execution.WorkflowId = execution.Id;
        _ = context.Counterparties.Add(counterparty);
        _ = context.WorkflowExecutions.Add(execution);

        foreach (string name in documentNames)
        {
            var file = new FileMetadata(organizationId, name, 10, StorageProvider.Database);
            var document = new Document(file, MediaType.ApplicationPdf);
            _ = context.FileMetadata.Add(file);
            _ = context.Documents.Add(document);
            _ = context.DocumentInfos.Add(new DocumentInfo(document.Id) { JobId = jobId, DocumentClass = EditorAgentWorkflows.DOCUMENT_CLASS });
        }

        _ = context.Workflows.Add(Executed(execution.Id, organizationId, workflowTypeId));
        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return execution.Id;
    }

    private static Workflow Executed(string executionId, string organizationId, string workflowTypeId)
    {
        Workflow workflow = EditorAgentWorkflows.Build();
        workflow.Id = executionId;
        workflow.OrganizationId = organizationId;
        workflow.WorkflowTypeId = workflowTypeId;
        string pageText = string.Concat(Enumerable.Repeat(SOURCE_PAGE_TEXT, 10));

        Section section = workflow.Sections.Single();
        var retrieval = new Block(BlockTypes.SimpleRag, section, "context") { Name = "Annual report context", Order = 2 };
        _ = new BlockResponse(retrieval) { RawResponse = pageText };
        retrieval.ExecutionState = new BlockExecutionState(retrieval) { Status = ExecutionStatus.Completed };

        Block producer = EditorAgentWorkflows.Producer(workflow);
        producer.StableBlockId ??= Guid.NewGuid().ToString();
        _ = new BlockResponse(producer) { RawResponse = PRODUCER_OUTPUT };
        producer.ExecutionState = new BlockExecutionState(producer) { Status = ExecutionStatus.Completed };
        var properties = (AiQueryProperties)producer.Properties;
        properties.ExecutionState = new AiQueryPropertiesExecutionState(properties)
        {
            UserPrompt = $"{RESOLVED_PRODUCER_PROMPT} Context: {pageText} Notes: {pageText}",
            InputContext = pageText,
        };

        Block reader = EditorAgentWorkflows.Reader(workflow);
        _ = new BlockResponse(reader) { RawResponse = new string('y', READER_OUTPUT_CHARS) };
        reader.ExecutionState = new BlockExecutionState(reader) { Status = ExecutionStatus.Completed };

        var periods = new Section("Balance sheet", workflow)
        {
            Title = "Balance sheet",
            Order = 1,
            ParameterizedConfig = JsonConvert.SerializeObject(new ParameterizedSectionConfig([new SectionVariant("AR", "Annual"), new SectionVariant("QR", "Quarterly")])),
        };
        _ = new Block(BlockTypes.AiQuery, periods, PERIOD_TAG)
        {
            Name = "Non-current assets",
            StableBlockId = PERIOD_STABLE_ID,
            Properties = new AiQueryProperties { UserPromptTemplate = "Extract the non-current assets." },
        };
        List<Section> copies = ParameterizedSectionExpander.Expand(periods, ParameterizedSectionNormalizer.Normalize(periods)!);
        workflow.ReplaceSectionWithExpanded(periods, copies);
        foreach (Section copy in copies)
        {
            Block assets = copy.Blocks.Single();
            _ = new BlockResponse(assets) { RawResponse = string.Format(CultureInfo.InvariantCulture, PERIOD_OUTPUT, copy.ExpandedFromParameterizedKey) };
            assets.ExecutionState = new BlockExecutionState(assets) { Status = ExecutionStatus.Completed };
        }

        return workflow;
    }

    /// <summary>
    /// The workflow is a template other organizations share: a proposal that copies a report's
    /// output into a prompt is sent back, and the agent's general rewrite is what the user sees.
    /// </summary>
    [Fact]
    public async Task AProposalThatCopiesReportTextIntoAPrompt_IsSentBack_AndRefusedUntilWrittenInGeneralTerms()
    {
        string picked = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, ACME, WorkflowState.Draft, daysAgo: 1, "acme-2025.pdf");
        EditorAgentConversationView conversation = await StartAsync();
        Workflow workflow = EditorAgentWorkflows.Build();
        string draft = workflow.ToExportJson(includeRunTraces: true);
        string copied = "Break revenue down by segment in {Currency}. Example answer: " + PRODUCER_OUTPUT;
        string general = "Break revenue down by segment in {Currency}. Give the growth rate and its main driver.";
        AgentWrites(RunId(conversation.Id, 1), Edited(draft, EditorAgentWorkflows.PRODUCER_TAG, "UserPromptTemplate", copied), Summary(workflow, EditorAgentWorkflows.PRODUCER_TAG));
        AgentWrites(RunId(conversation.Id, 2), Edited(draft, EditorAgentWorkflows.PRODUCER_TAG, "UserPromptTemplate", general), Summary(workflow, EditorAgentWorkflows.PRODUCER_TAG));

        EditorAgentTurnView sent = (await Service.SendMessageAsync(
            new EditorAgentMessage(ORG_ID, USER_ID, conversation.Id, "Make the revenue answer more useful", SONNET, workflow)
            {
                ReportIds = [picked],
            },
            TestContext.Current.CancellationToken)).Value;
        await WaitForSettledAsync(conversation.Id, sent.Number);
        EditorAgentTurnView turn = await ReadTurnAsync(conversation.Id, sent.Number);

        Assert.Equal(2, _client.StartRequests.Count);
        Assert.Contains($"[{ProposalChecks.REPORT_TEXT}]", _client.StartRequests[1].Prompt, StringComparison.Ordinal);
        Assert.Contains("contains a passage from a past report", _client.StartRequests[1].Prompt, StringComparison.Ordinal);
        EditorAgentChangeView change = Assert.Single(turn.Proposal!.Changes);
        Assert.True(change.Change.CanAccept);
        Assert.Contains("growth rate", change.Change.NewJson, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportLeaks_CatchCopiedPassagesAndClientNames_ButNotTheWorkflowsOwnText()
    {
        Workflow executed = Executed(Guid.NewGuid().ToString(), ORG_ID, EditorAgentWorkflows.TYPE);
        var summary = new EditorAgentReportSummary("run-1", DateTime.UtcNow, ACME, "completed", "Report", ["acme-2025-annual.pdf"]);
        EditorAgentReportEvidence evidence = EditorAgentReports.Evidence(summary, failure: null, executed, long.MaxValue);
        var leaks = new EditorAgentReportLeaks([evidence], []);
        string template = EditorAgentWorkflows.Build().ToExportJson(includeRunTraces: true);
        List<string> workflowTexts = [.. JObject.Parse(template).SelectTokens("$..*").Where(static token => token.Type == JTokenType.String).Select(static token => (string)token!)];

        ProposedChange copied = Change("Keep it short, like: revenue   GREW 12% to 4.1bn,\ndriven by retail.");
        ProposedChange named = Change("Say whether Acme Holdings met its covenants.");
        ProposedChange document = Change("Read the ACME-2025-ANNUAL file first.");
        ProposedChange ownText = Change(RESOLVED_PRODUCER_PROMPT.Replace("USD", "{Currency}", StringComparison.Ordinal) + " Add the growth rate.");
        ProposedChange general = Change("When a segment grows fast, name its driver in one sentence.");
        ProposedChange pageText = Change("Quote this: " + SOURCE_PAGE_TEXT + SOURCE_PAGE_TEXT);

        IReadOnlyList<(ProposedChange Change, string What)> found = leaks.Find([copied, named, document, ownText, general, pageText], workflowTexts);

        Assert.Equal([copied, named, document, pageText], found.Select(static item => item.Change));
        Assert.Equal("a passage from a past report", found[0].What);
        Assert.Equal("a client or document name from a past report", found[1].What);
    }

    private static ProposedChange Change(string prompt)
    {
        return new ProposedChange
        {
            Id = Guid.NewGuid().ToString(),
            Scope = ProposedChangeScope.Block,
            Kind = ProposedChangeKind.FieldEdited,
            TargetKey = "stable-producer",
            Field = "Properties.UserPromptTemplate",
            BaseJson = Newtonsoft.Json.JsonConvert.SerializeObject("Break revenue down by segment in {Currency}."),
            NewJson = Newtonsoft.Json.JsonConvert.SerializeObject(prompt),
        };
    }
}
