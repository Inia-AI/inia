using IniaAi.Core.Workflows.Dependencies;
using IniaAi.Core.Workflows.Graph;
using IniaAi.Core.Workflows.Scheduling;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;

namespace IniaAi.Core.Tests.Workflows.Dependencies;

public class WorkflowDependencyMapTests
{
    private const string ANNUAL = "AR";
    private const string QUARTERLY = "QR";
    private const string PERIOD_PARAM = "PERIOD_ID";

    [Fact]
    public void ParameterizedVariants_FoldOntoTheTemplateBlock_WithTheVariantKeyOnTheEdge()
    {
        var workflow = new Workflow { Title = "Variants" };
        Section notes = Parameterized(workflow, "Notes", [Variant(ANNUAL), Variant(QUARTERLY)]);
        Block noteBlock = AiQuery(notes, "notes", "Find the notes.");
        Section summary = new("Summary", workflow);
        Block reader = AiQuery(summary, "reader", "Quarterly notes: {notes_QR.answer}");

        WorkflowDependencyMap map = WorkflowDependencyMap.ForTemplate(workflow);

        DependencyBlock folded = Assert.Single(map.Blocks, block => block.Block == noteBlock);
        Assert.Equal([ANNUAL, QUARTERLY], folded.VariantKeys);
        DependencyEdge edge = Assert.Single(map.IncomingEdges(reader.Id));
        Assert.Equal(noteBlock.Id, edge.SourceId);
        Assert.Equal(DependencyEdgeKind.Data, edge.Kind);
        Assert.Equal(["notes_QR.answer"], edge.Tags);
        Assert.Equal([QUARTERLY], edge.VariantKeys);
    }

    [Fact]
    public void NestedParameterInsideATag_ResolvesPerVariant()
    {
        var workflow = new Workflow { Title = "Nested" };
        Section notes = Parameterized(workflow, "Notes", [Variant(ANNUAL), Variant(QUARTERLY)]);
        Block pension = AiQuery(notes, "pension_notes", "Find the pension notes.");
        Section content = Parameterized(workflow, "Content", [
            Variant(ANNUAL, (PERIOD_PARAM, ANNUAL)),
            Variant(QUARTERLY, (PERIOD_PARAM, QUARTERLY))]);
        Block reader = AiQuery(content, "pension_content", "Summarise {pension_notes_{PERIOD_ID}.answer}");

        WorkflowDependencyMap map = WorkflowDependencyMap.ForTemplate(workflow);

        DependencyEdge edge = Assert.Single(map.IncomingEdges(reader.Id));
        Assert.Equal(pension.Id, edge.SourceId);
        Assert.Equal(["pension_notes_AR.answer", "pension_notes_QR.answer"], edge.Tags);
        Assert.Equal([ANNUAL, QUARTERLY], edge.VariantKeys);
        Assert.DoesNotContain(map.Issues, issue => issue.Kind == DependencyIssueKind.UnresolvedTag);
    }

    [Fact]
    public void AParameterNameLeftInAPromptPreview_IsNotAnUnresolvedTag()
    {
        var workflow = new Workflow { Title = "Preview" };
        Section notes = Parameterized(workflow, "Notes", [
            Variant(ANNUAL, (PERIOD_PARAM, ANNUAL)),
            Variant(QUARTERLY, (PERIOD_PARAM, QUARTERLY))]);
        Block block = AiQuery(notes, "notes", "Find the {PERIOD_ID} notes.");
        ((AiQueryProperties)block.Properties).UserPromptTemplatePreview = "Placeholders: {PERIOD_ID}";

        WorkflowDependencyMap map = WorkflowDependencyMap.ForTemplate(workflow);

        Assert.Empty(map.Issues);
        Assert.Empty(map.Inputs);
    }

