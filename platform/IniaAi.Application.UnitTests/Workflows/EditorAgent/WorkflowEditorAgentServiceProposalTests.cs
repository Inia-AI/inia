using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ErrorOr;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Workflows.EditorAgent;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Proposals;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace IniaAi.Application.UnitTests.Workflows.EditorAgent;

/// <summary>
/// The agent proposes changes: the runtime stand-in plays the agent by writing out/workflow.json
/// and out/summary.json per run, and everything from the stop gate to the editor's apply and undo
/// is the real code.
/// </summary>
public sealed partial class WorkflowEditorAgentServiceTests
{
    private const string TRACE_PATH = "trace/trace.ndjson";
    private const string NEW_PRODUCER_PROMPT = "Break revenue down by segment and by region in {Currency}.";
    private const string USER_READER_PROMPT = "Summarize briefly: {revenue.answer}";
    private const string WEB_CALL_TAG = "notify";
    private const string SEARCH_TAG = "managerSearch";
    private const string INCLUDE_DOMAINS = "IncludeDomains";
    private const string NUM_RESULTS = "NumResults";

    private readonly ConcurrentDictionary<string, Dictionary<string, byte[]>> _outFiles = new();

    [Fact]
    public async Task BrokenEdit_IsSentBackWithItsErrors_AndTheFixedProposalNamesOnlyTheChangedBlock()
    {
        EditorAgentConversationView conversation = await StartAsync();
        Workflow workflow = EditorAgentWorkflows.Build();
        string draft = workflow.ToExportJson(includeRunTraces: true);
        JObject broken = Edited(draft, EditorAgentWorkflows.PRODUCER_TAG, "UserPromptTemplate", NEW_PRODUCER_PROMPT);
        Block(broken, EditorAgentWorkflows.PRODUCER_TAG)["Properties"]!["SystemPromptTemplat"] = "You are an analyst.";
        AgentWrites(RunId(conversation.Id, 1), broken, Summary(workflow, EditorAgentWorkflows.PRODUCER_TAG));
        AgentWrites(RunId(conversation.Id, 2), Edited(draft, EditorAgentWorkflows.PRODUCER_TAG, "UserPromptTemplate", NEW_PRODUCER_PROMPT), Summary(workflow, EditorAgentWorkflows.PRODUCER_TAG));

        EditorAgentTurnView turn = await SendAndSettleAsync(conversation.Id, workflow);

        Assert.Equal(2, _client.StartRequests.Count);
        Assert.Contains("SystemPromptTemplat", _client.StartRequests[1].Prompt, StringComparison.Ordinal);
        Assert.Contains($"[{ProposalChecks.SCHEMA}]", _client.StartRequests[1].Prompt, StringComparison.Ordinal);
        Assert.Equal($"answer of {RunId(conversation.Id, 2)}", turn.AnswerMarkdown);

        EditorAgentProposalView proposal = Assert.IsType<EditorAgentProposalView>(turn.Proposal);
        Assert.Equal(2, proposal.Rounds);
        Assert.Empty(proposal.CheckErrors);
        EditorAgentChangeView change = Assert.Single(proposal.Changes);
        Assert.Equal(EditorAgentWorkflows.Producer(workflow).StableBlockId, change.Change.TargetKey);
        Assert.Equal("Properties.UserPromptTemplate", change.Change.Field);
        Assert.Null(change.Decision);
    }

    [Fact]
    public async Task EditThatStaysBroken_StopsAfterThreeRounds_AndItsChangesCannotBeAccepted()
    {
        EditorAgentConversationView conversation = await StartAsync();
        Workflow workflow = EditorAgentWorkflows.Build();
        string draft = workflow.ToExportJson(includeRunTraces: true);
        for (int run = 1; run <= EditorAgentProposalGate.MAX_ROUNDS; run++)
        {
            JObject broken = Edited(draft, EditorAgentWorkflows.PRODUCER_TAG, "UserPromptTemplate", NEW_PRODUCER_PROMPT);
            Block(broken, EditorAgentWorkflows.PRODUCER_TAG)["Properties"]![$"SystemPromptTemplat{run}"] = "You are an analyst.";
            AgentWrites(RunId(conversation.Id, run), broken, Summary(workflow, EditorAgentWorkflows.PRODUCER_TAG));
        }

        EditorAgentTurnView turn = await SendAndSettleAsync(conversation.Id, workflow);

        Assert.Equal(EditorAgentProposalGate.MAX_ROUNDS, _client.StartRequests.Count);
        EditorAgentProposalView proposal = turn.Proposal!;
        Assert.Equal(EditorAgentProposalGate.MAX_ROUNDS, proposal.Rounds);
        ProposalCheckError schema = proposal.CheckErrors.First(error => error.Check == ProposalChecks.SCHEMA
            && error.Message.Contains("SystemPromptTemplat", StringComparison.Ordinal));
        Assert.Equal(EditorAgentWorkflows.Producer(workflow).StableBlockId, schema.BlockKey);
        Assert.All(proposal.Changes, change => Assert.False(change.Change.CanAccept));

        ErrorOr<EditorAgentApplyResult> accepted = await Service.AcceptChangesAsync(
            new EditorAgentAcceptRequest(ORG_ID, USER_ID, turn.Id, [proposal.Changes[0].Change.Id], workflow),
            TestContext.Current.CancellationToken);

        Assert.Empty(accepted.Value.Applied);
        _ = Assert.Single(accepted.Value.NotApplied);
        Assert.Equal(EditorAgentWorkflows.PRODUCER_TAG, EditorAgentWorkflows.Producer(workflow).ReplacementTag);
        Assert.NotEqual(NEW_PRODUCER_PROMPT, ((AiQueryProperties)EditorAgentWorkflows.Producer(workflow).Properties).UserPromptTemplate);
    }

