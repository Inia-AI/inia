using System.Security.Cryptography;
using System.Text;
using AngleSharp.Dom;
using Bunit;
using IniaAi.Application.Workflows.EditorAgent;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApp.Components.WorkflowEditor.Agent;
using IniaAi.WebApp.Components.WorkflowEditor.State;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Agent;

/// <summary>
/// The review of proposed changes, end to end: the runtime stand-in plays the agent by writing
/// out/workflow.json and out/summary.json, and everything from the stop gate to the change cards
/// and the editor's live workflow is the real code.
/// </summary>
public sealed class EditorAgentProposalTests : EditorAgentRenderTest
{
    private const string WORKFLOW_TYPE = "WT-EditorAgentReview";
    private const string VERSION_ID = "version-after-save";
    private const string PRODUCER_ID = "stable-revenue";
    private const string READER_ID = "stable-summary";
    private const string WEB_CALL_ID = "stable-notify";
    private const string PRODUCER_NAME = "Revenue breakdown";
    private const string READER_NAME = "Summary";
    private const string OLD_PROMPT = "Break revenue down by segment in {Currency}.";
    private const string NEW_PROMPT = "Break revenue down by segment and by region in {Currency}.";
    private const string READER_PROMPT = "Summarize: {revenue.answer}";
    private const string AGENT_READER_PROMPT = "Summarize in three bullets: {revenue.answer}";
    private const string USER_READER_PROMPT = "Summarize briefly: {revenue.answer}";
    private const string OLD_ADDRESS = "https://hooks.example.com/report";
    private const string NEW_ADDRESS = "https://elsewhere.example.net/collect";
    private const string CARD = "[data-testid=editor-agent-change]";
    private const string DRAWER = "[data-testid=editor-agent-detail]";
    private const string QUESTION = "Add regions to the revenue breakdown.";
    private const string TRACE_FILE = "trace/trace.ndjson";
    private const string OVERVIEW = "Shows revenue by region and gives the summary three bullets. Nothing else changes.";

    private readonly Workflow _workflow;
    private readonly List<EditorAgentWorkflowEdit> _edits = [];
    private IReadOnlyCollection<string> _proposed = [];
    private EditorAgentPreview? _preview;
    private byte[]? _proposedWorkflow;
    private byte[]? _proposedSummary;

    public EditorAgentProposalTests()
        : base(enabled: true)
    {
        _workflow = new Workflow("Credit report") { Title = "Credit report", WorkflowTypeId = WORKFLOW_TYPE };
        _ = new WorkflowVariable(_workflow, "Currency");
        var section = new Section("Analysis", _workflow) { Title = "Analysis", StableSectionId = "stable-analysis" };
        var producer = new Block(BlockTypes.AiQuery, section, "revenue")
        {
            Name = PRODUCER_NAME,
            StableBlockId = PRODUCER_ID,
            Properties = new AiQueryProperties { UserPromptTemplate = OLD_PROMPT },
        };
        var reader = new Block(BlockTypes.AiQuery, section, "summary")
        {
            Name = READER_NAME,
            StableBlockId = READER_ID,
            Properties = new AiQueryProperties { UserPromptTemplate = READER_PROMPT },
        };
        producer.Order = 0;
        reader.Order = 1;
        RuntimeAnswers();
    }