    [Fact]
    public void SiblingReadInsideAParameterizedSection_StaysWithinTheVariant()
    {
        var workflow = new Workflow { Title = "Siblings" };
        Section notes = Parameterized(workflow, "Notes", [Variant(ANNUAL), Variant(QUARTERLY)]);
        Block first = AiQuery(notes, "first", "Find.");
        Block second = AiQuery(notes, "second", "Use {first.answer}");
        Section other = new("Other", workflow);
        Block sameTagElsewhere = AiQuery(other, "first", "An unrelated block sharing the tag.");

        WorkflowDependencyMap map = WorkflowDependencyMap.ForTemplate(workflow);

        DependencyEdge edge = Assert.Single(map.IncomingEdges(second.Id));
        Assert.Equal(first.Id, edge.SourceId);
        Assert.Equal(["first_AR.answer", "first_QR.answer"], edge.Tags);
        Assert.Empty(map.OutgoingEdges(sameTagElsewhere.Id));
    }

    [Fact]
    public void TheBlocksOwnTextInput_WinsOverABlockCarryingTheSameTag()
    {
        var workflow = new Workflow { Title = "Local" };
        Section section = new("Section", workflow);
        Block collision = AiQuery(section, "query", "Produces a value tagged query.");
        Block reader = AiQuery(section, "reader", "Search for {query}");
        _ = new TextInput(reader, "query", "local value");

        WorkflowDependencyMap map = WorkflowDependencyMap.ForTemplate(workflow);

        Assert.Empty(map.IncomingEdges(reader.Id));
        Assert.Empty(map.OutgoingEdges(collision.Id));
        Assert.DoesNotContain(map.Issues, issue => issue.Tag == "query");
    }

    [Fact]
    public void ASignatureField_IsAnInputEvenWhenABlockCarriesTheSameTag()
    {
        var workflow = new Workflow { Title = "Signature" };
        _ = new WorkflowSignatureField(workflow, "SIGrules") { DefaultValue = "the rules" };
        Section section = new("Section", workflow);
        Block collision = AiQuery(section, "SIGrules", "Produces a value tagged SIGrules.");
        Block reader = AiQuery(section, "reader", "Apply {SIGrules}");

        WorkflowDependencyMap map = WorkflowDependencyMap.ForTemplate(workflow);

        DependencyEdge edge = Assert.Single(map.IncomingEdges(reader.Id));
        DependencyInput input = Assert.IsType<DependencyInput>(map.FindInput(edge.SourceId));
        Assert.Equal(DependencyInputKind.Signature, input.Kind);
        Assert.Equal("SIGrules", input.Tag);
        Assert.Empty(map.OutgoingEdges(collision.Id));
    }

    [Fact]
    public void AnArrayIndex_ReducesAnEntityTag_ButLeavesABlockTagUnresolvedAsTheRunDoes()
    {
        var workflow = new Workflow { Title = "Indexes" };
        Section section = new("Section", workflow);
        Block peers = AiQuery(section, "peer_list", "List peers.");
        Block reader = AiQuery(section, "reader", "Compare {PeerCompanies[1].Name} using {peer_list[0].answer}");

        WorkflowDependencyMap map = WorkflowDependencyMap.ForTemplate(workflow);

        List<DependencyEdge> incoming = [.. map.IncomingEdges(reader.Id)];
        Assert.DoesNotContain(incoming, edge => edge.SourceId == peers.Id);
        Assert.Contains(incoming, edge =>
            map.FindInput(edge.SourceId) == new DependencyInput(edge.SourceId, DependencyInputKind.System, "PeerCompanies"));
        DependencyIssue issue = Assert.Single(map.Issues);
        Assert.Equal((DependencyIssueKind.UnresolvedTag, "peer_list[0]"), (issue.Kind, issue.Tag));
    }

    [Fact]
    public void AVariableWithADefault_WinsOverABlockCarryingTheSameTag()
    {
        var workflow = new Workflow { Title = "Variable" };
        _ = new WorkflowVariable(workflow, "query") { DefaultValue = "the default" };
        Section section = new("Section", workflow);
        Block collision = AiQuery(section, "query", "Produces a value tagged query.");
        Block reader = AiQuery(section, "reader", "Search for {query}");
        Block dotted = AiQuery(section, "dotted", "Search for {query.answer}");

        WorkflowDependencyMap map = WorkflowDependencyMap.ForTemplate(workflow);

        DependencyEdge edge = Assert.Single(map.IncomingEdges(reader.Id));
        Assert.Equal(DependencyInputKind.Variable, map.FindInput(edge.SourceId)?.Kind);
        Assert.Equal(collision.Id, Assert.Single(map.IncomingEdges(dotted.Id)).SourceId);
    }