    [Fact]
    public async Task DeletedListSetting_IsSentBackWithHowToClearIt_AndTheFixedProposalApplies()
    {
        EditorAgentConversationView conversation = await StartAsync();
        Workflow workflow = WithSearch(EditorAgentWorkflows.Build());
        string draft = workflow.ToExportJson(includeRunTraces: true);
        AgentWrites(RunId(conversation.Id, 1), WithoutDomains(draft), Summary(workflow, SEARCH_TAG));
        AgentWrites(RunId(conversation.Id, 2), Edited(draft, SEARCH_TAG, INCLUDE_DOMAINS, new JArray()), Summary(workflow, SEARCH_TAG));

        EditorAgentTurnView turn = await SendAndSettleAsync(conversation.Id, workflow);

        Assert.Equal(2, _client.StartRequests.Count);
        string feedback = _client.StartRequests[1].Prompt;
        Assert.Contains($"[{ProposalChecks.APPLY}]", feedback, StringComparison.Ordinal);
        Assert.Contains($"you deleted the setting `Properties.{INCLUDE_DOMAINS}`", feedback, StringComparison.Ordinal);
        Assert.Contains("set it to an empty list `[]`", feedback, StringComparison.Ordinal);

        EditorAgentProposalView proposal = Assert.IsType<EditorAgentProposalView>(turn.Proposal);
        Assert.Empty(proposal.CheckErrors);
        EditorAgentChangeView change = Assert.Single(proposal.Changes);
        Assert.True(change.Change.CanAccept);

        ErrorOr<EditorAgentApplyResult> accepted = await Service.AcceptChangesAsync(
            new EditorAgentAcceptRequest(ORG_ID, USER_ID, turn.Id, [change.Change.Id], workflow),
            TestContext.Current.CancellationToken);

        _ = Assert.Single(accepted.Value.Applied);
        Assert.Empty(Search(workflow).IncludeDomains);
    }

    [Fact]
    public async Task DeletedNumberSetting_IsSentBackAsNeedingAValue()
    {
        EditorAgentConversationView conversation = await StartAsync();
        Workflow workflow = WithSearch(EditorAgentWorkflows.Build());
        string draft = workflow.ToExportJson(includeRunTraces: true);
        JObject deleted = JObject.Parse(draft);
        _ = ((JObject)Block(deleted, SEARCH_TAG)["Properties"]!).Remove(NUM_RESULTS);
        AgentWrites(RunId(conversation.Id, 1), deleted, Summary(workflow, SEARCH_TAG));
        AgentWrites(RunId(conversation.Id, 2), Edited(draft, SEARCH_TAG, NUM_RESULTS, new JValue(15)), Summary(workflow, SEARCH_TAG));

        EditorAgentTurnView turn = await SendAndSettleAsync(conversation.Id, workflow);

        Assert.Equal(2, _client.StartRequests.Count);
        Assert.Contains($"you deleted the setting `Properties.{NUM_RESULTS}`, which must always have a value", _client.StartRequests[1].Prompt, StringComparison.Ordinal);
        Assert.True(Assert.Single(turn.Proposal!.Changes).Change.CanAccept);
    }

    [Fact]
    public async Task SameErrorTwice_StopsBeforeTheLastRound_AndTheChangeCannotBeAccepted()
    {
        EditorAgentConversationView conversation = await StartAsync();
        Workflow workflow = WithSearch(EditorAgentWorkflows.Build());
        string draft = workflow.ToExportJson(includeRunTraces: true);
        for (int run = 1; run <= EditorAgentProposalGate.MAX_ROUNDS; run++)
        {
            AgentWrites(RunId(conversation.Id, run), WithoutDomains(draft), Summary(workflow, SEARCH_TAG));
        }

        EditorAgentTurnView turn = await SendAndSettleAsync(conversation.Id, workflow);

        Assert.Equal(2, _client.StartRequests.Count);
        EditorAgentProposalView proposal = turn.Proposal!;
        ProposalCheckError error = Assert.Single(proposal.CheckErrors, error => error.Check == ProposalChecks.APPLY);
        Assert.Equal(workflow.Sections.Single().Blocks.Single(block => block.ReplacementTag == SEARCH_TAG).StableBlockId, error.BlockKey);
        EditorAgentChangeView change = Assert.Single(proposal.Changes);
        Assert.False(change.Change.CanAccept);

        ErrorOr<EditorAgentApplyResult> accepted = await Service.AcceptChangesAsync(
            new EditorAgentAcceptRequest(ORG_ID, USER_ID, turn.Id, [change.Change.Id], workflow),
            TestContext.Current.CancellationToken);

        Assert.Empty(accepted.Value.Applied);
        Assert.Equal(["{ParentURL.answer}"], Search(workflow).IncludeDomains);
    }

