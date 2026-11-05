using ErrorOr;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Queries.SharedComponents.ScanSharedComponentBootstrap;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Properties;
using IniaAi.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Queries.SharedComponents;

public class ScanSharedComponentBootstrapQueryHandlerTests : IDisposable
{
    private const string ORG = "org-test";
    private const string USER = "user-test";

    private readonly SharedComponentsTestHarness _harness = new();
    private readonly ScanSharedComponentBootstrapQueryHandler _handler;
    private readonly SaveWorkflowCommandHandler _saveHandler;

    public ScanSharedComponentBootstrapQueryHandlerTests()
    {
        _handler = new ScanSharedComponentBootstrapQueryHandler(
            _harness.UnitOfWork,
            _harness.Normalizer,
            NullLogger<ScanSharedComponentBootstrapQueryHandler>.Instance);
        (_, _saveHandler) = _harness.CreateRealSaveSender();
    }

    [Fact]
    public async Task Scan_HandCopiedSectionAcrossTwoTemplates_GroupsSectionAndInnerBlockCrossType()
    {
        await SeedTemplateAsync("WT-A", workflow => AddPlainSection(workflow, "Risk", ("rate", "Extract the rate")));
        await SeedTemplateAsync("WT-B", workflow => AddPlainSection(workflow, "Risk", ("rate", "Extract the rate")));

        ErrorOr<SharedComponentBootstrapScanResult> result = await Scan();

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.ScannedWorkflowTypeCount);
        Assert.Equal(2, result.Value.Suggestions.Count);
        SharedComponentBootstrapSuggestion sectionGroup = Assert.Single(
            result.Value.Suggestions, s => s.Kind == SharedComponentKind.Section);
        Assert.Equal("Risk", sectionGroup.SuggestedName);
        Assert.True(sectionGroup.CrossType);
        Assert.Null(sectionGroup.ExistingComponentId);
        Assert.Equal(["WT-A", "WT-B"], sectionGroup.Nodes.Select(n => n.WorkflowTypeId));
        Assert.All(sectionGroup.Nodes, node => Assert.NotNull(node.TargetStableId));
        SharedComponentBootstrapSuggestion blockGroup = Assert.Single(
            result.Value.Suggestions, s => s.Kind == SharedComponentKind.Block);
        Assert.Equal(2, blockGroup.Nodes.Count);
    }

    [Fact]
    public async Task Scan_ActivelyLinkedSection_IsNotACandidate()
    {
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Risk Pack", id: "c-sec");
        await SeedTemplateAsync("WT-A", workflow =>
        {
            Section linked = AddPlainSection(workflow, "Risk", ("rate", "Extract the rate"));
            linked.SharedComponentId = "c-sec";
            linked.SharedComponentLinkState = SharedComponentLinkState.Linked;
            linked.SharedComponentVersionHash = new string('a', 64);
        });
        await SeedTemplateAsync("WT-B", workflow => AddPlainSection(workflow, "Risk", ("rate", "Extract the rate")));

        ErrorOr<SharedComponentBootstrapScanResult> result = await Scan();

        Assert.False(result.IsError);
        Assert.Empty(result.Value.Suggestions);
    }

    [Fact]
    public async Task Scan_BrokenOutSection_IsACandidateWithProvenance()
    {
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Risk Pack", id: "c-sec");
        await SeedTemplateAsync("WT-A", workflow =>
        {
            Section fork = AddPlainSection(workflow, "Risk", ("rate", "Extract the rate"));
            fork.SharedComponentId = "c-sec";
            fork.SharedComponentLinkState = SharedComponentLinkState.BrokenOut;
            fork.SharedComponentVersionHash = new string('a', 64);
        });
        await SeedTemplateAsync("WT-B", workflow => AddPlainSection(workflow, "Risk", ("rate", "Extract the rate")));

        ErrorOr<SharedComponentBootstrapScanResult> result = await Scan();

        Assert.False(result.IsError);
        SharedComponentBootstrapSuggestion sectionGroup = Assert.Single(
            result.Value.Suggestions, s => s.Kind == SharedComponentKind.Section);
        Assert.Equal(2, sectionGroup.Nodes.Count);
        SharedComponentBootstrapNode forked = Assert.Single(sectionGroup.Nodes, n => n.WorkflowTypeId == "WT-A");
        Assert.Equal("c-sec", forked.ProvenanceComponentId);
        Assert.Equal("Risk Pack", forked.ProvenanceComponentName);
        Assert.Null(Assert.Single(sectionGroup.Nodes, n => n.WorkflowTypeId == "WT-B").ProvenanceComponentId);
    }

    [Fact]
    public async Task Scan_BlockInsideActivelyLinkedSection_IsNotACandidate()
    {
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Risk Pack", id: "c-sec");
        await SeedTemplateAsync("WT-A", workflow =>
        {
            Section linked = AddPlainSection(workflow, "Risk", ("rate", "Extract the rate"));
            linked.SharedComponentId = "c-sec";
            linked.SharedComponentLinkState = SharedComponentLinkState.Linked;
            linked.SharedComponentVersionHash = new string('a', 64);
        });
        await SeedTemplateAsync("WT-B", workflow => AddPlainSection(workflow, "Other", ("rate", "Extract the rate")));

        ErrorOr<SharedComponentBootstrapScanResult> result = await Scan();

        Assert.False(result.IsError);
        Assert.Empty(result.Value.Suggestions);
    }

    [Fact]
    public async Task Scan_GroupHashMatchingActiveComponentHead_OffersAttachTarget()
    {
        await SeedTemplateAsync("WT-A", workflow => AddPlainSection(workflow, "Body", ("kyc", "Run the check")));
        await SeedTemplateAsync("WT-B", workflow => AddPlainSection(workflow, "Body2", ("kyc", "Run the check")));
        Workflow scratch = new("Scratch");
        Section scratchSection = AddPlainSection(scratch, "Scratch", ("kyc", "Run the check"));
        (string blockJson, string blockHash) = _harness.Definition(scratchSection.Blocks.Single());
        await _harness.AddComponentAsync(SharedComponentKind.Block, "KYC Check", id: "c-kyc");
        _ = await _harness.AddVersionAsync("c-kyc", 1, blockHash, blockJson);
        await _harness.AddComponentAsync(
            SharedComponentKind.Block, "Retired Twin", state: SharedComponentState.Retired, id: "c-old");
        _ = await _harness.AddVersionAsync("c-old", 1, blockHash, blockJson);

        ErrorOr<SharedComponentBootstrapScanResult> result = await Scan();

        Assert.False(result.IsError);
        SharedComponentBootstrapSuggestion blockGroup = Assert.Single(
            result.Value.Suggestions, s => s.Kind == SharedComponentKind.Block);
        Assert.Equal("c-kyc", blockGroup.ExistingComponentId);
        Assert.Equal("KYC Check", blockGroup.ExistingComponentName);
    }

    [Fact]
    public async Task Scan_GroupMatchingAHeadStoredBeforeAFieldWasDropped_OffersAttachTarget()
    {
        await SeedTemplateAsync("WT-A", workflow => AddPlainSection(workflow, "Body", ("kyc", "Run the check")));
        await SeedTemplateAsync("WT-B", workflow => AddPlainSection(workflow, "Body2", ("kyc", "Run the check")));
        Workflow scratch = new("Scratch");
        Section scratchSection = AddPlainSection(scratch, "Scratch", ("kyc", "Run the check"));
        (string blockJson, string blockHash) =
            _harness.StoredBeforeAFieldWasDropped(_harness.Definition(scratchSection.Blocks.Single()).DefinitionJson);
        await _harness.AddComponentAsync(SharedComponentKind.Block, "KYC Check", id: "c-kyc");
        _ = await _harness.AddVersionAsync("c-kyc", 1, blockHash, blockJson);

        ErrorOr<SharedComponentBootstrapScanResult> result = await Scan();

        Assert.False(result.IsError);
        SharedComponentBootstrapSuggestion blockGroup = Assert.Single(
            result.Value.Suggestions, s => s.Kind == SharedComponentKind.Block);
        Assert.Equal("c-kyc", blockGroup.ExistingComponentId);
    }

    [Fact]
    public async Task Scan_SingleStrayMatchingActiveComponentHead_OffersAttachSuggestion()
    {
        await SeedTemplateAsync("WT-A", workflow => AddPlainSection(workflow, "Body", ("kyc", "Run the check")));
        Workflow scratch = new("Scratch");
        Section scratchSection = AddPlainSection(scratch, "Scratch", ("kyc", "Run the check"));
        (string blockJson, string blockHash) = _harness.Definition(scratchSection.Blocks.Single());
        await _harness.AddComponentAsync(SharedComponentKind.Block, "KYC Check", id: "c-kyc");
        _ = await _harness.AddVersionAsync("c-kyc", 1, blockHash, blockJson);

        ErrorOr<SharedComponentBootstrapScanResult> result = await Scan();

        Assert.False(result.IsError);
        SharedComponentBootstrapSuggestion blockGroup = Assert.Single(
            result.Value.Suggestions, s => s.Kind == SharedComponentKind.Block);
        Assert.Equal("c-kyc", blockGroup.ExistingComponentId);
        SharedComponentBootstrapNode stray = Assert.Single(blockGroup.Nodes);
        Assert.Equal("WT-A", stray.WorkflowTypeId);
        Assert.DoesNotContain(result.Value.Suggestions, s => s.Kind == SharedComponentKind.Section);
    }

    [Fact]
    public async Task Scan_RepeatsWithinOneTemplate_GroupWithoutCrossTypeFlag()
    {
        await SeedTemplateAsync("WT-A", workflow =>
        {
            _ = AddPlainSection(workflow, "First", ("note", "Same prompt"));
            _ = AddPlainSection(workflow, "Second", ("note", "Same prompt"));
        });

        ErrorOr<SharedComponentBootstrapScanResult> result = await Scan();

        Assert.False(result.IsError);
        SharedComponentBootstrapSuggestion blockGroup = Assert.Single(
            result.Value.Suggestions, s => s.Kind == SharedComponentKind.Block);
        Assert.False(blockGroup.CrossType);
        Assert.Equal(2, blockGroup.Nodes.Count);
        Assert.All(blockGroup.Nodes, node => Assert.Equal("WT-A", node.WorkflowTypeId));
    }

    [Fact]
    public Task Scan_SeparatorSections_AreNotCandidatesAndStillConsumeAPathIndex()
    {
        return AssertSkippedSectionIsNotSuggested(
            workflow => AddSeparator(workflow, "Phase two"), "Phase two");
    }

    [Fact]
    public async Task Scan_NodesDifferingOnlyInLabels_StillGroup()
    {
        await SeedTemplateAsync("WT-A", workflow => AddPlainSection(workflow, "Risk", ("rate", "Extract the rate")));
        await SeedTemplateAsync("WT-B", workflow =>
        {
            Section section = AddPlainSection(workflow, "Risk Assessment", ("rate", "Extract the rate"));
            section.GroupColor = GroupColor.Color1;
            Block block = section.Blocks.Single();
            block.Name = "A completely different block name";
            block.OutputTitleTemplate = "A completely different output title";
        });

        ErrorOr<SharedComponentBootstrapScanResult> result = await Scan();

        Assert.False(result.IsError);
        SharedComponentBootstrapSuggestion sectionGroup = Assert.Single(
            result.Value.Suggestions, s => s.Kind == SharedComponentKind.Section);
        Assert.Equal(["WT-A", "WT-B"], sectionGroup.Nodes.Select(n => n.WorkflowTypeId));
        SharedComponentBootstrapSuggestion blockGroup = Assert.Single(
            result.Value.Suggestions, s => s.Kind == SharedComponentKind.Block);
        Assert.Equal(2, blockGroup.Nodes.Count);
    }

    [Fact]
    public Task Scan_EmptySections_AreNotCandidatesAndStillConsumeAPathIndex()
    {
        return AssertSkippedSectionIsNotSuggested(
            workflow => new Section("Placeholder", workflow) { Title = "Placeholder" }, "Placeholder");
    }

    private async Task AssertSkippedSectionIsNotSuggested(
        Func<Workflow, Section> addSkippableSection,
        string skippedTitle)
    {
        foreach (string workflowTypeId in (string[])["WT-A", "WT-B"])
        {
            await SeedTemplateAsync(workflowTypeId, workflow =>
            {
                _ = addSkippableSection(workflow);
                _ = AddPlainSection(workflow, "Risk", ("rate", "Extract the rate"));
            });
        }

        ErrorOr<SharedComponentBootstrapScanResult> result = await Scan();

        Assert.False(result.IsError);
        Assert.DoesNotContain(result.Value.Suggestions, s => s.SuggestedName == skippedTitle);
        SharedComponentBootstrapSuggestion sectionGroup = Assert.Single(
            result.Value.Suggestions, s => s.Kind == SharedComponentKind.Section);
        Assert.Equal("Risk", sectionGroup.SuggestedName);
        Assert.All(sectionGroup.Nodes, node => Assert.Equal("Sections[1]", node.TargetPath));
    }

    [Fact]
    public async Task Scan_LoopBoundaryBlocks_AreNotCandidates()
    {
        await SeedTemplateAsync("WT-A", workflow => AddLoopSection(workflow, "Risk"));
        await SeedTemplateAsync("WT-B", workflow => AddLoopSection(workflow, "Risk"));

        ErrorOr<SharedComponentBootstrapScanResult> result = await Scan();

        Assert.False(result.IsError);
        Assert.DoesNotContain(
            result.Value.Suggestions,
            s => s.Kind == SharedComponentKind.Block && s.Nodes.Count > 0);
    }

    [Fact]
    public async Task Scan_ReservedWorkflowTypes_AreNeitherScannedNorNamedInNodes()
    {
        await SeedTemplateAsync("WT-A", workflow => AddPlainSection(workflow, "Risk", ("rate", "Extract the rate")));
        await SeedTemplateAsync(
            WorkflowType.TEST_WORKFLOW_TYPE,
            workflow => AddPlainSection(workflow, "Risk", ("rate", "Extract the rate")));
        await SeedTemplateAsync(
            WorkflowType.GENERIC,
            workflow => AddPlainSection(workflow, "Risk", ("rate", "Extract the rate")));

        ErrorOr<SharedComponentBootstrapScanResult> result = await Scan();

        Assert.False(result.IsError);
        Assert.Equal(1, result.Value.ScannedWorkflowTypeCount);
        Assert.DoesNotContain(
            result.Value.Suggestions.SelectMany(suggestion => suggestion.Nodes),
            node => node.WorkflowTypeId is WorkflowType.TEST_WORKFLOW_TYPE or WorkflowType.GENERIC);
    }

    private Task<ErrorOr<SharedComponentBootstrapScanResult>> Scan()
    {
        return _handler.Handle(
            new ScanSharedComponentBootstrapQuery(ORG, USER),
            TestContext.Current.CancellationToken);
    }

    private async Task SeedTemplateAsync(string workflowTypeId, Action<Workflow> compose)
    {
        var workflow = new Workflow(workflowTypeId) { Title = workflowTypeId };
        compose(workflow);
        _ = await _harness.SaveTemplateAsync(_saveHandler, ORG, USER, workflowTypeId, workflow);
    }

    private static Section AddSeparator(Workflow workflow, string title)
    {
        return new Section(title, workflow)
        {
            Title = title,
            Properties = new SectionProperties { IsSeparator = true },
        };
    }

    private static Section AddLoopSection(Workflow workflow, string title)
    {
        var section = new Section(title, workflow) { Title = title };
        _ = new Block(BlockTypes.LoopStart, section, "loopStart") { Name = "Loop Start" };
        _ = new Block(BlockTypes.LoopEnd, section, "loopEnd") { Name = "Loop End" };

        return section;
    }

    private static Section AddPlainSection(Workflow workflow, string title, params (string Tag, string Prompt)[] blocks)
    {
        var section = new Section(title, workflow) { Title = title };
        foreach ((string tag, string prompt) in blocks)
        {
            _ = new Block(BlockTypes.AiQuery, section, tag)
            {
                Name = "Node",
                Properties = new AiQueryProperties { UserPromptTemplate = prompt },
            };
        }

        return section;
    }

    public void Dispose()
    {
        _harness.Dispose();
    }
}
