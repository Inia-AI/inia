using IniaAi.Core.Workflows.Dependencies;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApp.Components.WorkflowEditor.Dependencies;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Dependencies;

public class DependencySceneBuilderTests
{
    [Fact]
    public void Chain_ShowsOneStepEachWay_WithAStubForEachFurtherStep()
    {
        var workflow = new Workflow { Title = "Line" };
        Section section = new("Line", workflow);
        Block first = AiQuery(section, "first", "Start.");
        Block before = AiQuery(section, "before", "Use {first.answer}");
        Block focus = AiQuery(section, "focus", "Use {before.answer}");
        Block after = AiQuery(section, "after", "Use {focus.answer}");
        Block last = AiQuery(section, "last", "Use {after.answer}");

        DependencyScene scene = ChainOf(workflow, focus);

        Assert.Equal(DependencyViewModes.CHAIN, scene.Mode);
        Assert.Equal(DependencyRoles.FOCUS, Card(scene, focus.Id).Role);
        Assert.Equal(DependencyRoles.UPSTREAM, Card(scene, before.Id).Role);
        Assert.Equal(DependencyRoles.DOWNSTREAM, Card(scene, after.Id).Role);
        Assert.DoesNotContain(scene.Cards, card => card.Id == first.Id || card.Id == last.Id);

        DependencyCard upStub = Card(scene, DependencyViewState.UpstreamStubId(before.Id));
        Assert.Equal("+1 more in 1 section", upStub.Label);
        Assert.Contains(scene.Links, link => link.SourceId == upStub.Id && link.TargetId == before.Id);
        DependencyCard downStub = Card(scene, DependencyViewState.DownstreamStubId(after.Id));
        Assert.Contains(scene.Links, link => link.SourceId == after.Id && link.TargetId == downStub.Id);
    }

    [Fact]
    public void ExpandingAStub_OpensExactlyOneMoreStep()
    {
        var workflow = new Workflow { Title = "Line" };
        Section section = new("Line", workflow);
        Block root = AiQuery(section, "root", "Start.");
        Block first = AiQuery(section, "first", "Use {root.answer}");
        Block before = AiQuery(section, "before", "Use {first.answer}");
        Block focus = AiQuery(section, "focus", "Use {before.answer}");
        var state = new DependencyViewState();
        _ = state.Focus(focus.Id);

        Assert.True(state.ExpandStub(DependencyViewState.UpstreamStubId(before.Id)));
        DependencyScene scene = DependencySceneBuilder.Build(WorkflowDependencyMap.ForTemplate(workflow), state);

        Assert.Equal(DependencyRoles.UPSTREAM, Card(scene, first.Id).Role);
        Assert.DoesNotContain(scene.Cards, card => card.Id == root.Id);
        Assert.Contains(scene.Cards, card => card.Id == DependencyViewState.UpstreamStubId(first.Id));
        Assert.DoesNotContain(scene.Cards, card => card.Id == DependencyViewState.UpstreamStubId(before.Id));
    }

    [Fact]
    public void Chain_DrawsOnlyLinksThatLeadToTheFocus_NotSideLinksBetweenRevealedBlocks()
    {
        var workflow = new Workflow { Title = "Fan" };
        Section section = new("Fan", workflow);
        Block shared = AiQuery(section, "shared", "Start.");
        Block reader = AiQuery(section, "reader", "Use {shared.answer}");
        Block focus = AiQuery(section, "focus", "Use {shared.answer} and {reader.answer}");
        var state = new DependencyViewState();
        _ = state.Focus(focus.Id);
        _ = state.ExpandStub(DependencyViewState.UpstreamStubId(reader.Id));

        DependencyScene scene = DependencySceneBuilder.Build(WorkflowDependencyMap.ForTemplate(workflow), state);

        Assert.Contains(scene.Links, link => link.SourceId == shared.Id && link.TargetId == focus.Id);
        Assert.Contains(scene.Links, link => link.SourceId == reader.Id && link.TargetId == focus.Id);
        Assert.DoesNotContain(scene.Links, link => link.SourceId == shared.Id && link.TargetId == reader.Id);
    }