    /// <summary>
    /// The user keeps editing while the agent works. Accept all lands the agent's changes on the
    /// live workflow except the field the user also changed (a conflict carrying both values) and
    /// the web call's address (it needs its own accept); undo takes them all back.
    /// </summary>
    [Fact]
    public async Task AcceptAll_AppliesWhatDidNotConflict_SkipsOutboundChanges_AndUndoTakesThemBack()
    {
        EditorAgentConversationView conversation = await StartAsync();
        Workflow workflow = WithWebCall(EditorAgentWorkflows.Build());
        string draft = workflow.ToExportJson(includeRunTraces: true);
        JObject proposed = Edited(draft, EditorAgentWorkflows.PRODUCER_TAG, "UserPromptTemplate", NEW_PRODUCER_PROMPT);
        Block(proposed, EditorAgentWorkflows.READER_TAG)["Properties"]!["UserPromptTemplate"] = "Summarize in three bullets: {revenue.answer}";
        Block(proposed, EditorAgentWorkflows.READER_TAG)["Name"] = "Executive summary";
        Block(proposed, WEB_CALL_TAG)["Properties"]!["UrlTemplate"] = "https://elsewhere.example.net/collect";
        AgentWrites(RunId(conversation.Id, 1), proposed, Summary(workflow, EditorAgentWorkflows.PRODUCER_TAG, EditorAgentWorkflows.READER_TAG, WEB_CALL_TAG));

        EditorAgentTurnView turn = await SendAndSettleAsync(conversation.Id, workflow);
        ((AiQueryProperties)EditorAgentWorkflows.Reader(workflow).Properties).UserPromptTemplate = USER_READER_PROMPT;
        string? addressBefore = WebCall(workflow).UrlTemplate;

        EditorAgentApplyResult result = (await Service.AcceptChangesAsync(
            new EditorAgentAcceptRequest(ORG_ID, USER_ID, turn.Id, null, workflow), TestContext.Current.CancellationToken)).Value;

        Assert.Equal(2, result.Applied.Count);
        ProposalConflict conflict = Assert.Single(result.Conflicts);
        Assert.Equal("Properties.UserPromptTemplate", conflict.Field);
        Assert.Equal(USER_READER_PROMPT, JToken.Parse(conflict.LiveJson!).Value<string>());
        Assert.Equal(NEW_PRODUCER_PROMPT, ((AiQueryProperties)EditorAgentWorkflows.Producer(workflow).Properties).UserPromptTemplate);
        Assert.Equal("Executive summary", EditorAgentWorkflows.Reader(workflow).Name);
        Assert.Equal(USER_READER_PROMPT, ((AiQueryProperties)EditorAgentWorkflows.Reader(workflow).Properties).UserPromptTemplate);
        Assert.Equal(addressBefore, WebCall(workflow).UrlTemplate);

        EditorAgentProposalView view = (await ReadTurnAsync(conversation.Id)).Proposal!;
        Assert.True(view.CanUndo);
        Assert.True(Assert.Single(view.Changes, change => change.Change.Field == "Properties.UrlTemplate").Change.SendsDataOutside);
        Assert.Equal(2, view.Changes.Count(change => change.Decision == EditorAgentChangeDecisionStatus.Accepted));

        EditorAgentUndoResult undone = (await Service.UndoChangesAsync(
            ORG_ID, USER_ID, turn.Id, workflow, cancellationToken: TestContext.Current.CancellationToken)).Value;

        Assert.Equal(2, undone.UndoneChangeIds.Count);
        Assert.Equal(EditorAgentWorkflows.READER_NAME, EditorAgentWorkflows.Reader(workflow).Name);
        Assert.NotEqual(NEW_PRODUCER_PROMPT, ((AiQueryProperties)EditorAgentWorkflows.Producer(workflow).Properties).UserPromptTemplate);
        Assert.Equal(USER_READER_PROMPT, ((AiQueryProperties)EditorAgentWorkflows.Reader(workflow).Properties).UserPromptTemplate);
        Assert.False((await ReadTurnAsync(conversation.Id)).Proposal!.CanUndo);
        Assert.Equal(
            EditorAgentErrors.NOTHING_TO_UNDO_CODE,
            (await Service.UndoChangesAsync(ORG_ID, USER_ID, turn.Id, workflow, cancellationToken: TestContext.Current.CancellationToken)).FirstError.Code);
    }

    [Fact]
    public async Task OutboundChange_AppliesOnlyWhenNamed_AndRejectMarksTheRest()
    {
        EditorAgentConversationView conversation = await StartAsync();
        Workflow workflow = WithWebCall(EditorAgentWorkflows.Build());
        string draft = workflow.ToExportJson(includeRunTraces: true);
        JObject proposed = Edited(draft, WEB_CALL_TAG, "UrlTemplate", "https://elsewhere.example.net/collect");
        Block(proposed, EditorAgentWorkflows.READER_TAG)["Name"] = "Executive summary";
        AgentWrites(RunId(conversation.Id, 1), proposed, Summary(workflow, EditorAgentWorkflows.READER_TAG, WEB_CALL_TAG));
        EditorAgentTurnView turn = await SendAndSettleAsync(conversation.Id, workflow);
        string outboundId = turn.Proposal!.Changes.Single(change => change.Change.SendsDataOutside).Change.Id;

        EditorAgentApplyResult named = (await Service.AcceptChangesAsync(
            new EditorAgentAcceptRequest(ORG_ID, USER_ID, turn.Id, [outboundId], workflow), TestContext.Current.CancellationToken)).Value;
        ErrorOr<Success> rejected = await Service.RejectChangesAsync(ORG_ID, USER_ID, turn.Id, null, TestContext.Current.CancellationToken);

        Assert.Equal(outboundId, Assert.Single(named.Applied).ChangeId);
        Assert.Equal("https://elsewhere.example.net/collect", WebCall(workflow).UrlTemplate);
        Assert.False(rejected.IsError);
        EditorAgentProposalView view = (await ReadTurnAsync(conversation.Id)).Proposal!;
        Assert.Equal(EditorAgentChangeDecisionStatus.Accepted, view.Changes.Single(change => change.Change.Id == outboundId).Decision);
        Assert.Equal(EditorAgentChangeDecisionStatus.Rejected, view.Changes.Single(change => change.Change.Id != outboundId).Decision);
        Assert.Equal(EditorAgentWorkflows.READER_NAME, EditorAgentWorkflows.Reader(workflow).Name);
    }

