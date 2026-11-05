using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Proposals;
using Newtonsoft.Json.Linq;

namespace IniaAi.Domain.Tests.Proposals;

/// <summary>
/// The agent proposes against a snapshot of the editor's workflow; the user keeps editing the live
/// one. Each test proposes against the snapshot and applies to the live workflow, as the editor does.
/// </summary>
public class WorkflowProposalApplierTests
{
    private const string NEW_PROMPT = "Break revenue down by segment and by region in {Currency}.";
    private const string NEW_SYSTEM = "You are a careful credit analyst.";
    private const string USER_PROMPT = "Break revenue down by product line in {Currency}.";

    private readonly Workflow _live = ProposalWorkflows.Build();
    private readonly string _base;

    public WorkflowProposalApplierTests()
    {
        _base = ProposalWorkflows.Json(_live);
    }

    [Fact]
    public void AcceptedFieldEdits_LandOnTheLiveWorkflow_AndNothingElseChanges()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        ProposalWorkflows.Block(proposed, ProposalWorkflows.PRODUCER)["Properties"]!["UserPromptTemplate"] = NEW_PROMPT;
        WorkflowProposal proposal = Propose(proposed);
        string? readerBefore = Prompt(ProposalWorkflows.READER);

        ProposalApplyResult result = Apply(proposal.Changes);