    [Fact]
    public async Task EachChange_ShowsInline_WithItsReasonAndTheWordsThatChanged_AndOpenBlockShowsTheBlock()
    {
        AgentProposes(draft =>
        {
            Properties(draft, PRODUCER_ID)["UserPromptTemplate"] = NEW_PROMPT;
            Block(draft, READER_ID)["Name"] = "Executive summary";
        }, PRODUCER_ID, READER_ID);

        IRenderedComponent<EditorAgentPanel> panel = await AskAsync();

        Assert.Contains("Changes to 2 blocks", panel.Find("[data-testid=editor-agent-proposal]").TextContent);
        Assert.StartsWith("Your organization today: $", panel.Find("[data-testid=editor-agent-today]").TextContent.Trim(), StringComparison.Ordinal);
        Assert.Empty(panel.FindAll(DRAWER));
        IElement section = panel.Find("[data-testid=editor-agent-section]");
        Assert.Equal("Analysis", section.QuerySelector(".editor-agent-section-title")!.TextContent.Trim());
        IElement producer = Card(panel, PRODUCER_ID);
        Assert.Equal(PRODUCER_NAME, producer.QuerySelector(".editor-agent-edit-name")!.TextContent.Trim());
        Assert.Equal("Changed", producer.QuerySelector(".editor-agent-kind")!.TextContent.Trim());
        Assert.Equal("AI instructions", producer.QuerySelector(".editor-agent-field-label")!.TextContent.Trim());
        Assert.Contains($"Changes {PRODUCER_ID}.", producer.QuerySelector(".editor-agent-edit-reason")!.TextContent);
        Assert.Contains("region", producer.QuerySelector("[data-testid=editor-agent-change-diff] mark.diff-added")!.TextContent);
        Assert.DoesNotContain("{", producer.QuerySelector(".editor-agent-edit-card-head")!.TextContent);
        IElement reader = Card(panel, READER_ID);
        Assert.Contains("Executive summary", reader.TextContent);
        Assert.NotNull(reader.QuerySelector("button[aria-label='Accept: Executive summary']"));

        Click(panel, () => Card(panel, PRODUCER_ID), $"Open the block {PRODUCER_NAME}");

        IElement drawer = panel.WaitForElement(DRAWER);
        Assert.Contains(PRODUCER_NAME, drawer.TextContent);
        Assert.Equal(OLD_PROMPT, drawer.QuerySelector("[data-testid=editor-agent-block-instructions]")!.TextContent.Trim());
    }

    [Fact]
    public async Task AcceptingACard_ChangesTheLiveWorkflow_MarksTheBlock_AndUndoPutsItBack()
    {
        AgentProposes(draft => Properties(draft, PRODUCER_ID)["UserPromptTemplate"] = NEW_PROMPT, PRODUCER_ID);
        IRenderedComponent<EditorAgentPanel> panel = await AskAsync();

        Assert.Equal([PRODUCER_ID], _proposed);

        Click(panel, () => Card(panel, PRODUCER_ID), $"Accept: {PRODUCER_NAME}");

        panel.WaitForAssertion(() =>
        {
            Assert.Empty(_proposed);
            Assert.Equal(NEW_PROMPT, Prompt(PRODUCER_ID));
            Assert.Equal("Accepted", Card(panel, PRODUCER_ID).QuerySelector("[data-testid=editor-agent-decision]")!.TextContent.Trim());
            Assert.NotNull(panel.Find("[data-testid=editor-agent-applied]"));
        });
        EditorAgentWorkflowEdit edit = Assert.Single(_edits);
        Assert.False(edit.Undone);
        Assert.Equal([PRODUCER_ID], edit.ChangedStableIds);

        var surface = new EditorSurfaceState(EditorSurfaceCapabilities.FullEditor);
        surface.MarkChangedByAgent(edit.ProposalId, edit.ChangedStableIds);
        Assert.True(surface.IsChangedByAgent(Producer()));
        Assert.True(surface.IsChangedByAgent(Producer().Section!));
        Assert.False(surface.IsChangedByAgent(_workflow.GetAllBlocks().Single(block => block.StableBlockId == READER_ID)));

        Click(panel, () => panel.Find("[data-testid=editor-agent-applied]"), "Undo these changes");

        panel.WaitForAssertion(() =>
        {
            Assert.Equal(OLD_PROMPT, Prompt(PRODUCER_ID));
            Assert.Empty(panel.FindAll("[data-testid=editor-agent-applied]"));
            Assert.NotNull(Card(panel, PRODUCER_ID).QuerySelector("[data-testid=editor-agent-change-diff]"));
        });
        Assert.True(_edits[^1].Undone);
        surface.UnmarkChangedByAgent(_edits[^1].ProposalId);
        Assert.False(surface.IsChangedByAgent(Producer()));
    }