    [Fact]
    public void AVariableWithNoDefault_StillLetsTheBlockSupplyTheTag()
    {
        var workflow = new Workflow { Title = "Variable" };
        _ = new WorkflowVariable(workflow, "query");
        Section section = new("Section", workflow);
        Block collision = AiQuery(section, "query", "Produces a value tagged query.");
        Block reader = AiQuery(section, "reader", "Search for {query}");

        WorkflowDependencyMap map = WorkflowDependencyMap.ForTemplate(workflow);

        List<DependencyEdge> incoming = [.. map.IncomingEdges(reader.Id)];
        Assert.Equal(2, incoming.Count);
        Assert.Contains(incoming, edge => edge.SourceId == collision.Id);
    }

    [Fact]
    public void ADocumentReaderDispatchedBeforeItsProducer_IsAnIssueNotAnEdge()
    {
        var workflow = new Workflow { Title = "Segments" };
        Section first = new("First", workflow);
        Block reader = RagReader(first, "reader", "web");
        _ = new Block(BlockTypes.BuildTopicsForAllDocuments, first) { ReplacementTag = "topics" };
        Section second = new("Second", workflow) { Order = 1 };
        Block search = new(BlockTypes.IntelligentSearch, second)
        {
            ReplacementTag = "search",
            Properties = new IntelligentSearchProperties { SearchTermTemplate = "news", CreatesDocumentClass = "web" },
        };

        WorkflowDependencyMap map = WorkflowDependencyMap.ForTemplate(workflow);

        Assert.DoesNotContain(map.Edges, edge => edge.Kind == DependencyEdgeKind.Document);
        DependencyIssue issue = Assert.Single(map.Issues);
        Assert.Equal((DependencyIssueKind.ReadsDocumentsBeforeProducer, "web"), (issue.Kind, issue.Tag));
        Assert.Equal([reader.Id, search.Id], issue.BlockIds);
    }

    [Fact]
    public void ACounterpartyReaderDispatchedBeforeTheWriter_IsAnIssueNotAnEdge()
    {
        var workflow = new Workflow { Title = "Segments" };
        Section first = new("First", workflow);
        Block reader = AiQuery(first, "styled", "Write in the {Counterparty.Style} style.");
        _ = new Block(BlockTypes.BuildTopicsForAllDocuments, first) { ReplacementTag = "topics" };
        Section second = new("Second", workflow) { Order = 1 };
        Block writer = AiQuery(second, "style", "Pick a style.");
        writer.ShouldUpdateCounterpartyWithResult = true;
        writer.CounterpartyResultType = CounterpartyResultType.Property;
        writer.UpdateCounterpartyPropertyKey = "Style";

        WorkflowDependencyMap map = WorkflowDependencyMap.ForTemplate(workflow);

        Assert.DoesNotContain(map.Edges, edge => edge.Kind == DependencyEdgeKind.Counterparty);
        DependencyIssue issue = Assert.Single(map.Issues);
        Assert.Equal((DependencyIssueKind.ReadsCounterpartyBeforeWriter, "Counterparty.Style"), (issue.Kind, issue.Tag));
        Assert.Equal([reader.Id, writer.Id], issue.BlockIds);
    }

    [Fact]
    public void AParameterizedReaderAuthoredBeforeItsProducer_SharesTheRunsSegmentAndKeepsTheEdge()
    {
        var workflow = new Workflow { Title = "Expanded segment" };
        Section notes = Parameterized(workflow, "Notes", [
            Variant(ANNUAL) with { ClassMap = [] },
            Variant(QUARTERLY) with { ClassMap = [] }]);
        Block reader = RagReader(notes, "reader", "web");
        Section later = new("Later", workflow) { Order = 1 };
        Block search = new(BlockTypes.IntelligentSearch, later)
        {
            ReplacementTag = "search",
            Properties = new IntelligentSearchProperties { SearchTermTemplate = "news", CreatesDocumentClass = "web" },
        };

        WorkflowDependencyMap map = WorkflowDependencyMap.ForTemplate(workflow);

        Assert.Contains(map.Edges, edge =>
            edge.SourceId == search.Id && edge.TargetId == reader.Id && edge.Kind == DependencyEdgeKind.Document);
        Assert.Empty(map.Issues);
    }