    [Fact]
    public void AStub_CountsTheHiddenBlocks_GroupedBySection()
    {
        var workflow = new Workflow { Title = "Sections" };
        Section notes = new("Notes", workflow);
        Block noteA = AiQuery(notes, "note_a", "A.");
        Block noteB = AiQuery(notes, "note_b", "B.");
        Section figures = new("Figures", workflow);
        Block figure = AiQuery(figures, "figure", "C.");
        Section summary = new("Summary", workflow);
        Block gather = AiQuery(summary, "gather", "{note_a.answer} {note_b.answer} {figure.answer}");
        Block focus = AiQuery(summary, "focus", "Use {gather.answer}");

        DependencyScene scene = ChainOf(workflow, focus);

        DependencyCard stub = Card(scene, DependencyViewState.UpstreamStubId(gather.Id));
        Assert.Equal("+3 more in 2 sections", stub.Label);
        Assert.Equal(["Notes (2)", "Figures (1)"], stub.Summary);
        Assert.DoesNotContain(scene.Cards, card => card.Id == noteA.Id || card.Id == noteB.Id || card.Id == figure.Id);
    }

    [Fact]
    public void Chain_ShowsAMergeSource_AsABlock()
    {
        var workflow = new Workflow { Title = "Merges" };
        Section sources = new("Source section", workflow);
        Block source = Merge(sources, "block_0", "Start.");
        Section readers = new("Reader section", workflow);
        Block reader = Merge(readers, "block_1", "{block_0}");

        DependencyScene scene = ChainOf(workflow, reader);

        Assert.Equal(DependencyRoles.UPSTREAM, Card(scene, source.Id).Role);
        DependencyLink link = Assert.Single(scene.Links, link => link.TargetId == reader.Id);
        Assert.Equal(source.Id, link.SourceId);
        Assert.Equal(DependencyLinkStyles.DATA, link.Style);
        Assert.Null(scene.EmptyMessage);
    }

    [Fact]
    public void Chain_StopsAtTheMergeItReads_WithAStubForWhatTheMergeReads()
    {
        var workflow = new Workflow { Title = "Merges" };
        Section section = new("Merges", workflow);
        Block source = AiQuery(section, "source", "Start.");
        Block merge = Merge(section, "merge", "{source.answer}");
        Block focus = AiQuery(section, "focus", "Use {merge}");

        DependencyScene scene = ChainOf(workflow, focus);

        Assert.Equal(DependencyRoles.UPSTREAM, Card(scene, merge.Id).Role);
        Assert.DoesNotContain(scene.Cards, card => card.Id == source.Id);
        Assert.Contains(scene.Cards, card => card.Id == DependencyViewState.UpstreamStubId(merge.Id));
    }

    [Fact]
    public void ABlockReadDirectlyAndThroughAMerge_IsLinkedToBoth()
    {
        var workflow = new Workflow { Title = "Both" };
        Section section = new("Both", workflow);
        Block source = AiQuery(section, "source", "Start.");
        Block merge = Merge(section, "merge", "{source.answer}");
        Block focus = AiQuery(section, "focus", "{source.answer} and {merge}");

        DependencyScene scene = ChainOf(workflow, focus);

        List<string> sourceIds = [.. scene.Links.Where(link => link.TargetId == focus.Id).Select(link => link.SourceId)];
        Assert.Equal(2, sourceIds.Count);
        Assert.Contains(source.Id, sourceIds);
        Assert.Contains(merge.Id, sourceIds);
    }

    [Fact]
    public void AFocusedMerge_ShowsWhatItReadsAndWhatReadsIt()
    {
        var workflow = new Workflow { Title = "Merge focus" };
        Section section = new("Merge focus", workflow);
        Block source = AiQuery(section, "source", "Start.");
        Block merge = Merge(section, "merge", "{source.answer}");
        Block reader = AiQuery(section, "reader", "Use {merge}");

        DependencyScene scene = ChainOf(workflow, merge);

        Assert.Equal(DependencyRoles.FOCUS, Card(scene, merge.Id).Role);
        Assert.Equal(DependencyRoles.UPSTREAM, Card(scene, source.Id).Role);
        Assert.Equal(DependencyRoles.DOWNSTREAM, Card(scene, reader.Id).Role);
    }

    [Fact]
    public void Inputs_AreHiddenBehindACount_UntilTheReaderOpensThem()
    {
        var workflow = new Workflow { Title = "Inputs" };
        _ = new WorkflowVariable(workflow, "report_date");
        Section section = new("Inputs", workflow);
        Block focus = AiQuery(section, "focus", "As at {report_date} for {Counterparty.Name}");
        var state = new DependencyViewState();
        _ = state.Focus(focus.Id);
        WorkflowDependencyMap map = WorkflowDependencyMap.ForTemplate(workflow);

        DependencyScene closed = DependencySceneBuilder.Build(map, state);

        Assert.Equal(2, Card(closed, focus.Id).InputCount);
        Assert.DoesNotContain(closed.Cards, card => card.Kind == DependencyCardKinds.INPUT);

        state.ToggleInputs(focus.Id);
        DependencyScene open = DependencySceneBuilder.Build(map, state);

        Assert.True(Card(open, focus.Id).InputsOpen);
        List<DependencyCard> inputs = [.. open.Cards.Where(card => card.Kind == DependencyCardKinds.INPUT)];
        Assert.Equal(["Workflow input", "System value"], inputs.Select(card => card.Detail));
        Assert.All(inputs, input => Assert.Contains(open.Links, link =>
            link.SourceId == input.Id && link.TargetId == focus.Id && link.Style == DependencyLinkStyles.INPUT));
    }