    [Fact]
    public async Task RejectingACard_LeavesTheWorkflow_AndFoldsTheCardToItsHeading()
    {
        AgentProposes(draft =>
        {
            Properties(draft, PRODUCER_ID)["UserPromptTemplate"] = NEW_PROMPT;
            Block(draft, READER_ID)["Name"] = "Executive summary";
        }, PRODUCER_ID, READER_ID);
        IRenderedComponent<EditorAgentPanel> panel = await AskAsync();

        Click(panel, () => Card(panel, PRODUCER_ID), $"Reject: {PRODUCER_NAME}");

        panel.WaitForAssertion(() =>
        {
            IElement producer = Card(panel, PRODUCER_ID);
            Assert.Equal("Rejected", producer.QuerySelector("[data-testid=editor-agent-decision]")!.TextContent.Trim());
            Assert.Empty(producer.QuerySelectorAll("button[aria-label^='Accept']"));
            Assert.Null(producer.QuerySelector("[data-testid=editor-agent-change-diff]"));
        });
        Assert.Equal(OLD_PROMPT, Prompt(PRODUCER_ID));
        Assert.Equal(READER_NAME, Reader().Name);
        Assert.NotNull(Card(panel, READER_ID).QuerySelector("button[aria-label='Accept: Executive summary']"));
    }

    [Fact]
    public async Task AcceptAll_LeavesOutTheChangeThatSendsDataOutside_SaysSo_AndItsOwnCardAcceptsIt()
    {
        WithWebCall();
        AgentProposes(draft =>
        {
            Block(draft, READER_ID)["Name"] = "Executive summary";
            Properties(draft, WEB_CALL_ID)["UrlTemplate"] = NEW_ADDRESS;
        }, READER_ID, WEB_CALL_ID);
        IRenderedComponent<EditorAgentPanel> panel = await AskAsync();
        IElement webCall = Card(panel, WEB_CALL_ID);
        Assert.NotNull(webCall.QuerySelector("[data-testid=editor-agent-outbound]"));
        Assert.Contains(EditorAgentChangeText.OUTBOUND, webCall.QuerySelector("[data-testid=editor-agent-outbound-detail]")!.TextContent);
        Assert.Contains(NEW_ADDRESS, webCall.TextContent);

        Click(panel, () => panel.Find("[data-testid=editor-agent-proposal]"), "Accept all the changes that can be accepted together");

        panel.WaitForAssertion(() =>
        {
            Assert.Equal("Executive summary", Reader().Name);
            Assert.Contains(
                panel.FindAll("[data-testid=editor-agent-proposal-notes]"),
                note => note.TextContent.Contains("Accept all leaves out 1 change that needs its own review", StringComparison.Ordinal));
        });
        Assert.Equal(OLD_ADDRESS, WebCall().UrlTemplate);

        Click(panel, () => Card(panel, WEB_CALL_ID), "Accept: Notify");

        panel.WaitForAssertion(() => Assert.Equal(NEW_ADDRESS, WebCall().UrlTemplate));
    }

    [Fact]
    public async Task AFieldTheUserChangedMeanwhile_ShowsBothValuesOnTheCard_AndUseTheAgentsApplies()
    {
        AgentProposes(draft => Properties(draft, READER_ID)["UserPromptTemplate"] = AGENT_READER_PROMPT, READER_ID);
        IRenderedComponent<EditorAgentPanel> panel = await AskAsync();
        ((AiQueryProperties)Reader().Properties).UserPromptTemplate = USER_READER_PROMPT;

        Click(panel, () => Card(panel, READER_ID), $"Accept: {READER_NAME}");

        IElement conflict = panel.WaitForElement("[data-testid=editor-agent-change] [data-testid=editor-agent-conflict]");
        Assert.Empty(panel.FindAll(DRAWER));
        Assert.Equal(USER_READER_PROMPT, conflict.QuerySelector("[data-testid=editor-agent-conflict-mine]")!.TextContent.Trim());
        Assert.Equal(AGENT_READER_PROMPT, conflict.QuerySelector("[data-testid=editor-agent-conflict-agent]")!.TextContent.Trim());
        Assert.Equal(USER_READER_PROMPT, ReaderPrompt());
        IElement card = Card(panel, READER_ID);
        Assert.NotNull(card.QuerySelector($"button[aria-label='Keep mine: AI instructions of {READER_NAME}']"));

        Click(panel, () => Card(panel, READER_ID), $"Use the agent's: AI instructions of {READER_NAME}");

        panel.WaitForAssertion(() =>
        {
            Assert.Equal(AGENT_READER_PROMPT, ReaderPrompt());
            Assert.Empty(panel.FindAll("[data-testid=editor-agent-conflict]"));
        });
    }