    [Fact]
    public void InputsAreClassified()
    {
        var workflow = new Workflow { Title = "Inputs" };
        _ = new WorkflowVariable(workflow, "Region");
        Section loop = new("Loop", workflow);
        _ = new Block(BlockTypes.LoopStart, loop)
        {
            ReplacementTag = "loop_start",
            Properties = new LoopStartProperties { ListTitle = "Funds", LoopedListTemplate = "A\nB" },
        };
        Block reader = AiQuery(loop, "reader",
            "{Region} {ReportDate} {Counterparty.Name} {Organization} {Funds.reportingStyle} {nowhere.answer}");

        WorkflowDependencyMap map = WorkflowDependencyMap.ForTemplate(workflow);

        Dictionary<string, DependencyInputKind> kinds = map.IncomingEdges(reader.Id)
            .Select(edge => map.FindInput(edge.SourceId))
            .OfType<DependencyInput>()
            .ToDictionary(input => input.Tag, input => input.Kind);
        Assert.Equal(DependencyInputKind.Variable, kinds["Region"]);
        Assert.Equal(DependencyInputKind.System, kinds["ReportDate"]);
        Assert.Equal(DependencyInputKind.System, kinds["Counterparty"]);
        Assert.Equal(DependencyInputKind.System, kinds["Organization"]);
        Assert.Equal(DependencyInputKind.Loop, kinds["Funds"]);
        Assert.Equal(DependencyInputKind.Unresolved, kinds["nowhere"]);
        DependencyIssue issue = Assert.Single(map.Issues);
        Assert.Equal(DependencyIssueKind.UnresolvedTag, issue.Kind);
        Assert.Equal("nowhere", issue.Tag);
        Assert.Equal([reader.Id], issue.BlockIds);
    }

    [Fact]
    public void GateEdges_AreShownButNeverScheduled()
    {
        var workflow = new Workflow { Title = "Gates" };
        Section first = new("First", workflow);
        Block check = AiQuery(first, "check", "Is there a quarterly report?");
        Section gated = new("Gated", workflow) { TextInputTemplate = "{check.stop}" };
        Block one = AiQuery(gated, "one", "One.");
        Block two = AiQuery(gated, "two", "Two.");

        WorkflowDependencyMap map = WorkflowDependencyMap.ForTemplate(workflow);
        WorkflowGraph graph = map.ToSchedulingGraph();

        Assert.Equal(
            [one.Id, two.Id],
            map.OutgoingEdges(check.Id).Where(edge => edge.Kind == DependencyEdgeKind.Gate).Select(edge => edge.TargetId));
        Assert.DoesNotContain(graph.Edges, edge => edge.SourceId == check.Id);
    }

    [Fact]
    public void VerificationGroup_IsAGroup_AndItsCoProducerAndFenceEdgesStayInTheScheduler()
    {
        var workflow = new Workflow { Title = "Verification" };
        Section section = new("Section", workflow);
        Block member = AiQuery(section, "revenue", "Extract revenue.");
        Block verifier = new(BlockTypes.Verification, section)
        {
            ReplacementTag = "verify",
            Properties = new VerificationProperties { Members = [new VerificationMember { MemberTag = "revenue" }] },
        };
        Block fence = new(BlockTypes.BuildTopicsForAllDocuments, section) { ReplacementTag = "topics" };
        Block reader = AiQuery(section, "reader", "Report {revenue.answer}");

        WorkflowDependencyMap map = WorkflowDependencyMap.ForTemplate(workflow);
        WorkflowGraph graph = map.ToSchedulingGraph(new HashSet<string> { fence.Id });

        DependencyVerificationGroup group = Assert.Single(map.VerificationGroups);
        Assert.Equal(verifier.Id, group.VerifierBlockId);
        Assert.Equal([member.Id], group.MemberBlockIds);
        Assert.DoesNotContain(map.Edges, edge => edge.SourceId == verifier.Id || edge.SourceId == fence.Id || edge.TargetId == fence.Id);
        Assert.Contains(graph.Edges, edge => edge.SourceId == verifier.Id && edge.TargetId == reader.Id);
        Assert.Contains(graph.Edges, edge => edge.SourceId == fence.Id && edge.TargetId == reader.Id);
    }

