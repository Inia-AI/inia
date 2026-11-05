using ErrorOr;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.SharedComponents.GetSharedComponentVersions;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;
using IniaAi.WebApi.Contracts.Responses.SharedComponents;
using NSubstitute;

namespace IniaAi.Application.UnitTests.Queries.SharedComponents;

public class GetSharedComponentVersionsQueryHandlerTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";

    [Fact]
    public async Task Versions_OrderedNewestFirst_WithPayloadAbsentFlagAndLineage()
    {
        using var harness = new SharedComponentsTestHarness();
        SharedComponent component = await harness.AddComponentAsync(SharedComponentKind.Section, "Alpha");
        SharedComponentVersion first = await harness.AddVersionAsync(component.Id, 1, new string('a', 64), """{"v":1}""");
        SharedComponentVersion second = await harness.AddVersionAsync(
            component.Id, 2, new string('b', 64), contentJson: null,
            payloadAbsentAt: DateTime.UtcNow, parentVersionId: first.Id);
        SharedComponentVersion third = await harness.AddVersionAsync(
            component.Id, 3, new string('c', 64), """{"v":3}""", parentVersionId: second.Id);

        GetSharedComponentVersionsQueryHandler handler = CreateHandler(harness);
        ErrorOr<List<SharedComponentVersionDto>> result = await handler.Handle(
            new GetSharedComponentVersionsQuery(ORG, USER, component.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(3, result.Value.Count);
        Assert.Equal(third.Id, result.Value[0].VersionId);
        Assert.Equal(second.Id, result.Value[0].ParentVersionId);
        Assert.False(result.Value[0].PayloadAbsent);
        Assert.Equal(second.Id, result.Value[1].VersionId);
        Assert.True(result.Value[1].PayloadAbsent);
        Assert.Equal(first.Id, result.Value[2].VersionId);
        Assert.Null(result.Value[2].ParentVersionId);
    }

    [Fact]
    public async Task Versions_UnknownId_ReturnsNotFound()
    {
        using var harness = new SharedComponentsTestHarness();

        GetSharedComponentVersionsQueryHandler handler = CreateHandler(harness);
        ErrorOr<List<SharedComponentVersionDto>> result = await handler.Handle(
            new GetSharedComponentVersionsQuery(ORG, USER, "missing"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.UNKNOWN_COMPONENT, result.FirstError.Code);
    }

    [Fact]
    public async Task Versions_AuthorNameResolved_WhenResolverKnowsTheUser()
    {
        using var harness = new SharedComponentsTestHarness();
        SharedComponent component = await harness.AddComponentAsync(SharedComponentKind.Block, "Alpha");
        _ = await harness.AddVersionAsync(component.Id, 1, new string('a', 64), """{"v":1}""");

        GetSharedComponentVersionsQueryHandler handler = CreateHandler(
            harness,
            new Dictionary<string, string> { ["user-test"] = "Jane Doe" });
        ErrorOr<List<SharedComponentVersionDto>> result = await handler.Handle(
            new GetSharedComponentVersionsQuery(ORG, USER, component.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentVersionDto only = Assert.Single(result.Value);
        Assert.Equal("user-test", only.CreatedByUserId);
        Assert.Equal("Jane Doe", only.CreatedByName);
    }

    [Fact]
    public async Task Versions_AuthorNameFallsBackToRawId_WhenUnresolved()
    {
        using var harness = new SharedComponentsTestHarness();
        SharedComponent component = await harness.AddComponentAsync(SharedComponentKind.Block, "Alpha");
        _ = await harness.AddVersionAsync(component.Id, 1, new string('a', 64), """{"v":1}""");

        GetSharedComponentVersionsQueryHandler handler = CreateHandler(harness);
        ErrorOr<List<SharedComponentVersionDto>> result = await handler.Handle(
            new GetSharedComponentVersionsQuery(ORG, USER, component.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentVersionDto only = Assert.Single(result.Value);
        Assert.Equal("user-test", only.CreatedByName);
    }

    [Fact]
    public async Task Versions_DefaultRequest_LeavesNarrativesNull()
    {
        using var harness = new SharedComponentsTestHarness();
        SharedComponent component = await harness.AddComponentAsync(SharedComponentKind.Section, "Alpha");
        (string defV1, string hashV1) = Definition(DefSection("Alpha", ("rate", "Extract")));
        (string defV2, string hashV2) = Definition(DefSection("Alpha", ("rate", "Extract v2")));
        SharedComponentVersion first = await harness.AddVersionAsync(component.Id, 1, hashV1, defV1);
        _ = await harness.AddVersionAsync(component.Id, 2, hashV2, defV2, parentVersionId: first.Id);

        GetSharedComponentVersionsQueryHandler handler = CreateHandler(harness);
        ErrorOr<List<SharedComponentVersionDto>> result = await handler.Handle(
            new GetSharedComponentVersionsQuery(ORG, USER, component.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.All(result.Value, dto => Assert.Null(dto.Narrative));
    }

    [Fact]
    public async Task Versions_IncludeNarratives_DescribesEachVersionAgainstItsParent()
    {
        using var harness = new SharedComponentsTestHarness();
        SharedComponent component = await harness.AddComponentAsync(SharedComponentKind.Section, "Alpha");
        (string defV1, string hashV1) = Definition(DefSection("Alpha", ("rate", "Extract")));
        (string defV2, string hashV2) = Definition(DefSection("Alpha", ("rate", "Extract"), ("fresh", "New")));
        SharedComponentVersion first = await harness.AddVersionAsync(component.Id, 1, hashV1, defV1);
        _ = await harness.AddVersionAsync(component.Id, 2, hashV2, defV2, parentVersionId: first.Id);

        GetSharedComponentVersionsQueryHandler handler = CreateHandler(harness);
        ErrorOr<List<SharedComponentVersionDto>> result = await handler.Handle(
            new GetSharedComponentVersionsQuery(ORG, USER, component.Id, IncludeNarratives: true),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal("Added block 'Node'", result.Value[0].Narrative);
        Assert.Equal(WorkflowNarrativeGenerator.FirstVersionSummary, result.Value[1].Narrative);
    }

    [Fact]
    public async Task Versions_IncludeNarratives_PayloadAbsentLinksYieldNullNarratives()
    {
        using var harness = new SharedComponentsTestHarness();
        SharedComponent component = await harness.AddComponentAsync(SharedComponentKind.Section, "Alpha");
        (string defV1, string hashV1) = Definition(DefSection("Alpha", ("rate", "Extract")));
        (string defV3, string hashV3) = Definition(DefSection("Alpha", ("rate", "Extract v3")));
        SharedComponentVersion first = await harness.AddVersionAsync(component.Id, 1, hashV1, defV1);
        SharedComponentVersion absent = await harness.AddVersionAsync(
            component.Id, 2, new string('b', 64), contentJson: null,
            payloadAbsentAt: DateTime.UtcNow, parentVersionId: first.Id);
        _ = await harness.AddVersionAsync(component.Id, 3, hashV3, defV3, parentVersionId: absent.Id);

        GetSharedComponentVersionsQueryHandler handler = CreateHandler(harness);
        ErrorOr<List<SharedComponentVersionDto>> result = await handler.Handle(
            new GetSharedComponentVersionsQuery(ORG, USER, component.Id, IncludeNarratives: true),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(3, result.Value.Count);
        Assert.Null(result.Value[0].Narrative);
        Assert.Null(result.Value[1].Narrative);
        Assert.Equal(WorkflowNarrativeGenerator.FirstVersionSummary, result.Value[2].Narrative);
    }

    private static GetSharedComponentVersionsQueryHandler CreateHandler(
        SharedComponentsTestHarness harness,
        Dictionary<string, string>? names = null)
    {
        IUserDisplayNameResolver resolver = Substitute.For<IUserDisplayNameResolver>();
        _ = resolver
            .ResolveAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(names ?? []);

        return new GetSharedComponentVersionsQueryHandler(
            harness.UnitOfWork,
            resolver,
            new SharedComponentDefinitionDiffer(new WorkflowDiffService(), new WorkflowNarrativeGenerator()));
    }

    private static (string DefinitionJson, string ContentHash) Definition(Section section)
    {
        var canonicalizer = new WorkflowCanonicalizer();
        string json = new SharedComponentContentNormalizer(canonicalizer).ToDefinitionJson(section);

        return (json, canonicalizer.ComputeContentHash(json));
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
}