    [Fact]
    public async Task AChangeTheChecksStillReject_SaysWhyOnItsCard_AndCannotBeAccepted()
    {
        AgentProposes(draft =>
        {
            Properties(draft, PRODUCER_ID)["UserPromptTemplate"] = NEW_PROMPT;
            Properties(draft, PRODUCER_ID)["SystemPromptTemplat"] = "You are an analyst.";
        }, PRODUCER_ID);
        IRenderedComponent<EditorAgentPanel> panel = await AskAsync();

        IReadOnlyList<IElement> cards = Cards(panel, PRODUCER_ID);
        Assert.NotEmpty(cards);
        Assert.All(cards, card =>
        {
            Assert.NotNull(card.QuerySelector("[data-testid=editor-agent-refused]"));
            Assert.NotNull(card.QuerySelector("[data-testid=editor-agent-refused-detail]"));
            Assert.DoesNotContain(card.QuerySelectorAll("button"), button => button.GetAttribute("aria-label")?.StartsWith("Accept", StringComparison.Ordinal) == true);
        });
        Assert.NotNull(panel.Find("[data-testid=editor-agent-check-errors]"));
        Assert.Empty(panel.FindAll("[data-testid=editor-agent-accept-all]"));
        Assert.Equal(OLD_PROMPT, Prompt(PRODUCER_ID));
    }

    [Fact]
    public async Task SixChangesToTwoBlocks_AreTwoCardsUnderTheirSection_AfterTheAgentsOverview()
    {
        AgentProposes(draft =>
        {
            Properties(draft, PRODUCER_ID)["UserPromptTemplate"] = NEW_PROMPT;
            Properties(draft, PRODUCER_ID)["SystemPromptTemplate"] = "You are an analyst.";
            Block(draft, PRODUCER_ID)["Name"] = "Revenue by region";
            Properties(draft, READER_ID)["UserPromptTemplate"] = AGENT_READER_PROMPT;
            Properties(draft, READER_ID)["SystemPromptTemplate"] = "You are an editor.";
            Block(draft, READER_ID)["Name"] = "Executive summary";
        }, PRODUCER_ID, READER_ID);
        _proposedSummary = Encoding.UTF8.GetBytes(new JObject
        {
            ["overview"] = OVERVIEW,
            ["changes"] = new JArray(new[] { PRODUCER_ID, READER_ID }.Select(id => new JObject { ["stableBlockId"] = id, ["what"] = $"Changes {id}." })),
        }.ToString());
        IRenderedComponent<EditorAgentPanel> panel = await AskAsync();

        Assert.Equal(OVERVIEW, panel.Find("[data-testid=editor-agent-overview]").TextContent.Trim());
        Assert.Equal(2, panel.FindAll(CARD).Count);
        Assert.Equal(3, Card(panel, PRODUCER_ID).QuerySelectorAll("[data-testid=editor-agent-field]").Length);

        Click(panel, () => panel.Find("[data-testid=editor-agent-section]"), "Accept the changes in Analysis");

        panel.WaitForAssertion(() =>
        {
            Assert.Equal(NEW_PROMPT, Prompt(PRODUCER_ID));
            Assert.Equal(AGENT_READER_PROMPT, ReaderPrompt());
        });

        Click(panel, () => panel.Find("[data-testid=editor-agent-section]"), "Undo the changes in Analysis");

        panel.WaitForAssertion(() =>
        {
            Assert.Equal(OLD_PROMPT, Prompt(PRODUCER_ID));
            Assert.Equal(READER_PROMPT, ReaderPrompt());
            Assert.Empty(panel.FindAll("[data-testid=editor-agent-section-undo]"));
        });
        Assert.True(_edits[^1].Undone);
    }