    [Fact]
    public void ANamedClassReader_GetsADocumentEdgeTaggedWithTheClass()
    {
        var workflow = new Workflow { Title = "Documents" };
        Section section = new("Section", workflow);
        Block producer = new(BlockTypes.IntelligentSearch, section)
        {
            ReplacementTag = "search",
            Properties = new IntelligentSearchProperties { SearchTermTemplate = "news", CreatesDocumentClass = "web" },
        };
        Block named = RagReader(section, "named", "web");

        WorkflowDependencyMap map = WorkflowDependencyMap.ForTemplate(workflow);

        DependencyEdge edge = Assert.Single(map.OutgoingEdges(producer.Id));
        Assert.Equal((named.Id, DependencyEdgeKind.Document), (edge.TargetId, edge.Kind));
        Assert.Equal(["web"], edge.Tags);
    }

    [Fact]
    public void ADocumentEdgeThatWouldCloseACycle_IsReportedButNotScheduled()
    {
        var workflow = new Workflow { Title = "Document cycle" };
        Section section = new("Section", workflow);
        Block reader = RagReader(section, "reader", "web");
        Block producer = new(BlockTypes.IntelligentSearch, section)
        {
            ReplacementTag = "search",
            Properties = new IntelligentSearchProperties { SearchTermTemplate = "{reader.answer}", CreatesDocumentClass = "web" },
        };

        WorkflowDependencyMap map = WorkflowDependencyMap.ForTemplate(workflow);
        WorkflowGraph graph = map.ToSchedulingGraph();
        SchedulingView view = SchedulingViewBuilder.Build(graph);

        Assert.Contains(map.Edges, edge => edge.SourceId == producer.Id && edge.Kind == DependencyEdgeKind.Document);
        DependencyIssue cycle = Assert.Single(map.Issues);
        Assert.Equal(DependencyIssueKind.Cycle, cycle.Kind);
        Assert.Equal([reader.Id, producer.Id], cycle.BlockIds);
        Assert.DoesNotContain(graph.Edges, edge => edge.SourceId == producer.Id && edge.TargetId == reader.Id);
        Assert.Empty(view.ForcedCycleNodes);
    }

    [Fact]
    public void AVariantClassMap_DecidesWhichVariantReadsTheProducedClass()
    {
        var workflow = new Workflow { Title = "Class map" };
        Section first = new("First", workflow);
        Block producer = new(BlockTypes.IntelligentSearch, first)
        {
            ReplacementTag = "search",
            Properties = new IntelligentSearchProperties { SearchTermTemplate = "news", CreatesDocumentClass = "Quarterly" },
        };
        Section notes = Parameterized(workflow, "Notes", [
            Variant(ANNUAL) with { ClassMap = new() { ["Annual"] = ["Annual"] } },
            Variant(QUARTERLY) with { ClassMap = new() { ["Annual"] = ["Quarterly"] } }]);
        Block reader = RagReader(notes, "notes", "Annual");

        WorkflowDependencyMap map = WorkflowDependencyMap.ForTemplate(workflow);

        DependencyEdge edge = Assert.Single(map.OutgoingEdges(producer.Id));
        Assert.Equal((reader.Id, DependencyEdgeKind.Document), (edge.TargetId, edge.Kind));
        Assert.Equal([QUARTERLY], edge.VariantKeys);
    }