        Assert.Equal(proposal.Changes.Single().Id, Assert.Single(result.Applied).ChangeId);
        Assert.Empty(result.Conflicts);
        Assert.Equal(NEW_PROMPT, Prompt(ProposalWorkflows.PRODUCER));
        Assert.Equal(readerBefore, Prompt(ProposalWorkflows.READER));
    }

    [Fact]
    public void FieldTheUserChangedDuringTheTurn_IsAConflict_AndTheBlocksOtherFieldStillApplies()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        JObject producer = ProposalWorkflows.Block(proposed, ProposalWorkflows.PRODUCER);
        producer["Properties"]!["UserPromptTemplate"] = NEW_PROMPT;
        producer["Properties"]!["SystemPromptTemplate"] = NEW_SYSTEM;
        WorkflowProposal proposal = Propose(proposed);
        Properties(ProposalWorkflows.PRODUCER).UserPromptTemplate = USER_PROMPT;

        ProposalApplyResult result = Apply(proposal.Changes);

        ProposalConflict conflict = Assert.Single(result.Conflicts);
        Assert.Equal("Properties.UserPromptTemplate", conflict.Field);
        Assert.Equal(USER_PROMPT, JToken.Parse(conflict.LiveJson!).Value<string>());
        Assert.Equal(NEW_PROMPT, JToken.Parse(conflict.NewJson!).Value<string>());
        Assert.Equal(ProposalWorkflows.PRODUCER_PROMPT, JToken.Parse(conflict.BaseJson!).Value<string>());
        Assert.Equal(USER_PROMPT, Prompt(ProposalWorkflows.PRODUCER));
        Assert.Equal(NEW_SYSTEM, Properties(ProposalWorkflows.PRODUCER).SystemPromptTemplate);
    }

    [Fact]
    public void Conflict_SettledForTheProposedValue_Applies()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        ProposalWorkflows.Block(proposed, ProposalWorkflows.PRODUCER)["Properties"]!["UserPromptTemplate"] = NEW_PROMPT;
        WorkflowProposal proposal = Propose(proposed);
        Properties(ProposalWorkflows.PRODUCER).UserPromptTemplate = USER_PROMPT;

        ProposalApplyResult result = WorkflowProposalApplier.Apply(
            _live, proposal.Changes, new HashSet<string> { proposal.Changes.Single().Id });

        Assert.Empty(result.Conflicts);
        Assert.Equal(NEW_PROMPT, Prompt(ProposalWorkflows.PRODUCER));
    }

    [Fact]
    public void AddedBlock_GetsARealStableIdAndNewRowIds_InItsSectionAndPlace()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        JArray blocks = ProposalWorkflows.Blocks(proposed, 0);
        var clone = (JObject)ProposalWorkflows.Block(proposed, ProposalWorkflows.PRODUCER).DeepClone();
        clone["StableBlockId"] = "new-region-split";
        clone["ReplacementTag"] = "regions";
        clone["Name"] = "Region split";
        blocks.Insert(1, clone);
        for (int i = 0; i < blocks.Count; i++)
        {
            blocks[i]["Order"] = i;
        }

        WorkflowProposal proposal = Propose(proposed);
        Block producer = ProposalWorkflows.LiveBlock(_live, ProposalWorkflows.PRODUCER);

        ProposalApplyResult result = Apply(proposal.Changes);

        Assert.Empty(result.Conflicts);
        Assert.Empty(result.NotApplied);
        Block added = ProposalWorkflows.LiveBlock(_live, "regions");
        string minted = Assert.Single(result.Applied, applied => applied.MintedStableId is not null).MintedStableId!;
        Assert.Equal(minted, added.StableBlockId);
        Assert.NotEqual("new-region-split", added.StableBlockId);
        Assert.NotEqual(producer.Id, added.Id);
        Assert.NotEqual(producer.RagSettings.Single().Id, added.RagSettings.Single().Id);
        Assert.Same(added, added.RagSettings.Single().Block);
        Assert.Same(_live.Sections.First(), added.Section);
        Assert.Equal(
            [ProposalWorkflows.PRODUCER, "regions", ProposalWorkflows.READER, ProposalWorkflows.WEB_CALL, ProposalWorkflows.SEARCH, ProposalWorkflows.LINKED],
            _live.Sections.First().Blocks.OrderBy(block => block.Order).Select(block => block.ReplacementTag));
    }

    [Fact]
    public void Undo_PutsBackEveryFieldAddedRemovedAndMovedBlock_AndTheOrder()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        ProposalWorkflows.Block(proposed, ProposalWorkflows.PRODUCER)["Properties"]!["UserPromptTemplate"] = NEW_PROMPT;
        var clone = (JObject)ProposalWorkflows.Block(proposed, ProposalWorkflows.READER).DeepClone();
        clone["StableBlockId"] = "new-risk";
        clone["ReplacementTag"] = "risk";
        clone["Order"] = 0;
        ProposalWorkflows.Blocks(proposed, 0).Add(clone);
        JObject search = ProposalWorkflows.Block(proposed, ProposalWorkflows.SEARCH);
        search.Remove();
        ProposalWorkflows.Blocks(proposed, 1).Add(search);
        ProposalWorkflows.Block(proposed, ProposalWorkflows.MERGE).Remove();
        WorkflowProposal proposal = Propose(proposed);
        string before = Comparable(_live);

        ProposalApplyResult result = Apply(proposal.Changes);
        Assert.Empty(result.Conflicts);
        Assert.Empty(result.NotApplied);
        Assert.NotEqual(before, Comparable(_live));

        ProposalUndo undo = ProposalUndo.FromJson(result.Undo.ToJson());
        _ = WorkflowProposalApplier.Undo(_live, undo);

        Assert.Equal(before, Comparable(_live));
    }

    [Fact]
    public void RemovedBlock_TheUserEditedDuringTheTurn_IsAConflict()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        ProposalWorkflows.Block(proposed, ProposalWorkflows.MERGE).Remove();
        WorkflowProposal proposal = Propose(proposed);
        ((MergeProperties)ProposalWorkflows.LiveBlock(_live, ProposalWorkflows.MERGE).Properties).TextTemplate = "{summary}\n{revenue}";

        ProposalApplyResult result = Apply(proposal.Changes);

        _ = Assert.Single(result.Conflicts);
        _ = ProposalWorkflows.LiveBlock(_live, ProposalWorkflows.MERGE);
    }

    [Fact]
    public void AddedVariable_JoinsTheWorkflow()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        ((JArray)proposed["Variables"]!).Add(new JObject
        {
            ["ReplacementTag"] = "Region",
            ["DisplayName"] = "Region",
            ["Description"] = string.Empty,
            ["Type"] = "String",
        });
        WorkflowProposal proposal = Propose(proposed);

        _ = Apply(proposal.Changes);

        WorkflowVariable added = Assert.Single(_live.Variables, variable => variable.ReplacementTag == "Region");
        Assert.Same(_live, added.Workflow);
    }

    [Fact]
    public void ChangeAlreadyInTheLiveWorkflow_CountsAsApplied()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        ProposalWorkflows.Block(proposed, ProposalWorkflows.PRODUCER)["Properties"]!["UserPromptTemplate"] = NEW_PROMPT;
        WorkflowProposal proposal = Propose(proposed);
        Properties(ProposalWorkflows.PRODUCER).UserPromptTemplate = NEW_PROMPT;

        ProposalApplyResult result = Apply(proposal.Changes);

        _ = Assert.Single(result.Applied);
        Assert.True(result.Undo.IsEmpty);
    }

    [Fact]
    public void RemovalAfterAnEarlierAcceptRenumberedTheSection_IsNotAConflict()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        ProposalWorkflows.Block(proposed, ProposalWorkflows.PRODUCER).Remove();
        ProposalWorkflows.Block(proposed, ProposalWorkflows.LINKED).Remove();
        WorkflowProposal proposal = Propose(proposed);
        ProposedChange first = proposal.Changes.Single(change => change.Tag == ProposalWorkflows.PRODUCER);

        _ = Apply([first]);
        ProposalApplyResult later = Apply([.. proposal.Changes.Where(change => change.Id != first.Id)]);

        Assert.Empty(later.Conflicts);
        Assert.DoesNotContain(_live.Sections.First().Blocks, block => block.ReplacementTag == ProposalWorkflows.LINKED);
    }

    [Fact]
    public void BlockMovedIntoASectionAcceptedEarlier_FindsItByItsMintedId()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        var section = (JObject)proposed["Sections"]![1]!.DeepClone();
        section["StableSectionId"] = "new-appendix";
        section["Order"] = 2;
        section["Blocks"] = new JArray();
        ((JArray)proposed["Sections"]!).Add(section);
        JObject reader = ProposalWorkflows.Block(proposed, ProposalWorkflows.READER);
        reader.Remove();
        ((JArray)section["Blocks"]!).Add(reader);
        WorkflowProposal proposal = Propose(proposed);
        ProposedChange addSection = proposal.Changes.Single(change => change.Scope == ProposedChangeScope.Section);
        ProposalApplyResult first = Apply([addSection]);
        var minted = new Dictionary<string, string> { ["new-appendix"] = first.Applied.Single().MintedStableId! };

        ProposalApplyResult later = WorkflowProposalApplier.Apply(
            _live, [proposal.Changes.Single(change => change.Kind == ProposedChangeKind.Moved)], new HashSet<string>(), minted);

        Assert.Empty(later.NotApplied);
        Assert.Equal(minted["new-appendix"], ProposalWorkflows.LiveBlock(_live, ProposalWorkflows.READER).Section!.StableSectionId);
    }

    [Fact]
    public void Undo_LeavesAFieldTheUserChangedAfterTheApply_AndReportsIt()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        ProposalWorkflows.Block(proposed, ProposalWorkflows.PRODUCER)["Properties"]!["UserPromptTemplate"] = NEW_PROMPT;
        ProposalWorkflows.Block(proposed, ProposalWorkflows.READER)["Name"] = "Executive summary";
        WorkflowProposal proposal = Propose(proposed);
        ProposalApplyResult result = Apply(proposal.Changes);
        Properties(ProposalWorkflows.PRODUCER).UserPromptTemplate = USER_PROMPT;

        ProposalUndoOutcome outcome = WorkflowProposalApplier.Undo(_live, result.Undo);

        ProposalConflict conflict = Assert.Single(outcome.Conflicts);
        Assert.Equal("Properties.UserPromptTemplate", conflict.Field);
        Assert.Equal(USER_PROMPT, Prompt(ProposalWorkflows.PRODUCER));
        Assert.Equal("Summary", ProposalWorkflows.LiveBlock(_live, ProposalWorkflows.READER).Name);
    }

    [Fact]
    public void Undo_RestoresTheOrderOfOnlyTheSectionsTheApplyTouched()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        ProposalWorkflows.Block(proposed, ProposalWorkflows.PRODUCER)["Properties"]!["UserPromptTemplate"] = NEW_PROMPT;
        WorkflowProposal proposal = Propose(proposed);
        ProposalApplyResult result = Apply(proposal.Changes);
        var report = new Block(BlockTypes.Merge, _live.Sections.Last(), "late") { Name = "Late" };
        report.Order = -1;
        _live.Sections.Last().NormalizeBlockOrdering();

        _ = WorkflowProposalApplier.Undo(_live, result.Undo);

        Assert.Equal(["late", ProposalWorkflows.MERGE], _live.Sections.Last().Blocks.OrderBy(block => block.Order).Select(block => block.ReplacementTag));
    }

    [Fact]
    public void AcceptingOnlyAnAddedBlock_WithItsRenumbering_PutsItWhereTheAgentDid()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        JArray blocks = ProposalWorkflows.Blocks(proposed, 0);
        var clone = (JObject)ProposalWorkflows.Block(proposed, ProposalWorkflows.READER).DeepClone();
        clone["StableBlockId"] = "new-risk-check";
        clone["ReplacementTag"] = "risk";
        blocks.Insert(1, clone);
        for (int i = 0; i < blocks.Count; i++)
        {
            blocks[i]["Order"] = i;
        }

        WorkflowProposal proposal = Propose(proposed);
        ProposedChange added = Assert.Single(proposal.Changes, change => change.Kind == ProposedChangeKind.Added);

        _ = Apply([added, .. proposal.RenumberingFor([added])]);

        Assert.Equal(
            [ProposalWorkflows.PRODUCER, "risk", ProposalWorkflows.READER],
            _live.Sections.First().Blocks.OrderBy(block => block.Order).Take(3).Select(block => block.ReplacementTag));
    }

    [Fact]
    public void UndoingOneSection_TakesBackOnlyItsChanges_AndKeepsTheRestForALaterUndo()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        ProposalWorkflows.Block(proposed, ProposalWorkflows.PRODUCER)["Properties"]!["UserPromptTemplate"] = NEW_PROMPT;
        ProposalWorkflows.Block(proposed, ProposalWorkflows.MERGE)["Name"] = "Final report";
        WorkflowProposal proposal = Propose(proposed);
        ProposalApplyResult result = Apply(proposal.Changes);
        ProposedChange prompt = Assert.Single(proposal.Changes, change => change.Tag == ProposalWorkflows.PRODUCER);

        _ = WorkflowProposalApplier.Undo(_live, result.Undo, new HashSet<string> { prompt.Id });

        Assert.Equal(ProposalWorkflows.PRODUCER_PROMPT, Prompt(ProposalWorkflows.PRODUCER));
        Assert.Equal("Final report", _live.GetAllBlocks().Single(block => block.ReplacementTag == ProposalWorkflows.MERGE).Name);
        Assert.DoesNotContain(result.Undo.Steps, step => step.ChangeId == prompt.Id);

        _ = WorkflowProposalApplier.Undo(_live, result.Undo);

        Assert.NotEqual("Final report", _live.GetAllBlocks().Single(block => block.ReplacementTag == ProposalWorkflows.MERGE).Name);
    }

    private WorkflowProposal Propose(JObject proposed)
    {
        return WorkflowProposalBuilder.Build(_base, proposed.ToString(), []);
    }

    private ProposalApplyResult Apply(IReadOnlyList<ProposedChange> changes)
    {
        return WorkflowProposalApplier.Apply(_live, changes, new HashSet<string>());
    }

    private AiQueryProperties Properties(string tag)
    {
        return (AiQueryProperties)ProposalWorkflows.LiveBlock(_live, tag).Properties;
    }

    private string? Prompt(string tag)
    {
        return Properties(tag).UserPromptTemplate;
    }

    /// <summary>The workflow's content without row ids, which an undo is free to renew.</summary>
    private static string Comparable(Workflow workflow)
    {
        JObject json = JObject.Parse(ProposalWorkflows.Json(workflow));
        foreach (JProperty id in json.Descendants().OfType<JProperty>().Where(property => property.Name is "Id" or "BlockId").ToList())
        {
            id.Remove();
        }

        return json.ToString();
    }
}