    [Fact]
    public async Task AddedBlock_GetsARealStableIdOnAccept_AndTheSaveNoteCountsEachChangedBlockOnce()
    {
        EditorAgentConversationView conversation = await StartAsync();
        Workflow workflow = EditorAgentWorkflows.Build();
        string draft = workflow.ToExportJson(includeRunTraces: true);
        JObject proposed = Edited(draft, EditorAgentWorkflows.PRODUCER_TAG, "UserPromptTemplate", NEW_PRODUCER_PROMPT);
        Block(proposed, EditorAgentWorkflows.PRODUCER_TAG)["Name"] = "Revenue split";
        var added = (JObject)Block(proposed, EditorAgentWorkflows.READER_TAG).DeepClone();
        added["StableBlockId"] = "new-risk-check";
        added["Id"] = Guid.NewGuid().ToString();
        added["ReplacementTag"] = "risk";
        added["Name"] = "Risk check";
        added["Order"] = 2;
        ((JArray)proposed["Sections"]![0]!["Blocks"]!).Add(added);
        AgentWrites(RunId(conversation.Id, 1), proposed, Summary(workflow, EditorAgentWorkflows.PRODUCER_TAG, "new-risk-check"));
        EditorAgentTurnView turn = await SendAndSettleAsync(conversation.Id, workflow);

        EditorAgentApplyResult result = (await Service.AcceptChangesAsync(
            new EditorAgentAcceptRequest(ORG_ID, USER_ID, turn.Id, null, workflow), TestContext.Current.CancellationToken)).Value;
        EditorAgentSaveNote? note = (await Service.PeekSaveNoteAsync(ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, TestContext.Current.CancellationToken)).Value;
        EditorAgentSaveNote? beforeCommit = (await Service.PeekSaveNoteAsync(ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, TestContext.Current.CancellationToken)).Value;
        _ = await Service.CommitSaveNoteAsync(ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, note!, "version-1", TestContext.Current.CancellationToken);
        EditorAgentSaveNote? afterCommit = (await Service.PeekSaveNoteAsync(ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, TestContext.Current.CancellationToken)).Value;

        Block risk = workflow.Sections.Single().Blocks.Single(block => block.ReplacementTag == "risk");
        Assert.Equal(Assert.Single(result.Applied, applied => applied.MintedStableId is not null).MintedStableId, risk.StableBlockId);
        Assert.NotEqual("new-risk-check", risk.StableBlockId);
        Assert.Equal(string.Format(System.Globalization.CultureInfo.InvariantCulture, WorkflowEditorAgentService.SAVE_NOTE_MANY, 2), note!.Text);
        Assert.Equal(note.Text, beforeCommit!.Text);
        Assert.Null(afterCommit);
    }

    [Fact]
    public async Task AcceptingOnlyAnAddedBlock_PutsItWhereTheAgentDid_AndTheSaveNoteCarriesTheOverview()
    {
        const string overview = "Adds a risk check before the revenue breakdown.";
        EditorAgentConversationView conversation = await StartAsync();
        Workflow workflow = EditorAgentWorkflows.Build();
        string draft = workflow.ToExportJson(includeRunTraces: true);
        JObject proposed = JObject.Parse(draft);
        var blocks = new JArray(((JArray)proposed["Sections"]![0]!["Blocks"]!).OrderBy(static block => (int)block["Order"]!));
        proposed["Sections"]![0]!["Blocks"] = blocks;
        var added = (JObject)Block(proposed, EditorAgentWorkflows.READER_TAG).DeepClone();
        added["StableBlockId"] = "new-risk-check";
        added["Id"] = Guid.NewGuid().ToString();
        added["ReplacementTag"] = "risk";
        added["Name"] = "Risk check";
        blocks.Insert(0, added);
        for (int i = 0; i < blocks.Count; i++)
        {
            blocks[i]["Order"] = i;
        }

        JObject summary = Summary(workflow, "new-risk-check");
        summary["overview"] = overview;
        AgentWrites(RunId(conversation.Id, 1), proposed, summary);
        EditorAgentTurnView turn = await SendAndSettleAsync(conversation.Id, workflow);
        EditorAgentProposalView proposal = turn.Proposal!;
        string addedId = Assert.Single(proposal.Changes, view => view.Change.Kind == ProposedChangeKind.Added).Change.Id;

        _ = await Service.AcceptChangesAsync(
            new EditorAgentAcceptRequest(ORG_ID, USER_ID, turn.Id, [addedId], workflow), TestContext.Current.CancellationToken);
        EditorAgentSaveNote? note = (await Service.PeekSaveNoteAsync(ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, TestContext.Current.CancellationToken)).Value;

        Assert.Equal(overview, proposal.Overview);
        Assert.All(proposal.Changes.Where(view => view.Change.Field == "Order"), view => Assert.True(view.Change.Incidental));
        Assert.Equal("risk", workflow.Sections.Single().Blocks.OrderBy(block => block.Order).First().ReplacementTag);
        Assert.Equal($"{WorkflowEditorAgentService.SAVE_NOTE_ONE}. {overview}", note!.Text);
    }