    [Fact]
    public void AnUnresolvedTag_IsAlwaysShown_AndIsNotCountedAsAnInput()
    {
        var workflow = new Workflow { Title = "Unresolved" };
        Section section = new("Unresolved", workflow);
        Block focus = AiQuery(section, "focus", "Use {nobody_makes_this}");

        DependencyScene scene = ChainOf(workflow, focus);

        DependencyCard input = Assert.Single(scene.Cards, card => card.Kind == DependencyCardKinds.INPUT);
        Assert.True(input.IsUnresolved);
        Assert.Equal("No block or input supplies {nobody_makes_this}", input.Label);
        Assert.Equal(0, Card(scene, focus.Id).InputCount);
    }

    [Fact]
    public void SectionGates_AreHiddenBehindACount_UntilOpened()
    {
        var workflow = new Workflow { Title = "Gates" };
        Section checks = new("Checks", workflow);
        Block check = AiQuery(checks, "check", "Should the next section run?");
        Section gated = new("Gated", workflow) { TextInputTemplate = "{check.stop}" };
        Block focus = AiQuery(gated, "focus", "Work.");
        var state = new DependencyViewState();
        _ = state.Focus(focus.Id);
        WorkflowDependencyMap map = WorkflowDependencyMap.ForTemplate(workflow);

        DependencyScene closed = DependencySceneBuilder.Build(map, state);

        Assert.Equal(1, Card(closed, focus.Id).GateCount);
        Assert.DoesNotContain(closed.Cards, card => card.Id == check.Id);
        Assert.Equal(DependencySceneBuilder.EMPTY_CHAIN_MESSAGE, closed.EmptyMessage);

        state.ToggleGates(focus.Id);
        DependencyScene open = DependencySceneBuilder.Build(map, state);

        DependencyLink gate = Assert.Single(open.Links);
        Assert.Equal((check.Id, focus.Id, DependencyLinkStyles.GATE), (gate.SourceId, gate.TargetId, gate.Style));
    }

    [Fact]
    public void Map_DrawsSectionsAsCards_WithLinksWeightedByTheirCount()
    {
        var workflow = new Workflow { Title = "Map" };
        Section notes = new("Notes", workflow);
        _ = AiQuery(notes, "note_a", "A.");
        _ = AiQuery(notes, "note_b", "B.");
        Section analysis = new("Analysis", workflow);
        _ = AiQuery(analysis, "analysis", "{note_a.answer} {note_b.answer}");
        Section report = new("Report", workflow);
        _ = AiQuery(report, "report", "{analysis.answer}");

        DependencyScene scene = DependencySceneBuilder.Build(WorkflowDependencyMap.ForTemplate(workflow), new DependencyViewState());

        Assert.Equal(DependencyViewModes.MAP, scene.Mode);
        Assert.All(scene.Cards, card => Assert.Equal(DependencyCardKinds.SECTION, card.Kind));
        DependencyCard notesCard = Card(scene, DependencySceneBuilder.SectionCardId(notes.Id));
        Assert.Equal(2, notesCard.BlockCount);
        Assert.Equal(["2 AI Query"], notesCard.Summary);
        Assert.Equal(2, MapLink(scene, notes, analysis).Weight);
        Assert.Equal(1, MapLink(scene, analysis, report).Weight);
    }

