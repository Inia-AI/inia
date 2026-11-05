using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Proposals;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Domain.Tests.Proposals;

public class WorkflowProposalBuilderTests
{
    private const string NEW_PROMPT = "Break revenue down by segment and by region in {Currency}.";

    private readonly Workflow _workflow = ProposalWorkflows.Build();
    private readonly string _base;

    public WorkflowProposalBuilderTests()
    {
        _base = ProposalWorkflows.Json(_workflow);
    }

    [Fact]
    public void PromptEdit_IsOneFieldChange_CarryingTheBaseValue()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        ProposalWorkflows.Block(proposed, ProposalWorkflows.PRODUCER)["Properties"]!["UserPromptTemplate"] = NEW_PROMPT;

        WorkflowProposal proposal = Build(proposed, Summary(ProposalWorkflows.PRODUCER));

        ProposedChange change = Assert.Single(proposal.Changes);
        Assert.Equal(ProposedChangeScope.Block, change.Scope);
        Assert.Equal(ProposedChangeKind.FieldEdited, change.Kind);
        Assert.Equal(ProposalWorkflows.StableId(_workflow, ProposalWorkflows.PRODUCER), change.TargetKey);
        Assert.Equal("Properties.UserPromptTemplate", change.Field);
        Assert.Equal(ProposalWorkflows.PRODUCER_PROMPT, JsonConvert.DeserializeObject<string>(change.BaseJson!));
        Assert.Equal(NEW_PROMPT, JsonConvert.DeserializeObject<string>(change.NewJson!));
        Assert.False(change.MissingFromSummary);
        Assert.False(change.SendsDataOutside);
        Assert.True(change.CanAccept);
    }

    [Fact]
    public void TwoBlockEdit_NamesExactlyThoseTwoBlocks()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        ProposalWorkflows.Block(proposed, ProposalWorkflows.PRODUCER)["Properties"]!["UserPromptTemplate"] = NEW_PROMPT;
        ProposalWorkflows.Block(proposed, ProposalWorkflows.READER)["Name"] = "Executive summary";

        WorkflowProposal proposal = Build(proposed, Summary(ProposalWorkflows.PRODUCER, ProposalWorkflows.READER));

        Assert.Equal(
            new[] { ProposalWorkflows.StableId(_workflow, ProposalWorkflows.PRODUCER), ProposalWorkflows.StableId(_workflow, ProposalWorkflows.READER) }.Order(),
            proposal.Changes.Select(change => change.TargetKey!).Distinct().Order());
    }

    [Fact]
    public void RowIdsStampsAndRunEvidence_AreNotChanges()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        JObject producer = ProposalWorkflows.Block(proposed, ProposalWorkflows.PRODUCER);
        producer["Id"] = Guid.NewGuid().ToString();
        producer["RagSettings"]![0]!["Id"] = Guid.NewGuid().ToString();
        producer["TextInputs"]![0]!["Id"] = Guid.NewGuid().ToString();
        producer["ExecutionState"] = new JObject { ["Status"] = "Completed" };
        producer["BlockResponse"] = new JObject { ["MarkdownResponse"] = "an answer" };
        proposed["Sections"]![0]!["Id"] = Guid.NewGuid().ToString();

        WorkflowProposal proposal = Build(proposed, []);

        Assert.Empty(proposal.Changes);
    }

    [Fact]
    public void AddedBlock_WithAnInventedId_IsAddedInItsSection_AndRenumberedSiblingsAreNotMissingFromTheSummary()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        JArray blocks = ProposalWorkflows.Blocks(proposed, 0);
        var clone = (JObject)ProposalWorkflows.Block(proposed, ProposalWorkflows.READER).DeepClone();
        clone["StableBlockId"] = "new-risk-check";
        clone["ReplacementTag"] = "risk";
        clone["Name"] = "Risk check";
        blocks.Insert(2, clone);
        for (int i = 0; i < blocks.Count; i++)
        {
            blocks[i]["Order"] = i;
        }

        WorkflowProposal proposal = Build(proposed, Summary("new-risk-check"));

        ProposedChange added = Assert.Single(proposal.Changes, change => change.Kind == ProposedChangeKind.Added);
        Assert.Equal("new-risk-check", added.TargetKey);
        Assert.Equal(_workflow.Sections.First().StableSectionId, added.SectionKey);
        Assert.False(added.MissingFromSummary);
        Assert.All(
            proposal.Changes.Where(change => change.Kind == ProposedChangeKind.FieldEdited),
            change =>
            {
                Assert.Equal("Order", change.Field);
                Assert.False(change.MissingFromSummary);
                Assert.True(change.Incidental);
            });
    }

    [Fact]
    public void AddedSection_TheSummaryNamesAsNewAndItsTitle_IsListed()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        var section = (JObject)proposed["Sections"]![1]!.DeepClone();
        section["StableSectionId"] = Guid.NewGuid().ToString();
        section["Title"] = "Key risks";
        section["Order"] = 2;
        section["Blocks"] = new JArray();
        ((JArray)proposed["Sections"]!).Add(section);

        WorkflowProposal proposal = Build(proposed, [new ProposalSummaryEntry(null, "new-Key risks", "Adds a risks section.")]);

        Assert.False(Assert.Single(proposal.Changes).MissingFromSummary);
    }

    [Fact]
    public void NotesTheAgentWrites_AreOneWorkflowChange_AndSurviveTheExport()
    {
        const string notes = "A credit report for a bank's committee. Figures are in millions.";
        JObject proposed = ProposalWorkflows.Edit(_base);
        proposed["Notes"] = notes;

        ProposedChange change = Assert.Single(Build(proposed, []).Changes);

        Assert.Equal(ProposedChangeScope.Workflow, change.Scope);
        Assert.Equal("Notes", change.Field);
        Assert.Equal(notes, JsonConvert.DeserializeObject<string>(change.NewJson!));
        _workflow.Notes = notes;
        Assert.Equal(notes, Workflow.FromExportJson(_workflow.ToExportJson())!.Notes);
    }

    [Fact]
    public void SwappedBlocks_AreAReorder_SoTheirOrderEditsAreNotIncidental()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        ProposalWorkflows.Block(proposed, ProposalWorkflows.PRODUCER)["Order"] = 1;
        ProposalWorkflows.Block(proposed, ProposalWorkflows.READER)["Order"] = 0;

        WorkflowProposal proposal = Build(proposed, Summary(ProposalWorkflows.PRODUCER, ProposalWorkflows.READER));

        Assert.Equal(2, proposal.Changes.Count);
        Assert.All(proposal.Changes, change =>
        {
            Assert.Equal("Order", change.Field);
            Assert.False(change.Incidental);
        });
    }

    [Fact]
    public void AddedSection_RenumbersTheSectionsAfterIt_Incidentally()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        var section = (JObject)proposed["Sections"]![1]!.DeepClone();
        section["StableSectionId"] = "new-overview";
        section["Title"] = "Overview";
        section["Blocks"] = new JArray();
        var sections = (JArray)proposed["Sections"]!;
        sections.Insert(0, section);
        for (int i = 0; i < sections.Count; i++)
        {
            sections[i]["Order"] = i;
        }

        WorkflowProposal proposal = Build(proposed, [new ProposalSummaryEntry(null, "new-overview", "Adds an overview.")]);

        ProposedChange added = Assert.Single(proposal.Changes, change => change.Kind == ProposedChangeKind.Added);
        List<ProposedChange> renumbered = [.. proposal.Changes.Where(change => change.Field == "Order")];
        Assert.Equal(2, renumbered.Count);
        Assert.All(renumbered, change => Assert.True(change.Incidental));
        Assert.Equal(renumbered.Select(change => change.Id).Order(), proposal.RenumberingFor([added]).Select(change => change.Id).Order());
    }

    [Fact]
    public void RenumberingFor_IsOnlyTheRenumberingOfTheListsTheChosenChangesTouch()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        JArray blocks = ProposalWorkflows.Blocks(proposed, 0);
        var clone = (JObject)ProposalWorkflows.Block(proposed, ProposalWorkflows.READER).DeepClone();
        clone["StableBlockId"] = "new-risk-check";
        clone["ReplacementTag"] = "risk";
        blocks.Insert(0, clone);
        for (int i = 0; i < blocks.Count; i++)
        {
            blocks[i]["Order"] = i;
        }

        ProposalWorkflows.Block(proposed, ProposalWorkflows.MERGE)["Name"] = "Final report";
        WorkflowProposal proposal = Build(proposed, Summary("new-risk-check", ProposalWorkflows.MERGE));
        ProposedChange added = Assert.Single(proposal.Changes, change => change.Kind == ProposedChangeKind.Added);
        ProposedChange renamed = Assert.Single(proposal.Changes, change => change.Field == "Name");

        Assert.Equal(
            proposal.Changes.Where(change => change != renamed && change != added).Select(change => change.Id).Order(),
            proposal.RenumberingFor([added]).Select(change => change.Id).Order());
        Assert.Empty(proposal.RenumberingFor([renamed]));
    }

    [Fact]
    public void ClonedBlock_ThatKeptItsSiblingsStableId_IsNew_AndTheSiblingIsUnchanged()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        var clone = (JObject)ProposalWorkflows.Block(proposed, ProposalWorkflows.READER).DeepClone();
        clone["ReplacementTag"] = "risk";
        clone["Order"] = 99;
        ProposalWorkflows.Blocks(proposed, 0).Add(clone);

        WorkflowProposal proposal = Build(proposed, Summary("risk"));

        ProposedChange added = Assert.Single(proposal.Changes);
        Assert.Equal(ProposedChangeKind.Added, added.Kind);
        Assert.StartsWith(WorkflowProposalBuilder.NEW_BLOCK_KEY_PREFIX, added.TargetKey, StringComparison.Ordinal);
        Assert.False(added.MissingFromSummary);
    }

    [Fact]
    public void RemovedAndMovedBlocks_AreTheirOwnChanges()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        JObject reader = ProposalWorkflows.Block(proposed, ProposalWorkflows.READER);
        reader.Remove();
        ProposalWorkflows.Blocks(proposed, 1).Add(reader);
        ProposalWorkflows.Block(proposed, ProposalWorkflows.MERGE).Remove();

        WorkflowProposal proposal = Build(proposed, Summary(ProposalWorkflows.READER, ProposalWorkflows.MERGE));

        ProposedChange moved = Assert.Single(proposal.Changes, change => change.Kind == ProposedChangeKind.Moved);
        Assert.Equal(ProposalWorkflows.StableId(_workflow, ProposalWorkflows.READER), moved.TargetKey);
        Assert.Equal(_workflow.Sections.Last().StableSectionId, JsonConvert.DeserializeObject<string>(moved.NewJson!));
        ProposedChange removed = Assert.Single(proposal.Changes, change => change.Kind == ProposedChangeKind.Removed);
        Assert.Equal(ProposalWorkflows.StableId(_workflow, ProposalWorkflows.MERGE), removed.TargetKey);
    }

    [Fact]
    public void ChangedBlockMissingFromTheSummary_IsFlagged()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        ProposalWorkflows.Block(proposed, ProposalWorkflows.PRODUCER)["Properties"]!["UserPromptTemplate"] = NEW_PROMPT;
        ProposalWorkflows.Block(proposed, ProposalWorkflows.READER)["Name"] = "Executive summary";

        WorkflowProposal proposal = Build(proposed, Summary(ProposalWorkflows.PRODUCER));

        ProposedChange stray = Assert.Single(proposal.Changes, change => change.MissingFromSummary);
        Assert.Equal(ProposalWorkflows.READER, stray.Tag);
    }

    [Fact]
    public void WebCallAddressAndSearchQuery_SendDataOutside()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        ProposalWorkflows.Block(proposed, ProposalWorkflows.WEB_CALL)["Properties"]!["UrlTemplate"] = "https://elsewhere.example.net/collect";
        ProposalWorkflows.Block(proposed, ProposalWorkflows.SEARCH)["Properties"]!["SearchTermTemplate"] = "{revenue.answer}";
        ProposalWorkflows.Block(proposed, ProposalWorkflows.SEARCH)["Name"] = "Latest news";

        WorkflowProposal proposal = Build(proposed, Summary(ProposalWorkflows.WEB_CALL, ProposalWorkflows.SEARCH));

        Assert.True(Assert.Single(proposal.Changes, change => change.Field == "Properties.UrlTemplate").SendsDataOutside);
        Assert.True(Assert.Single(proposal.Changes, change => change.Field == "Properties.SearchTermTemplate").SendsDataOutside);
        Assert.False(Assert.Single(proposal.Changes, change => change.Field == "Name").SendsDataOutside);
    }

    [Fact]
    public void AddedWebCall_SendsDataOutside()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        var clone = (JObject)ProposalWorkflows.Block(proposed, ProposalWorkflows.WEB_CALL).DeepClone();
        clone["StableBlockId"] = "new-second-hook";
        clone["ReplacementTag"] = "notify_again";
        ProposalWorkflows.Blocks(proposed, 0).Add(clone);

        WorkflowProposal proposal = Build(proposed, Summary("new-second-hook"));

        Assert.True(Assert.Single(proposal.Changes).SendsDataOutside);
    }

    [Fact]
    public void LibraryLinkedBlock_IsFlagged_AndAChangeToItsLinkIsRefused()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        JObject linked = ProposalWorkflows.Block(proposed, ProposalWorkflows.LINKED);
        linked["Properties"]!["UserPromptTemplate"] = "Apply the house rule strictly.";
        linked["SharedComponentId"] = "component-2";

        WorkflowProposal proposal = Build(proposed, Summary(ProposalWorkflows.LINKED));

        ProposedChange prompt = Assert.Single(proposal.Changes, change => change.Field == "Properties.UserPromptTemplate");
        Assert.True(prompt.LibraryLinked);
        Assert.True(prompt.CanAccept);
        ProposedChange link = Assert.Single(proposal.Changes, change => change.Field == "SharedComponentId");
        Assert.Equal(ProposalRefusal.LibraryLink, link.Refusal);
    }

    [Fact]
    public void AddedBlockCarryingLinkFields_IsRefused()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        var clone = (JObject)ProposalWorkflows.Block(proposed, ProposalWorkflows.LINKED).DeepClone();
        clone["StableBlockId"] = "new-copy";
        clone["ReplacementTag"] = "library_copy";
        ProposalWorkflows.Blocks(proposed, 0).Add(clone);

        WorkflowProposal proposal = Build(proposed, Summary("new-copy"));

        Assert.Equal(ProposalRefusal.LibraryLink, Assert.Single(proposal.Changes).Refusal);
    }

    [Fact]
    public void VariablesDocumentClassesAndWorkflowFields_AreChanges()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        ((JArray)proposed["Variables"]!).Add(new JObject
        {
            ["ReplacementTag"] = "Region",
            ["DisplayName"] = "Region",
            ["Description"] = string.Empty,
            ["Type"] = "String",
        });
        proposed["DocumentClasses"]![0]!["DisplayName"] = "Annual report and accounts";
        proposed["Title"] = "Credit report v2";

        WorkflowProposal proposal = Build(proposed, []);

        Assert.Contains(proposal.Changes, change => change is { Scope: ProposedChangeScope.Variable, Kind: ProposedChangeKind.Added, TargetKey: "Region" });
        Assert.Contains(proposal.Changes, change => change is { Scope: ProposedChangeScope.DocumentClass, Field: "DisplayName", TargetKey: ProposalWorkflows.DOCUMENT_CLASS });
        Assert.Contains(proposal.Changes, change => change is { Scope: ProposedChangeScope.Workflow, Field: "Title" });
    }

    [Fact]
    public void TypeChange_IsOneChangeCarryingTheWholeSettings()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        JObject reader = ProposalWorkflows.Block(proposed, ProposalWorkflows.READER);
        reader["Type"] = "Merge";
        reader["Properties"] = new JObject { ["Type"] = "Merge", ["TextTemplate"] = "{revenue}" };

        WorkflowProposal proposal = Build(proposed, Summary(ProposalWorkflows.READER));

        ProposedChange change = Assert.Single(proposal.Changes);
        Assert.Equal("Type", change.Field);
        Assert.Equal("Merge", (string?)JObject.Parse(change.NewJson!)["Type"]);
    }

    [Fact]
    public void CheckError_OnABlockPath_RefusesOnlyThatBlocksChanges_AndOneWithoutABlockRefusesAll()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        ProposalWorkflows.Block(proposed, ProposalWorkflows.PRODUCER)["Properties"]!["UserPromptTemplate"] = NEW_PROMPT;
        ProposalWorkflows.Block(proposed, ProposalWorkflows.READER)["Name"] = "Executive summary";
        WorkflowProposal proposal = Build(proposed, Summary(ProposalWorkflows.PRODUCER, ProposalWorkflows.READER));

        WorkflowProposalBuilder.AttachCheckErrors(
            proposal,
            proposed.ToString(),
            [new ProposalCheckError(ProposalChecks.SCHEMA, "/Sections/0/Blocks/1/Properties: unknown key 'UserPrompt'")]);

        Assert.False(Assert.Single(proposal.Changes, change => change.Tag == ProposalWorkflows.READER).CanAccept);
        Assert.True(Assert.Single(proposal.Changes, change => change.Tag == ProposalWorkflows.PRODUCER).CanAccept);
        Assert.Equal(ProposalWorkflows.StableId(_workflow, ProposalWorkflows.READER), Assert.Single(proposal.CheckErrors).BlockKey);

        WorkflowProposalBuilder.AttachCheckErrors(
            proposal, proposed.ToString(), [new ProposalCheckError(ProposalChecks.STRUCTURE, "Workflow name must not be empty.")]);

        Assert.All(proposal.Changes, change => Assert.False(change.CanAccept));
    }

    [Fact]
    public void Proposal_RoundTripsThroughItsJson()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        ProposalWorkflows.Block(proposed, ProposalWorkflows.PRODUCER)["Properties"]!["UserPromptTemplate"] = NEW_PROMPT;
        WorkflowProposal proposal = Build(proposed, Summary(ProposalWorkflows.PRODUCER));

        WorkflowProposal read = WorkflowProposal.FromJson(proposal.ToJson());

        Assert.Equal(proposal.Changes, read.Changes);
        Assert.Equal(proposal.Summary, read.Summary);
    }

    [Fact]
    public void AddedSection_CarriesItsBlocks_AndTheyAreNotAlsoAddedOneByOne()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        var section = (JObject)proposed["Sections"]![1]!.DeepClone();
        section["StableSectionId"] = "new-appendix";
        section["Title"] = "Appendix";
        section["Order"] = 2;
        var block = (JObject)((JArray)section["Blocks"]!)[0]!;
        block["StableBlockId"] = "new-appendix-merge";
        block["Id"] = Guid.NewGuid().ToString();
        block["ReplacementTag"] = "appendix";
        ((JArray)proposed["Sections"]!).Add(section);

        WorkflowProposal proposal = Build(proposed, [new ProposalSummaryEntry(null, "new-appendix", "Adds an appendix.")]);

        ProposedChange added = Assert.Single(proposal.Changes);
        Assert.Equal(ProposedChangeScope.Section, added.Scope);
        Assert.Contains("new-appendix-merge", added.NewJson!, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckErrorOnABlockOfAnAddedSection_RefusesTheSection()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        var section = (JObject)proposed["Sections"]![1]!.DeepClone();
        section["StableSectionId"] = "new-appendix";
        var block = (JObject)((JArray)section["Blocks"]!)[0]!;
        block["StableBlockId"] = "new-appendix-merge";
        block["ReplacementTag"] = "appendix";
        ((JArray)proposed["Sections"]!).Add(section);
        WorkflowProposal proposal = Build(proposed, [new ProposalSummaryEntry(null, "new-appendix", "Adds an appendix.")]);

        WorkflowProposalBuilder.AttachCheckErrors(
            proposal, proposed.ToString(), [new ProposalCheckError(ProposalChecks.SCHEMA, "/Sections/2/Blocks/0/Properties: unknown key 'X'")]);

        Assert.False(Assert.Single(proposal.Changes).CanAccept);
    }

    [Fact]
    public void WorkflowWithoutStableIds_PairsByRowId()
    {
        JObject baseJson = ProposalWorkflows.Edit(_base);
        foreach (JProperty stable in baseJson.Descendants().OfType<JProperty>()
            .Where(property => property.Name is "StableBlockId" or "StableSectionId").ToList())
        {
            stable.Remove();
        }

        var proposed = (JObject)baseJson.DeepClone();
        ProposalWorkflows.Block(proposed, ProposalWorkflows.PRODUCER)["Properties"]!["UserPromptTemplate"] = NEW_PROMPT;

        WorkflowProposal proposal = WorkflowProposalBuilder.Build(baseJson.ToString(), proposed.ToString(), Summary(ProposalWorkflows.PRODUCER));

        ProposedChange change = Assert.Single(proposal.Changes);
        Assert.Equal("Properties.UserPromptTemplate", change.Field);
        Assert.Equal("id:" + ProposalWorkflows.LiveBlock(_workflow, ProposalWorkflows.PRODUCER).Id, change.TargetKey);
    }

    [Fact]
    public void AnInventedKeyTooLongForADecisionRow_GivesAnIdThatFits()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        var clone = (JObject)ProposalWorkflows.Block(proposed, ProposalWorkflows.READER).DeepClone();
        clone["StableBlockId"] = new string('x', 2000);
        clone["Id"] = Guid.NewGuid().ToString();
        clone["ReplacementTag"] = "risk";
        ProposalWorkflows.Blocks(proposed, 0).Add(clone);

        WorkflowProposal proposal = Build(proposed, []);

        Assert.True(Assert.Single(proposal.Changes).Id.Length <= WorkflowProposalBuilder.MAX_CHANGE_ID_LENGTH);
    }

    [Fact]
    public void ExchangeRateAndPeerFinancials_AreOutboundTypes()
    {
        Assert.Contains("ExchangeRate", WorkflowProposalBuilder.OutboundBlockTypes);
        Assert.Contains("PeerFinancials", WorkflowProposalBuilder.OutboundBlockTypes);
    }

    [Fact]
    public void ErrorNamingARepeatedTag_RefusesEveryBlockWithThatTag()
    {
        JObject proposed = ProposalWorkflows.Edit(_base);
        ProposalWorkflows.Block(proposed, ProposalWorkflows.PRODUCER)["Name"] = "Revenue split";
        ProposalWorkflows.Block(proposed, ProposalWorkflows.MERGE)["ReplacementTag"] = ProposalWorkflows.PRODUCER;
        ProposalWorkflows.Block(proposed, ProposalWorkflows.READER)["Name"] = "Executive summary";
        WorkflowProposal proposal = Build(proposed, []);

        WorkflowProposalBuilder.AttachCheckErrors(
            proposal, proposed.ToString(), [new ProposalCheckError(ProposalChecks.AGENT_GATES, $"Block '{ProposalWorkflows.PRODUCER}' is wrong.")]);

        Assert.All(proposal.Changes.Where(change => change.Tag == ProposalWorkflows.PRODUCER), change => Assert.False(change.CanAccept));
        Assert.True(Assert.Single(proposal.Changes, change => change.Tag == ProposalWorkflows.READER).CanAccept);
    }

    private WorkflowProposal Build(JObject proposed, List<ProposalSummaryEntry> summary)
    {
        return WorkflowProposalBuilder.Build(_base, proposed.ToString(), summary);
    }

    private List<ProposalSummaryEntry> Summary(params string[] tagsOrIds)
    {
        return [.. tagsOrIds.Select(tag => new ProposalSummaryEntry(tag, null, $"Changes {tag}."))];
    }
}