    /// <summary>
    /// A resumed session carries the previous turn's out/ files into the next turn; an unchanged
    /// workflow file there is the earlier proposal, not a new one.
    /// </summary>
    [Fact]
    public async Task WorkflowFileCarriedIntoTheNextTurn_IsNotProposedAgain()
    {
        EditorAgentConversationView conversation = await StartAsync();
        Workflow workflow = EditorAgentWorkflows.Build();
        string draft = workflow.ToExportJson(includeRunTraces: true);
        JObject proposed = Edited(draft, EditorAgentWorkflows.PRODUCER_TAG, "UserPromptTemplate", NEW_PRODUCER_PROMPT);
        AgentWrites(RunId(conversation.Id, 1), proposed, Summary(workflow, EditorAgentWorkflows.PRODUCER_TAG));
        AgentWrites($"{WorkflowEditorAgentService.PURPOSE}-{conversation.Id}-t2", proposed, Summary(workflow, EditorAgentWorkflows.PRODUCER_TAG));
        _ = await SendAndSettleAsync(conversation.Id, workflow);

        _ = (await SendAsync(conversation.Id, workflow)).Value;
        await WaitForSettledAsync(conversation.Id, 2);

        EditorAgentConversationView view = (await Service.GetConversationAsync(
            ORG_ID, USER_ID, conversation.Id, TestContext.Current.CancellationToken)).Value;
        Assert.NotNull(view.Turns[0].Proposal);
        Assert.Null(view.Turns[1].Proposal);
    }