    [Fact]
    public void ForTemplate_LeavesALegacyParameterizedConfigUntouched()
    {
        var workflow = new Workflow { Title = "Legacy" };
        Section notes = Parameterized(workflow, "Notes", [
            Variant(ANNUAL) with { SectionSupportedDocumentClasses = ["Annual"] },
            Variant(QUARTERLY) with { SectionSupportedDocumentClasses = ["Quarterly"] }]);
        string config = notes.ParameterizedConfig!;
        Block reader = AiQuery(notes, "notes", "Read the report.");
        reader.SupportedDocumentClasses = ["Something"];

        _ = WorkflowDependencyMap.ForTemplate(workflow);

        Assert.Equal(config, notes.ParameterizedConfig);
        Assert.Equal(["Something"], reader.SupportedDocumentClasses);
    }

    [Fact]
    public void ABareOutsideReadOfAParameterizedTemplateTag_IsUnresolved()
    {
        var workflow = new Workflow { Title = "Bare" };
        Section notes = Parameterized(workflow, "Notes", [Variant(ANNUAL), Variant(QUARTERLY)]);
        Block noteBlock = AiQuery(notes, "notes", "Find the notes.");
        Section summary = new("Summary", workflow);
        Block reader = AiQuery(summary, "reader", "All notes: {notes.answer}");

        WorkflowDependencyMap map = WorkflowDependencyMap.ForTemplate(workflow);

        Assert.Empty(map.OutgoingEdges(noteBlock.Id));
        DependencyIssue issue = Assert.Single(map.Issues);
        Assert.Equal((DependencyIssueKind.UnresolvedTag, "notes"), (issue.Kind, issue.Tag));
        Assert.Equal([reader.Id], issue.BlockIds);
    }

    [Fact]
    public void AParameterizedSectionIsGatedByEachVariantsStopGateAsWritten()
    {
        var workflow = new Workflow { Title = "Variant gates" };
        Section first = new("First", workflow);
        Block check = AiQuery(first, "check", "Is there a report?");
        Block templateGate = AiQuery(first, "template_gate", "Never read.");
        Block periodGate = AiQuery(first, "gate_QR", "Never read either.");
        Section notes = Parameterized(workflow, "Notes", [
            Variant(QUARTERLY, (PERIOD_PARAM, QUARTERLY)) with { SectionStopGate = "{check.stop} {gate_{PERIOD_ID}}" }]);
        notes.TextInputTemplate = "{template_gate.stop}";
        Block gated = AiQuery(notes, "notes", "Find the notes.");

        WorkflowDependencyMap map = WorkflowDependencyMap.ForTemplate(workflow);

        List<DependencyEdge> gates = [.. map.IncomingEdges(gated.Id).Where(edge => edge.Kind == DependencyEdgeKind.Gate)];
        DependencyEdge gate = Assert.Single(gates, edge => edge.SourceId == check.Id);
        Assert.Equal([QUARTERLY], gate.VariantKeys);
        Assert.DoesNotContain(gates, edge => edge.SourceId == templateGate.Id || edge.SourceId == periodGate.Id);
    }

    [Fact]
    public void AReservedTag_IsASystemInputEvenWhenABlockCarriesIt()
    {
        var workflow = new Workflow { Title = "Reserved" };
        Section section = new("Section", workflow);
        Block reportDate = AiQuery(section, "ReportDate", "Produces a value tagged ReportDate.");
        Block reader = AiQuery(section, "reader", "As of {ReportDate}");

        WorkflowDependencyMap map = WorkflowDependencyMap.ForTemplate(workflow);

        DependencyEdge edge = Assert.Single(map.IncomingEdges(reader.Id));
        Assert.Equal(DependencyInputKind.System, map.FindInput(edge.SourceId)?.Kind);
        Assert.Empty(map.OutgoingEdges(reportDate.Id));
    }