    [Fact]
    public async Task AnAddedBlock_IsOneNewCard_AndTheBlocksItPushedDownAreNotChanges()
    {
        AgentProposes(draft =>
        {
            var blocks = (JArray)draft["Sections"]![0]!["Blocks"]!;
            var added = (JObject)Block(draft, READER_ID).DeepClone();
            added["StableBlockId"] = "new-risk-check";
            added["Id"] = Guid.NewGuid().ToString();
            added["ReplacementTag"] = "risk";
            added["Name"] = "Risk check";
            blocks.Insert(0, added);
            for (int i = 0; i < blocks.Count; i++)
            {
                blocks[i]["Order"] = i;
            }
        }, "new-risk-check");
        IRenderedComponent<EditorAgentPanel> panel = await AskAsync();

        IElement card = Assert.Single(panel.FindAll(CARD));
        Assert.Equal("New", card.QuerySelector(".editor-agent-kind")!.TextContent.Trim());
        Block previewRisk = _preview!.Workflow.GetAllBlocks().Single(block => block.ReplacementTag == "risk");
        Assert.Equal(EditorAgentPreview.ADDED, Assert.Single(_preview.MarksByBlockId).Value);
        Assert.Equal(previewRisk.Id, _preview.MarksByBlockId.Keys.Single());
        Assert.DoesNotContain(_workflow.GetAllBlocks(), block => block.ReplacementTag == "risk");
        Assert.Contains(READER_PROMPT, card.QuerySelector("[data-testid=editor-agent-added-instructions]")!.TextContent);
        Assert.Empty(panel.FindAll("[data-testid=editor-agent-reorder]"));

        Click(panel, () => card, "Accept: Risk check");

        panel.WaitForAssertion(() => Assert.Null(_preview));
        panel.WaitForAssertion(() => Assert.Equal(
            ["risk", "revenue", "summary"],
            _workflow.Sections.Single().Blocks.OrderBy(block => block.Order).Select(block => block.ReplacementTag)));
        string riskId = _workflow.Sections.Single().Blocks.Single(block => block.ReplacementTag == "risk").StableBlockId!;
        Assert.Equal([riskId], Assert.Single(_edits).ChangedStableIds);
    }

    [Fact]
    public async Task TheSaveNote_CountsTheAcceptedChanges_SurvivesAFailedSave_AndIsGoneAfterASave()
    {
        AgentProposes(draft =>
        {
            Properties(draft, PRODUCER_ID)["UserPromptTemplate"] = NEW_PROMPT;
            Properties(draft, READER_ID)["UserPromptTemplate"] = AGENT_READER_PROMPT;
        }, PRODUCER_ID, READER_ID);
        IRenderedComponent<EditorAgentPanel> panel = await AskAsync();
        Click(panel, () => panel.Find("[data-testid=editor-agent-proposal]"), "Accept all the changes that can be accepted together");
        panel.WaitForAssertion(() => Assert.Equal(NEW_PROMPT, Prompt(PRODUCER_ID)));
        var note = new EditorAgentVersionNote(
            Services.GetRequiredService<IWorkflowEditorAgentService>(), ORGANIZATION_ID, panel.Instance.UserId);

        string? beforeThisEditorAccepted = await note.BeforeSaveAsync(WORKFLOW_TYPE);
        note.NoteAccepted();
        string? first = await note.BeforeSaveAsync(WORKFLOW_TYPE);
        await note.AfterSaveAsync(WORKFLOW_TYPE, wroteNewVersion: false, VERSION_ID);
        string? afterFailedSave = await note.BeforeSaveAsync(WORKFLOW_TYPE);
        await note.AfterSaveAsync(WORKFLOW_TYPE, wroteNewVersion: true, VERSION_ID);
        string? afterSave = await note.BeforeSaveAsync(WORKFLOW_TYPE);

        Assert.Null(beforeThisEditorAccepted);
        Assert.Equal("2 changes proposed by the editor agent", first);
        Assert.Equal(first, afterFailedSave);
        Assert.Null(afterSave);
    }

