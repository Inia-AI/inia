using ErrorOr;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.SharedComponents.GetSharedComponentVersionDiff;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;

namespace IniaAi.Application.UnitTests.Queries.SharedComponents;

public class GetSharedComponentVersionDiffQueryHandlerTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";

    private readonly WorkflowCanonicalizer _canonicalizer = new();
    private readonly SharedComponentContentNormalizer _normalizer;

    public GetSharedComponentVersionDiffQueryHandlerTests()
    {
        _normalizer = new SharedComponentContentNormalizer(_canonicalizer);
    }

    [Fact]
    public async Task Diff_SectionKind_PromptChange_ReportsMatchedSlotModified()
    {
        using var harness = new SharedComponentsTestHarness();
        (string defV1, string hashV1) = Definition(DefSection("Market Data", ("rate", "Extract")));
        (string defV2, string hashV2) = Definition(DefSection("Market Data", ("rate", "Extract v2")));
        SharedComponent component = await harness.AddComponentAsync(SharedComponentKind.Section, "Market Data");
        SharedComponentVersion v1 = await harness.AddVersionAsync(component.Id, 1, hashV1, defV1);
        SharedComponentVersion v2 = await harness.AddVersionAsync(
            component.Id, 2, hashV2, defV2, parentVersionId: v1.Id);

        GetSharedComponentVersionDiffQueryHandler handler = CreateHandler(harness);
        ErrorOr<SharedComponentVersionDiffResult> result = await handler.Handle(
            new GetSharedComponentVersionDiffQuery(ORG, USER, component.Id, v1.Id, v2.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(component.Id, result.Value.SharedComponentId);
        Assert.Equal(v1.Id, result.Value.FromVersionId);
        Assert.Equal(v2.Id, result.Value.ToVersionId);
        BlockDiff block = Assert.Single(result.Value.Diff.Blocks);
        Assert.Equal("rate", block.ReplacementTag);
        Assert.Equal(DiffChangeKind.Modified, block.ChangeKind);
        FieldDiff field = Assert.Single(block.FieldDiffs);
        Assert.Contains("UserPromptTemplate", field.FieldName);
        Assert.Equal("Extract", field.Before);
        Assert.Equal("Extract v2", field.After);
        Assert.Empty(result.Value.Diff.Sections);
    }

    [Fact]
    public async Task Diff_SectionKind_SlotSetChange_ReportsAddedAndRemoved()
    {
        using var harness = new SharedComponentsTestHarness();
        (string defV1, string hashV1) = Definition(
            DefSection("Market Data", ("rate", "Extract"), ("legacy", "Old")));
        (string defV2, string hashV2) = Definition(
            DefSection("Market Data", ("rate", "Extract"), ("fresh", "New")));
        SharedComponent component = await harness.AddComponentAsync(SharedComponentKind.Section, "Market Data");
        SharedComponentVersion v1 = await harness.AddVersionAsync(component.Id, 1, hashV1, defV1);
        SharedComponentVersion v2 = await harness.AddVersionAsync(
            component.Id, 2, hashV2, defV2, parentVersionId: v1.Id);

        GetSharedComponentVersionDiffQueryHandler handler = CreateHandler(harness);
        ErrorOr<SharedComponentVersionDiffResult> result = await handler.Handle(
            new GetSharedComponentVersionDiffQuery(ORG, USER, component.Id, v1.Id, v2.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Diff.Blocks.Count);
        Assert.Contains(result.Value.Diff.Blocks, b =>
            b.ReplacementTag == "legacy" && b.ChangeKind == DiffChangeKind.Removed);
        Assert.Contains(result.Value.Diff.Blocks, b =>
            b.ReplacementTag == "fresh" && b.ChangeKind == DiffChangeKind.Added);
        Assert.DoesNotContain(result.Value.Diff.Blocks, b => b.ReplacementTag == "rate");
    }

    [Fact]
    public async Task Diff_BlockKind_WrapsDefinitionAsNestedBlock()
    {
        using var harness = new SharedComponentsTestHarness();
        (string defV1, string hashV1) = Definition(BuildBlockDefinition("Extract"));
        (string defV2, string hashV2) = Definition(BuildBlockDefinition("Extract v2"));
        SharedComponent component = await harness.AddComponentAsync(SharedComponentKind.Block, "FX Rates");
        SharedComponentVersion v1 = await harness.AddVersionAsync(component.Id, 1, hashV1, defV1);
        SharedComponentVersion v2 = await harness.AddVersionAsync(
            component.Id, 2, hashV2, defV2, parentVersionId: v1.Id);

        GetSharedComponentVersionDiffQueryHandler handler = CreateHandler(harness);
        ErrorOr<SharedComponentVersionDiffResult> result = await handler.Handle(
            new GetSharedComponentVersionDiffQuery(ORG, USER, component.Id, v1.Id, v2.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        BlockDiff block = Assert.Single(result.Value.Diff.Blocks);
        Assert.Equal(DiffChangeKind.Modified, block.ChangeKind);
        Assert.Equal("Node", block.DisplayName);
        FieldDiff field = Assert.Single(block.FieldDiffs);
        Assert.Contains("UserPromptTemplate", field.FieldName);
    }

    [Fact]
    public async Task Diff_UnknownComponent_ReturnsNotFound()
    {
        using var harness = new SharedComponentsTestHarness();

        GetSharedComponentVersionDiffQueryHandler handler = CreateHandler(harness);
        ErrorOr<SharedComponentVersionDiffResult> result = await handler.Handle(
            new GetSharedComponentVersionDiffQuery(ORG, USER, "missing", "v1", "v2"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.UNKNOWN_COMPONENT, result.FirstError.Code);
    }

    [Fact]
    public async Task Diff_VersionOfAnotherComponent_ReturnsUnknownVersion()
    {
        using var harness = new SharedComponentsTestHarness();
        (string defJson, string hash) = Definition(DefSection("Market Data", ("rate", "Extract")));
        SharedComponent component = await harness.AddComponentAsync(SharedComponentKind.Section, "Market Data");
        SharedComponent other = await harness.AddComponentAsync(SharedComponentKind.Section, "Other");
        SharedComponentVersion ours = await harness.AddVersionAsync(component.Id, 1, hash, defJson);
        SharedComponentVersion foreign = await harness.AddVersionAsync(other.Id, 1, hash, defJson);

        GetSharedComponentVersionDiffQueryHandler handler = CreateHandler(harness);
        ErrorOr<SharedComponentVersionDiffResult> result = await handler.Handle(
            new GetSharedComponentVersionDiffQuery(ORG, USER, component.Id, ours.Id, foreign.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.UNKNOWN_VERSION, result.FirstError.Code);
        Assert.Contains(foreign.Id, result.FirstError.Description);
    }

    [Fact]
    public async Task Diff_PayloadAbsentSide_ReturnsPayloadAbsent()
    {
        using var harness = new SharedComponentsTestHarness();
        (string defJson, string hash) = Definition(DefSection("Market Data", ("rate", "Extract")));
        SharedComponent component = await harness.AddComponentAsync(SharedComponentKind.Section, "Market Data");
        SharedComponentVersion absent = await harness.AddVersionAsync(
            component.Id, 1, new string('a', 64), contentJson: null, payloadAbsentAt: DateTime.UtcNow);
        SharedComponentVersion head = await harness.AddVersionAsync(
            component.Id, 2, hash, defJson, parentVersionId: absent.Id);

        GetSharedComponentVersionDiffQueryHandler handler = CreateHandler(harness);
        ErrorOr<SharedComponentVersionDiffResult> result = await handler.Handle(
            new GetSharedComponentVersionDiffQuery(ORG, USER, component.Id, absent.Id, head.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.PAYLOAD_ABSENT, result.FirstError.Code);
        Assert.Contains(absent.Id, result.FirstError.Description);
    }

    [Fact]
    public async Task Diff_CorruptStoredPayload_ReturnsCorruptDefinition()
    {
        using var harness = new SharedComponentsTestHarness();
        (string defJson, string hash) = Definition(DefSection("Market Data", ("rate", "Extract")));
        SharedComponent component = await harness.AddComponentAsync(SharedComponentKind.Section, "Market Data");
        SharedComponentVersion corrupt = await harness.AddVersionAsync(
            component.Id, 1, new string('a', 64), "{ not json");
        SharedComponentVersion head = await harness.AddVersionAsync(
            component.Id, 2, hash, defJson, parentVersionId: corrupt.Id);

        GetSharedComponentVersionDiffQueryHandler handler = CreateHandler(harness);
        ErrorOr<SharedComponentVersionDiffResult> result = await handler.Handle(
            new GetSharedComponentVersionDiffQuery(ORG, USER, component.Id, corrupt.Id, head.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Unexpected, result.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.CORRUPT_DEFINITION, result.FirstError.Code);
    }

    private static GetSharedComponentVersionDiffQueryHandler CreateHandler(SharedComponentsTestHarness harness)
    {
        return new GetSharedComponentVersionDiffQueryHandler(
            harness.UnitOfWork,
            new SharedComponentDefinitionDiffer(new WorkflowDiffService(), new WorkflowNarrativeGenerator()));
    }

    private (string DefinitionJson, string ContentHash) Definition(Section section)
    {
        string json = _normalizer.ToDefinitionJson(section);

        return (json, _canonicalizer.ComputeContentHash(json));
    }

    private (string DefinitionJson, string ContentHash) Definition(Block block)
    {
        string json = _normalizer.ToDefinitionJson(block);

        return (json, _canonicalizer.ComputeContentHash(json));
    }

    private static Section DefSection(string title, params (string Tag, string Prompt)[] blocks)
    {
        var section = new Section(title, new Workflow("Scratch")) { Title = title };
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

    private static Block BuildBlockDefinition(string prompt)
    {
        var scratch = new Workflow("Scratch");
        var section = new Section("Scratch", scratch) { Title = "Scratch" };

        return new Block(BlockTypes.AiQuery, section, "definition")
        {
            Name = "Node",
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };
    }
}