    [Fact]
    public void AMonitoringOnlyTag_ResolvesToTheBlockThatSuppliesIt()
    {
        var workflow = new Workflow { Title = "Monitoring" };
        Section section = new("Section", workflow);
        Block manager = AiQuery(section, "base_info_fund_manager", "Name the manager.");
        Block reader = AiQuery(section, "reader", "Manager: {base_info_fund_manager} Funds: {FundsListFromDb}");

        WorkflowDependencyMap map = WorkflowDependencyMap.ForTemplate(workflow);

        List<DependencyEdge> incoming = [.. map.IncomingEdges(reader.Id)];
        Assert.Contains(incoming, edge => edge.SourceId == manager.Id);
        DependencyEdge funds = Assert.Single(incoming, edge => edge.SourceId != manager.Id);
        Assert.Equal(DependencyInputKind.System, map.FindInput(funds.SourceId)?.Kind);
        Assert.Empty(map.Issues);
    }

    [Fact]
    public void ACounterpartyReaderThatFeedsTheWriter_IsNeitherAnEdgeNorACycle()
    {
        var workflow = new Workflow { Title = "Put back" };
        Section section = new("Section", workflow);
        Block reader = AiQuery(section, "last", "Last time: {Counterparty.LastFinancialAnalysisResult}");
        Block writer = AiQuery(section, "writer", "Put back {last}.");
        writer.ShouldUpdateCounterpartyWithResult = true;

        WorkflowDependencyMap map = WorkflowDependencyMap.ForTemplate(workflow);

        Assert.DoesNotContain(map.Edges, edge => edge.Kind == DependencyEdgeKind.Counterparty);
        Assert.Contains(map.Edges, edge => edge.SourceId == reader.Id && edge.TargetId == writer.Id);
        Assert.Empty(map.Issues);
    }

    [Fact]
    public void AnAgentSkillFileFlaggedToFillTags_DrawsAnEdgeFromTheBlockItReads_AndAnUnflaggedFileDoesNot()
    {
        var workflow = new Workflow { Title = "Skill files" };
        Section section = new("Main", workflow);
        Block flaggedSource = AiQuery(section, "fund_terms", "Find the fund terms.");
        Block unflaggedSource = AiQuery(section, "fund_name", "Find the fund name.");
        var agent = new Block(BlockTypes.Agent, section, "analyst");
        ((AgentProperties)agent.Properties).WorkerSkills =
        [
            new AgentWorkerSkill
            {
                Name = "verify",
                Description = "Verifies the fund.",
                Content = "Run the staged scripts.",
                Assets =
                [
                    new AgentWorkerSkillAsset
                    {
                        Name = "filled.py",
                        Content = "TERMS = '''{{fund_terms.answer}}'''",
                        FillTags = true,
                    },
                    new AgentWorkerSkillAsset { Name = "verbatim.py", Content = "print('{{fund_name}}')" },
                ],
            },
        ];

        WorkflowDependencyMap map = WorkflowDependencyMap.ForTemplate(workflow);

        DependencyEdge edge = Assert.Single(map.IncomingEdges(agent.Id));
        Assert.Equal(flaggedSource.Id, edge.SourceId);
        Assert.Equal(["fund_terms.answer"], edge.Tags);
        Assert.DoesNotContain(map.Edges, candidate => candidate.SourceId == unflaggedSource.Id);
    }

    internal static Block RagReader(Section section, string tag, string documentClass)
    {
        Block reader = AiQuery(section, tag, "Read the documents.");
        reader.SupportedDocumentClasses = [documentClass];
        _ = new RagSettings(reader) { Type = RagTypes.WholeDocument };

        return reader;
    }

    internal static Block AiQuery(Section section, string tag, string prompt)
    {
        return new Block(BlockTypes.AiQuery, section)
        {
            ReplacementTag = tag,
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };
    }

    internal static Section Parameterized(Workflow workflow, string title, List<SectionVariant> variants)
    {
        return new Section(title, workflow)
        {
            ParameterizedConfig = JsonConvert.SerializeObject(new ParameterizedSectionConfig(variants)),
        };
    }

    internal static SectionVariant Variant(string key, params (string Name, string Value)[] parameters)
    {
        return new SectionVariant(
            key,
            key,
            parameters.Length == 0 ? null : parameters.ToDictionary(static p => p.Name, static p => p.Value));
    }
}