    [Fact]
    public void Map_OpensASectionIntoItsBlocks()
    {
        var workflow = new Workflow { Title = "Map" };
        Section notes = new("Notes", workflow);
        Block noteA = AiQuery(notes, "note_a", "A.");
        Block noteB = AiQuery(notes, "note_b", "B.");
        Section analysis = new("Analysis", workflow);
        _ = AiQuery(analysis, "analysis", "{note_a.answer} {note_b.answer}");
        var state = new DependencyViewState();
        state.ToggleSection(notes.Id);

        DependencyScene scene = DependencySceneBuilder.Build(WorkflowDependencyMap.ForTemplate(workflow), state);

        string boxId = DependencySceneBuilder.SectionBoxId(notes.Id);
        DependencyGroup box = Assert.Single(scene.Groups);
        Assert.Equal((boxId, DependencyGroupKinds.SECTION, "Notes"), (box.Id, box.Kind, box.Label));
        Assert.All([Card(scene, noteA.Id), Card(scene, noteB.Id)], card =>
        {
            Assert.Equal(DependencyCardKinds.BLOCK, card.Kind);
            Assert.Equal(boxId, card.GroupId);
        });
        string analysisCard = DependencySceneBuilder.SectionCardId(analysis.Id);
        Assert.Contains(scene.Links, link => link.SourceId == noteA.Id && link.TargetId == analysisCard);
        Assert.Contains(scene.Links, link => link.SourceId == noteB.Id && link.TargetId == analysisCard);
    }

    [Fact]
    public void Map_BlockCardsInAnOpenSection_KeepTheirInputAndGateCounts()
    {
        var workflow = new Workflow { Title = "Map" };
        _ = new WorkflowVariable(workflow, "report_date");
        Section checks = new("Checks", workflow);
        _ = AiQuery(checks, "check", "Should the next section run?");
        Section gated = new("Gated", workflow) { TextInputTemplate = "{check.stop}" };
        Block focus = AiQuery(gated, "focus", "As at {report_date}.");
        var state = new DependencyViewState();
        state.ToggleSection(gated.Id);

        DependencyScene scene = DependencySceneBuilder.Build(WorkflowDependencyMap.ForTemplate(workflow), state);

        DependencyCard card = Card(scene, focus.Id);
        Assert.Equal((1, 1), (card.InputCount, card.GateCount));
    }

    [Fact]
    public void OpeningACountFromTheMap_OpensThatBlocksChainWithThemShown()
    {
        var state = new DependencyViewState();

        state.ToggleInputs("a");

        Assert.Equal((DependencyViewModes.CHAIN, "a"), (state.Mode, state.FocusBlockId));
        Assert.Contains("a", state.InputsOpenFor);

        state.ShowMap();
        state.ToggleGates("b");

        Assert.Equal((DependencyViewModes.CHAIN, "b"), (state.Mode, state.FocusBlockId));
        Assert.Contains("b", state.GatesOpenFor);
        Assert.Empty(state.InputsOpenFor);
    }

    [Fact]
    public void OpeningACountInTheChain_KeepsTheFocus()
    {
        var state = new DependencyViewState();
        _ = state.Focus("a");

        state.ToggleInputs("upstream");

        Assert.Equal("a", state.FocusBlockId);
        Assert.Contains("upstream", state.InputsOpenFor);
    }

    [Fact]
    public void Map_DrawsTheMergesOfAnOpenSectionAsBlocks()
    {
        var workflow = new Workflow { Title = "Map merges" };
        Section inputs = new("Inputs", workflow);
        _ = AiQuery(inputs, "source", "A.");
        Section joins = new("Joins", workflow);
        Block merge = Merge(joins, "joined", "{source.answer}");
        Section report = new("Report", workflow);
        _ = AiQuery(report, "report", "{joined}");
        var state = new DependencyViewState();
        state.ToggleSection(joins.Id);

        DependencyScene scene = DependencySceneBuilder.Build(WorkflowDependencyMap.ForTemplate(workflow), state);

        Assert.Equal(DependencySceneBuilder.SectionBoxId(joins.Id), Card(scene, merge.Id).GroupId);
        Assert.Contains(scene.Links, link => link.SourceId == DependencySceneBuilder.SectionCardId(inputs.Id) && link.TargetId == merge.Id);
        Assert.Contains(scene.Links, link => link.SourceId == merge.Id && link.TargetId == DependencySceneBuilder.SectionCardId(report.Id));
    }

    [Fact]
    public void Map_OpensASectionOfOnlyMergesIntoItsBlocks()
    {
        var workflow = new Workflow { Title = "Map merges" };
        Section sources = new("Source section", workflow);
        _ = Merge(sources, "block_0", "Start.");
        Section readers = new("Reader section", workflow);
        Block reader = Merge(readers, "block_1", "{block_0}");
        var state = new DependencyViewState();
        state.ToggleSection(readers.Id);

        DependencyScene scene = DependencySceneBuilder.Build(WorkflowDependencyMap.ForTemplate(workflow), state);

        Assert.Contains(scene.Groups, group => group.Id == DependencySceneBuilder.SectionBoxId(readers.Id));
        Assert.Equal(DependencySceneBuilder.SectionBoxId(readers.Id), Card(scene, reader.Id).GroupId);
        Assert.DoesNotContain(scene.Cards, card => card.Id == DependencySceneBuilder.SectionCardId(readers.Id));
    }