    [Fact]
    public async Task AfterAcceptingEverything_AcceptAllIsGone_AndASaveTurnsTheCardToSavedWithoutUndo()
    {
        AgentProposes(draft => Properties(draft, PRODUCER_ID)["UserPromptTemplate"] = NEW_PROMPT, PRODUCER_ID);
        IRenderedComponent<EditorAgentPanel> panel = await AskAsync();
        Assert.NotNull(panel.Find("[data-testid=editor-agent-accept-all]"));

        Click(panel, () => panel.Find("[data-testid=editor-agent-proposal]"), "Accept all the changes that can be accepted together");

        panel.WaitForAssertion(() =>
        {
            Assert.Equal("Applied to the editor. Save to keep it.", panel.Find("[data-testid=editor-agent-applied]").TextContent.Replace("Undo", string.Empty).Trim());
            Assert.Empty(panel.FindAll("[data-testid=editor-agent-accept-all]"));
        });

        var note = new EditorAgentVersionNote(
            Services.GetRequiredService<IWorkflowEditorAgentService>(), ORGANIZATION_ID, panel.Instance.UserId);
        note.NoteAccepted();
        _ = await note.BeforeSaveAsync(WORKFLOW_TYPE);
        Assert.True(await note.AfterSaveAsync(WORKFLOW_TYPE, wroteNewVersion: true, VERSION_ID));
        await panel.Instance.RefreshAsync();

        panel.WaitForAssertion(() =>
        {
            Assert.Equal("Saved.", panel.Find("[data-testid=editor-agent-saved]").TextContent.Trim());
            Assert.Empty(panel.FindAll("[data-testid=editor-agent-applied]"));
            Assert.DoesNotContain(panel.FindAll("button"), button => button.GetAttribute("aria-label") == "Undo these changes");
        });
    }

    private async Task<IRenderedComponent<EditorAgentPanel>> AskAsync()
    {
        User user = await SignInAsync("engineer@inia.ai", Roles.WORKFLOW_EDITOR);
        await SeedWorkflowTypeAsync(WORKFLOW_TYPE);

        IRenderedComponent<EditorAgentPanel> panel = RenderInApp<EditorAgentPanel>(parameters => parameters
            .Add(c => c.OrganizationId, ORGANIZATION_ID)
            .Add(c => c.UserId, user.Id)
            .Add(c => c.WorkflowTypeId, WORKFLOW_TYPE)
            .Add(c => c.CurrentWorkflow, () => _workflow)
            .Add(c => c.OnWorkflowEdited, edit => _edits.Add(edit))
            .Add(c => c.OnProposedChanged, ids => _proposed = ids)
            .Add(c => c.OnPreviewChanged, preview => _preview = preview));
        _ = panel.WaitForElement("textarea:not([disabled])");

        await panel.InvokeAsync(() => panel.Find("textarea").Input(QUESTION));
        await panel.InvokeAsync(() => panel.FindAll("button").Single(button => button.TextContent.Trim() == "Send").Click());
        _ = panel.WaitForElement("[data-testid=editor-agent-proposal]", TimeSpan.FromSeconds(30));

        return panel;
    }

    private static IElement Card(IRenderedComponent<EditorAgentPanel> panel, string stableId)
    {
        return Assert.Single(Cards(panel, stableId));
    }

    private static IReadOnlyList<IElement> Cards(IRenderedComponent<EditorAgentPanel> panel, string stableId)
    {
        return [.. panel.FindAll(CARD).Where(card => card.GetAttribute("data-group") == $"Block:{stableId}")];
    }

    private static void Click(IRenderedComponent<EditorAgentPanel> panel, Func<IElement> scope, string accessibleName)
    {
        // Live progress re-renders the panel at any moment: find and act inside one dispatch.
        panel.InvokeAsync(() => scope().QuerySelectorAll("button").Single(button => button.GetAttribute("aria-label") == accessibleName).Click())
            .GetAwaiter().GetResult();
    }