    [Fact]
    public async Task SaveChecks_ReportUnknownAgentTagsAndSignatureDeletionsTheValidateEndpointSkips()
    {
        Workflow previous = EditorAgentWorkflows.Build();
        _ = new WorkflowSignatureField(previous, "Lender") { DisplayName = "Lender" };
        Workflow candidate = EditorAgentWorkflows.Build();
        _ = new Block(BlockTypes.Agent, candidate.Sections.Single(), "analyst")
        {
            Name = "Analyst",
            Properties = new AgentProperties { PromptTemplate = "Review {{nosuchtag}} and answer." },
        };
        using (IServiceScope seed = _provider.CreateScope())
        {
            CoreDbContext core = seed.ServiceProvider.GetRequiredService<CoreDbContext>();
            _ = core.SignatureFieldOverrides.Add(new SignatureFieldOverride
            {
                OrganizationId = ORG_ID,
                WorkflowTypeId = EditorAgentWorkflows.TYPE,
                PlaceholderName = "Lender",
                Value = "Bank of Example",
            });
            _ = await core.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using IServiceScope scope = _provider.CreateScope();
        List<WorkflowCheckFailure> errors = await scope.ServiceProvider.GetRequiredService<WorkflowSaveChecks>().CheckAsync(
            EditorAgentWorkflows.TYPE, candidate.ToExportJson(includeRunTraces: true), previous, "user-checks", TestContext.Current.CancellationToken);

        Assert.Contains(errors, error => error.Check == WorkflowCheck.AgentTags && error.Message.Contains("nosuchtag", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Check == WorkflowCheck.SignatureFields && error.Message.Contains("Lender", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, error => error.Check == WorkflowCheck.Schema);
    }

    [Fact]
    public async Task OutboundChange_NamedBesideAnotherChange_DoesNotApply_UnlessConfirmed()
    {
        EditorAgentConversationView conversation = await StartAsync();
        Workflow workflow = WithWebCall(EditorAgentWorkflows.Build());
        string draft = workflow.ToExportJson(includeRunTraces: true);
        JObject proposed = Edited(draft, WEB_CALL_TAG, "UrlTemplate", "https://elsewhere.example.net/collect");
        Block(proposed, EditorAgentWorkflows.READER_TAG)["Name"] = "Executive summary";
        AgentWrites(RunId(conversation.Id, 1), proposed, Summary(workflow, EditorAgentWorkflows.READER_TAG, WEB_CALL_TAG));
        EditorAgentTurnView turn = await SendAndSettleAsync(conversation.Id, workflow);
        List<string> ids = [.. turn.Proposal!.Changes.Select(static change => change.Change.Id)];
        string outboundId = turn.Proposal.Changes.Single(change => change.Change.SendsDataOutside).Change.Id;

        EditorAgentApplyResult together = (await Service.AcceptChangesAsync(
            new EditorAgentAcceptRequest(ORG_ID, USER_ID, turn.Id, ids, workflow), TestContext.Current.CancellationToken)).Value;

        Assert.Equal(WorkflowEditorAgentService.OUTBOUND_ALONE_REASON, Assert.Single(together.NotApplied).Reason);
        Assert.Equal("https://hooks.example.com/report", WebCall(workflow).UrlTemplate);

        EditorAgentApplyResult confirmed = (await Service.AcceptChangesAsync(
            new EditorAgentAcceptRequest(ORG_ID, USER_ID, turn.Id, ids, workflow) { ConfirmOutbound = [outboundId] },
            TestContext.Current.CancellationToken)).Value;

        Assert.Equal(outboundId, Assert.Single(confirmed.Applied).ChangeId);
        Assert.Equal("https://elsewhere.example.net/collect", WebCall(workflow).UrlTemplate);
    }

    [Fact]
    public async Task ChangeThatFeedsAWebCall_SendsDataOutside_AndAcceptAllSkipsItAndOneMissingFromTheSummary()
    {
        EditorAgentConversationView conversation = await StartAsync();
        Workflow workflow = WithWebCall(EditorAgentWorkflows.Build());
        WebCall(workflow).RequestPayloadTemplate = "{\"text\": \"{summary}\"}";
        string draft = workflow.ToExportJson(includeRunTraces: true);
        JObject proposed = Edited(draft, EditorAgentWorkflows.PRODUCER_TAG, "UserPromptTemplate", NEW_PRODUCER_PROMPT);
        Block(proposed, EditorAgentWorkflows.READER_TAG)["Name"] = "Executive summary";
        AgentWrites(RunId(conversation.Id, 1), proposed, Summary(workflow, EditorAgentWorkflows.PRODUCER_TAG));
        EditorAgentTurnView turn = await SendAndSettleAsync(conversation.Id, workflow);

        EditorAgentApplyResult result = (await Service.AcceptChangesAsync(
            new EditorAgentAcceptRequest(ORG_ID, USER_ID, turn.Id, null, workflow), TestContext.Current.CancellationToken)).Value;

        Assert.True(turn.Proposal!.Changes.Single(change => change.Change.Field == "Properties.UserPromptTemplate").Change.SendsDataOutside);
        Assert.True(turn.Proposal.Changes.Single(change => change.Change.Field == "Name").Change.MissingFromSummary);
        Assert.Empty(result.Applied);
    }

    [Fact]
    public async Task ErrorTheWorkflowAlreadyHad_RefusesNothing_AndSendsNothingBack()
    {
        EditorAgentConversationView conversation = await StartAsync();
        Workflow workflow = EditorAgentWorkflows.Build();
        _ = new Block(BlockTypes.Merge, workflow.Sections.Single(), "library_copy")
        {
            Name = "Library copy",
            Properties = new MergeProperties { TextTemplate = "{summary}" },
            SharedComponentId = "missing-component",
            SharedComponentLinkState = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentLinkState.Linked,
            SharedComponentVersionHash = new string('a', 64),
        };
        string draft = workflow.ToExportJson(includeRunTraces: true);
        AgentWrites(RunId(conversation.Id, 1), Edited(draft, EditorAgentWorkflows.PRODUCER_TAG, "UserPromptTemplate", NEW_PRODUCER_PROMPT), Summary(workflow, EditorAgentWorkflows.PRODUCER_TAG));

        EditorAgentTurnView turn = await SendAndSettleAsync(conversation.Id, workflow);

        _ = Assert.Single(_client.StartRequests);
        Assert.Empty(turn.Proposal!.CheckErrors);
        Assert.True(Assert.Single(turn.Proposal.Changes).Change.CanAccept);
    }

    [Fact]
    public async Task UnknownAgentTag_IsAWarning_NotAnError()
    {
        EditorAgentConversationView conversation = await StartAsync();
        Workflow workflow = EditorAgentWorkflows.Build();
        string draft = workflow.ToExportJson(includeRunTraces: true);
        JObject proposed = JObject.Parse(draft);
        var agent = (JObject)Block(proposed, EditorAgentWorkflows.READER_TAG).DeepClone();
        agent["StableBlockId"] = "new-analyst";
        agent["Id"] = Guid.NewGuid().ToString();
        agent["ReplacementTag"] = "analyst";
        agent["Type"] = "Agent";
        agent["Properties"] = JObject.Parse(JsonOf(new AgentProperties { PromptTemplate = "Review {{nosuchtag}} and answer." }));
        ((JArray)proposed["Sections"]![0]!["Blocks"]!).Add(agent);
        AgentWrites(RunId(conversation.Id, 1), proposed, Summary(workflow, "new-analyst"));

        EditorAgentTurnView turn = await SendAndSettleAsync(conversation.Id, workflow);

        Assert.DoesNotContain(turn.Proposal!.CheckErrors, error => error.Check == ProposalChecks.AGENT_TAGS);
        Assert.Contains(turn.Proposal.Warnings, warning => warning.Check == ProposalChecks.AGENT_TAGS && warning.Message.Contains("nosuchtag", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TagAParameterizedSectionCannotFill_IsAWarning_NotAnError()
    {
        EditorAgentConversationView conversation = await StartAsync();
        Workflow workflow = EditorAgentWorkflows.Build();
        string draft = workflow.ToExportJson(includeRunTraces: true);
        Workflow proposed = Workflow.FromExportJson(draft)!;
        var parameterized = new Section("Per region", proposed) { ParameterizedConfig = """{"Variants":[{"Key":"EU","Label":"EU"}]}""" };
        _ = new Block(BlockTypes.Merge, parameterized, "param_tag") { Properties = new MergeProperties { TextTemplate = "per variant" } };
        _ = new Block(BlockTypes.Agent, proposed.Sections.First(), "analyst")
        {
            Properties = new AgentProperties { PromptTemplate = "Review {{param_tag.answer}} and answer." },
        };
        AgentWrites(RunId(conversation.Id, 1), JObject.Parse(proposed.ToExportJson()), Summary(workflow, "analyst", "param_tag"));

        EditorAgentTurnView turn = await SendAndSettleAsync(conversation.Id, workflow);

        Assert.DoesNotContain(turn.Proposal!.CheckErrors, error => error.Message.Contains("param_tag_<variant key>", StringComparison.Ordinal));
        Assert.Contains(turn.Proposal.Warnings, warning => warning.Check == ProposalChecks.AGENT_TAGS
            && warning.Message.Contains("param_tag_<variant key>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithoutResumedSessions_TheGateRoundIsHandedThePreviousAttempt()
    {
        _editor.ResumeSessions = false;
        EditorAgentConversationView conversation = await StartAsync();
        Workflow workflow = EditorAgentWorkflows.Build();
        string draft = workflow.ToExportJson(includeRunTraces: true);
        JObject broken = Edited(draft, EditorAgentWorkflows.PRODUCER_TAG, "UserPromptTemplate", NEW_PRODUCER_PROMPT);
        Block(broken, EditorAgentWorkflows.PRODUCER_TAG)["Properties"]!["SystemPromptTemplat"] = "You are an analyst.";
        AgentWrites(RunId(conversation.Id, 1), broken, Summary(workflow, EditorAgentWorkflows.PRODUCER_TAG));
        AgentWrites(RunId(conversation.Id, 2), Edited(draft, EditorAgentWorkflows.PRODUCER_TAG, "UserPromptTemplate", NEW_PRODUCER_PROMPT), Summary(workflow, EditorAgentWorkflows.PRODUCER_TAG));

        _ = await SendAndSettleAsync(conversation.Id, workflow);

        AgentRunStartRequest second = _client.StartRequests[1];
        Assert.Null(second.Continuation);
        Assert.Contains(second.Documents, document => document.Name == "previous-attempt.json");
        Assert.Contains(second.Documents, document => document.Name == "previous-attempt-summary.json");
        Assert.Contains(second.Documents, document => document.Name == "previous-attempt-answer.md");
        Assert.Contains("docs/previous-attempt.json", second.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Undo_LeavesWhatTheUserChangedAfterAccepting_AndKeepsThatChangeAccepted()
    {
        EditorAgentConversationView conversation = await StartAsync();
        Workflow workflow = EditorAgentWorkflows.Build();
        string draft = workflow.ToExportJson(includeRunTraces: true);
        JObject proposed = Edited(draft, EditorAgentWorkflows.PRODUCER_TAG, "UserPromptTemplate", NEW_PRODUCER_PROMPT);
        Block(proposed, EditorAgentWorkflows.READER_TAG)["Name"] = "Executive summary";
        AgentWrites(RunId(conversation.Id, 1), proposed, Summary(workflow, EditorAgentWorkflows.PRODUCER_TAG, EditorAgentWorkflows.READER_TAG));
        EditorAgentTurnView turn = await SendAndSettleAsync(conversation.Id, workflow);
        _ = await Service.AcceptChangesAsync(
            new EditorAgentAcceptRequest(ORG_ID, USER_ID, turn.Id, null, workflow), TestContext.Current.CancellationToken);
        EditorAgentWorkflows.Reader(workflow).Name = "My summary";

        EditorAgentUndoResult undone = (await Service.UndoChangesAsync(
            ORG_ID, USER_ID, turn.Id, workflow, cancellationToken: TestContext.Current.CancellationToken)).Value;

        Assert.Equal("Name", Assert.Single(undone.Conflicts).Field);
        Assert.Equal("My summary", EditorAgentWorkflows.Reader(workflow).Name);
        Assert.NotEqual(NEW_PRODUCER_PROMPT, ((AiQueryProperties)EditorAgentWorkflows.Producer(workflow).Properties).UserPromptTemplate);
        EditorAgentProposalView view = (await ReadTurnAsync(conversation.Id)).Proposal!;
        Assert.Equal(EditorAgentChangeDecisionStatus.Accepted, view.Changes.Single(change => change.Change.Field == "Name").Decision);
    }

    [Fact]
    public async Task AcceptAfterTheEditorRoleWasTaken_IsForbidden()
    {
        EditorAgentConversationView conversation = await StartAsync();
        Workflow workflow = EditorAgentWorkflows.Build();
        string draft = workflow.ToExportJson(includeRunTraces: true);
        AgentWrites(RunId(conversation.Id, 1), Edited(draft, EditorAgentWorkflows.PRODUCER_TAG, "UserPromptTemplate", NEW_PRODUCER_PROMPT), Summary(workflow, EditorAgentWorkflows.PRODUCER_TAG));
        EditorAgentTurnView turn = await SendAndSettleAsync(conversation.Id, workflow);
        using (IServiceScope scope = _provider.CreateScope())
        {
            UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
            context.UserRoles.RemoveRange(context.UserRoles.Where(role => role.UserId == USER_ID));
            _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        ErrorOr<EditorAgentApplyResult> accepted = await Service.AcceptChangesAsync(
            new EditorAgentAcceptRequest(ORG_ID, USER_ID, turn.Id, null, workflow), TestContext.Current.CancellationToken);

        Assert.Equal(EditorAgentErrors.FORBIDDEN_CODE, accepted.FirstError.Code);
    }

    private static string JsonOf(object properties)
    {
        return Newtonsoft.Json.JsonConvert.SerializeObject(properties);
    }

    private async Task<EditorAgentTurnView> SendAndSettleAsync(string conversationId, Workflow workflow)
    {
        EditorAgentTurnView sent = (await SendAsync(conversationId, workflow)).Value;
        await WaitForSettledAsync(conversationId, sent.Number);

        return (await ReadTurnAsync(conversationId, sent.Number))!;
    }

    private async Task<EditorAgentTurnView> ReadTurnAsync(string conversationId, int number = 1)
    {
        EditorAgentConversationView view = (await Service.GetConversationAsync(
            ORG_ID, USER_ID, conversationId, TestContext.Current.CancellationToken)).Value;

        return view.Turns.Single(turn => turn.Number == number);
    }

    private static string RunId(string conversationId, int round)
    {
        string first = $"{WorkflowEditorAgentService.PURPOSE}-{conversationId}-t1";
        return round == 1 ? first : $"{first}-p{round}";
    }

    private static Workflow WithWebCall(Workflow workflow)
    {
        var block = new Block(BlockTypes.WebApiCall, workflow.Sections.Single(), WEB_CALL_TAG)
        {
            Name = "Notify",
            Properties = new WebApiCallProperties { UrlTemplate = "https://hooks.example.com/report", HttpMethod = "POST" },
        };
        block.Order = 2;
        return workflow;
    }

    private static Workflow WithSearch(Workflow workflow)
    {
        var block = new Block(BlockTypes.IntelligentSearch, workflow.Sections.Single(), SEARCH_TAG)
        {
            Name = "Manager search",
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = "{Currency} manager background",
                IncludeDomains = ["{ParentURL.answer}"],
            },
        };
        block.Order = 2;
        return workflow;
    }

    private static IntelligentSearchProperties Search(Workflow workflow)
    {
        return (IntelligentSearchProperties)workflow.Sections.Single().Blocks.Single(block => block.ReplacementTag == SEARCH_TAG).Properties;
    }

    private static JObject WithoutDomains(string draft)
    {
        JObject root = JObject.Parse(draft);
        _ = ((JObject)Block(root, SEARCH_TAG)["Properties"]!).Remove(INCLUDE_DOMAINS);
        return root;
    }

    private static JObject Edited(string draft, string tag, string property, JToken value)
    {
        JObject root = JObject.Parse(draft);
        Block(root, tag)["Properties"]![property] = value;
        return root;
    }

    private static WebApiCallProperties WebCall(Workflow workflow)
    {
        return (WebApiCallProperties)workflow.Sections.Single().Blocks.Single(block => block.ReplacementTag == WEB_CALL_TAG).Properties;
    }

    private static JObject Edited(string draft, string tag, string property, string value)
    {
        JObject root = JObject.Parse(draft);
        Block(root, tag)["Properties"]![property] = value;
        return root;
    }

    private static JObject Block(JObject root, string tag)
    {
        return root["Sections"]!.SelectMany(section => section["Blocks"]!).OfType<JObject>()
            .Single(block => (string?)block["ReplacementTag"] == tag);
    }

    private static JObject Summary(Workflow workflow, params string[] tagsOrIds)
    {
        return new JObject
        {
            ["changes"] = new JArray(tagsOrIds.Select(tagOrId => new JObject
            {
                ["stableBlockId"] = workflow.Sections.SelectMany(section => section.Blocks)
                    .FirstOrDefault(block => block.ReplacementTag == tagOrId)?.StableBlockId ?? tagOrId,
                ["what"] = $"Changes {tagOrId}.",
            })),
        };
    }

    /// <summary>The run writes these files under out/, as the agent would.</summary>
    private void AgentWrites(string runId, JObject workflow, JObject summary)
    {
        AgentWritesOut(runId, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [EditorAgentProposalGate.WORKFLOW_FILE] = workflow.ToString(),
            [EditorAgentProposalGate.SUMMARY_FILE] = summary.ToString(),
        });
    }

    /// <summary>The run writes these files under out/, keyed by their path below out/.</summary>
    private void AgentWritesOut(string runId, Dictionary<string, string> files)
    {
        _outFiles[runId] = files.ToDictionary(
            static file => $"{AgentRunFilePaths.ARTIFACT_PREFIX}{file.Key}",
            static file => Encoding.UTF8.GetBytes(file.Value),
            StringComparer.Ordinal);

        byte[] trace = Encoding.UTF8.GetBytes("""{"type":"stage","stage":"started","ts":"2026-10-06T10:00:00Z"}""" + "\n");
        _client.OnListRunFiles = id =>
        {
            var entries = new List<AgentRunFileEntry> { Entry(TRACE_PATH, trace, AgentRunFileKind.Trace) };
            if (_outFiles.TryGetValue(id, out Dictionary<string, byte[]>? files))
            {
                entries.AddRange(files.Select(file => Entry(file.Key, file.Value, AgentRunFileKind.Artifact)));
            }

            return ErrorOrFactory.From<IReadOnlyList<AgentRunFileEntry>>(entries);
        };
        _client.OnGetRunFile = (id, path) => _outFiles.TryGetValue(id, out Dictionary<string, byte[]>? files)
            && files.TryGetValue(path, out byte[]? content)
                ? content
                : trace;
    }

    private static AgentRunFileEntry Entry(string path, byte[] content, AgentRunFileKind kind)
    {
        return new AgentRunFileEntry
        {
            Path = path,
            SizeBytes = content.LongLength,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)),
            Kind = kind,
        };
    }
}