    [Fact]
    public void FocusingAnotherBlock_StartsFromOneStepAgain_AndKeepsTheTrail()
    {
        var state = new DependencyViewState();
        _ = state.Focus("a");
        _ = state.ExpandStub(DependencyViewState.UpstreamStubId("b"));
        state.ToggleInputs("a");

        Assert.False(state.Focus("a"));
        Assert.True(state.Focus("c"));
        Assert.Empty(state.ExpandedUpstream);
        Assert.Empty(state.InputsOpenFor);
        Assert.True(state.Focus("a"));
        Assert.Equal(["c", "a"], state.Trail);

        state.Forget(id => id != "a");
        Assert.Equal("c", state.FocusBlockId);
        Assert.Equal(["c"], state.Trail);
    }

    [Fact]
    public void ABlockSentToTheMap_IsTheFocusInsideItsOpenedSection()
    {
        var workflow = new Workflow { Title = "Map" };
        Section notes = new("Notes", workflow);
        Block note = AiQuery(notes, "note", "A.");
        Section analysis = new("Analysis", workflow);
        _ = AiQuery(analysis, "analysis", "{note.answer}");
        WorkflowDependencyMap map = WorkflowDependencyMap.ForTemplate(workflow);
        var state = new DependencyViewState();
        state.RequestMapFocus(note.Id);

        Assert.True(state.ApplyPendingMapFocus(id => map.FindBlock(id) is { } block ? DependencySceneBuilder.SectionKey(block) : null));
        DependencyScene scene = DependencySceneBuilder.Build(map, state);

        Assert.Null(state.PendingMapFocusId);
        Assert.Equal((DependencyViewModes.MAP, note.Id), (scene.Mode, scene.FocusId));
        DependencyCard card = Card(scene, note.Id);
        Assert.Equal((DependencyRoles.FOCUS, DependencySceneBuilder.SectionBoxId(notes.Id)), (card.Role, card.GroupId));
        Assert.Equal(DependencyCardKinds.SECTION, Card(scene, DependencySceneBuilder.SectionCardId(analysis.Id)).Kind);
    }

    [Fact]
    public void AMapFocusForABlockThatIsGone_IsDropped()
    {
        var state = new DependencyViewState();
        state.RequestMapFocus("gone");

        Assert.False(state.ApplyPendingMapFocus(_ => null));
        Assert.False(state.ApplyPendingMapFocus(_ => "section"));
        Assert.Null(state.PendingMapFocusId);
        Assert.Null(state.FocusBlockId);
        Assert.Empty(state.OpenSections);
    }

    [Fact]
    public void ANewLook_KeepsTheSwitches_AndStartsANewTrail()
    {
        var state = new DependencyViewState { ShowInputs = true, ShowGates = true };
        _ = state.Focus("a");
        state.ToggleSection("section");

        DependencyViewState fresh = state.WithSwitchesOnly();

        Assert.Equal((true, true), (fresh.ShowInputs, fresh.ShowGates));
        Assert.Null(fresh.FocusBlockId);
        Assert.Empty(fresh.Trail);
        Assert.Empty(fresh.OpenSections);
        Assert.Equal(DependencyViewModes.MAP, fresh.Mode);
    }

    private static DependencyScene ChainOf(Workflow workflow, Block focus)
    {
        var state = new DependencyViewState();
        _ = state.Focus(focus.Id);

        return DependencySceneBuilder.Build(WorkflowDependencyMap.ForTemplate(workflow), state);
    }

    private static DependencyCard Card(DependencyScene scene, string id)
    {
        return Assert.Single(scene.Cards, card => card.Id == id);
    }

    private static DependencyLink MapLink(DependencyScene scene, Section source, Section target)
    {
        return Assert.Single(scene.Links, link =>
            link.SourceId == DependencySceneBuilder.SectionCardId(source.Id)
            && link.TargetId == DependencySceneBuilder.SectionCardId(target.Id));
    }

    private static Block AiQuery(Section section, string tag, string prompt)
    {
        return new Block(BlockTypes.AiQuery, section)
        {
            ReplacementTag = tag,
            Name = tag,
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };
    }

    private static Block Merge(Section section, string tag, string template)
    {
        return new Block(BlockTypes.Merge, section)
        {
            ReplacementTag = tag,
            Name = tag,
            Properties = new MergeProperties { TextTemplate = template },
        };
    }
}