    private void AgentProposes(Action<JObject> edit, params string[] listed)
    {
        JObject draft = JObject.Parse(_workflow.ToExportJson(includeRunTraces: true));
        edit(draft);
        _proposedWorkflow = Encoding.UTF8.GetBytes(draft.ToString());
        _proposedSummary = Encoding.UTF8.GetBytes(new JObject
        {
            ["changes"] = new JArray(listed.Select(id => new JObject { ["stableBlockId"] = id, ["what"] = $"Changes {id}." })),
        }.ToString());
    }

    private void WithWebCall()
    {
        var call = new Block(BlockTypes.WebApiCall, _workflow.Sections.Single(), "notify")
        {
            Name = "Notify",
            StableBlockId = WEB_CALL_ID,
            Properties = new WebApiCallProperties { UrlTemplate = OLD_ADDRESS, HttpMethod = "POST" },
        };
        call.Order = 2;
    }

    private static JObject Block(JObject root, string stableId)
    {
        return root["Sections"]!.SelectMany(section => section["Blocks"]!).OfType<JObject>()
            .Single(block => (string?)block["StableBlockId"] == stableId);
    }

    private static JObject Properties(JObject root, string stableId)
    {
        return (JObject)Block(root, stableId)["Properties"]!;
    }

    private Block Producer()
    {
        return _workflow.GetAllBlocks().Single(block => block.StableBlockId == PRODUCER_ID);
    }

    private Block Reader()
    {
        return _workflow.GetAllBlocks().Single(block => block.StableBlockId == READER_ID);
    }

    private string? Prompt(string stableId)
    {
        return ((AiQueryProperties)_workflow.GetAllBlocks().Single(block => block.StableBlockId == stableId).Properties).UserPromptTemplate;
    }

    private string? ReaderPrompt()
    {
        return Prompt(READER_ID);
    }

    private WebApiCallProperties WebCall()
    {
        return (WebApiCallProperties)_workflow.GetAllBlocks().Single(block => block.StableBlockId == WEB_CALL_ID).Properties;
    }

    private void RuntimeAnswers()
    {
        byte[] trace = Encoding.UTF8.GetBytes("""{"type":"stage","stage":"started","ts":"2026-10-06T10:00:00Z"}""" + "\n");
        string workflowPath = $"{AgentRunFilePaths.ARTIFACT_PREFIX}{EditorAgentProposalGate.WORKFLOW_FILE}";
        string summaryPath = $"{AgentRunFilePaths.ARTIFACT_PREFIX}{EditorAgentProposalGate.SUMMARY_FILE}";

        Runtime.OnStartRun = request => Running(request.ExecutionId);
        Runtime.OnGetRun = id => new AgentRunMonitor
        {
            ExecutionId = id,
            Status = AgentRunStatus.Succeeded,
            EstimatedCostUsd = 0.02m,
            Result = new AgentRunResult("I added regions to the [Revenue breakdown](block:stable-revenue).", "session"),
        };
        Runtime.OnCompleteRun = id => new AgentRunMonitor { ExecutionId = id, Status = AgentRunStatus.Succeeded };
        Runtime.OnListRunFiles = _ =>
        {
            var entries = new List<AgentRunFileEntry> { Entry(TRACE_FILE, trace, AgentRunFileKind.Trace) };
            if (_proposedWorkflow is not null && _proposedSummary is not null)
            {
                entries.Add(Entry(workflowPath, _proposedWorkflow, AgentRunFileKind.Artifact));
                entries.Add(Entry(summaryPath, _proposedSummary, AgentRunFileKind.Artifact));
            }

            return ErrorOr.ErrorOrFactory.From<IReadOnlyList<AgentRunFileEntry>>(entries);
        };
        Runtime.OnGetRunFile = (_, path) => path == workflowPath ? _proposedWorkflow! : path == summaryPath ? _proposedSummary! : trace;
        Runtime.OnGetRunCheckpoint = id =>
        {
            string stamp = $$"""{"bundleFormat":"1","sdk":"2.0.44","model":"{{SONNET}}","run":"{{id}}"}""";
            byte[] bundle = IniaAi.Domain.Serialization.WorkflowJsonCompression.Compress(
                $$"""{"sessionId":"s","files":{},"version":{{stamp}}}""");
            return new AgentRunCheckpoint(bundle, Convert.ToHexStringLower(SHA256.HashData(bundle)));
        };
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
