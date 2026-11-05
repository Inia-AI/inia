using ErrorOr;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.SharedComponents.GetSharedComponentVersionContent;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;

namespace IniaAi.Application.UnitTests.Queries.SharedComponents;

public class GetSharedComponentVersionContentQueryHandlerTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";

    [Fact]
    public async Task VersionContent_ServesStoredPayloadVerbatim()
    {
        using var harness = new SharedComponentsTestHarness();
        SharedComponent component = await harness.AddComponentAsync(SharedComponentKind.Section, "Alpha");
        SharedComponentVersion v1 = await harness.AddVersionAsync(
            component.Id, 1, new string('a', 64), """{"Title":"Alpha","Blocks":[]}""");
        _ = await harness.AddVersionAsync(
            component.Id, 2, new string('b', 64), """{"Title":"Beta","Blocks":[]}""", parentVersionId: v1.Id);

        var handler = new GetSharedComponentVersionContentQueryHandler(harness.UnitOfWork);
        ErrorOr<SharedComponentVersionContentResult> result = await handler.Handle(
            new GetSharedComponentVersionContentQuery(ORG, USER, component.Id, v1.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(component.Id, result.Value.SharedComponentId);
        Assert.Equal(v1.Id, result.Value.VersionId);
        Assert.Equal(new string('a', 64), result.Value.ContentHash);
        Assert.Equal("""{"Title":"Alpha","Blocks":[]}""", result.Value.ContentJson);
    }

    [Fact]
    public async Task VersionContent_UnknownComponent_ReturnsNotFound()
    {
        using var harness = new SharedComponentsTestHarness();

        var handler = new GetSharedComponentVersionContentQueryHandler(harness.UnitOfWork);
        ErrorOr<SharedComponentVersionContentResult> result = await handler.Handle(
            new GetSharedComponentVersionContentQuery(ORG, USER, "missing", "v-any"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.UNKNOWN_COMPONENT, result.FirstError.Code);
    }

    [Fact]
    public async Task VersionContent_VersionOfAnotherComponent_ReturnsUnknownVersion()
    {
        using var harness = new SharedComponentsTestHarness();
        SharedComponent component = await harness.AddComponentAsync(SharedComponentKind.Section, "Alpha");
        SharedComponent other = await harness.AddComponentAsync(SharedComponentKind.Section, "Other");
        _ = await harness.AddVersionAsync(component.Id, 1, new string('a', 64), """{"v":1}""");
        SharedComponentVersion foreign = await harness.AddVersionAsync(
            other.Id, 1, new string('b', 64), """{"v":1}""");

        var handler = new GetSharedComponentVersionContentQueryHandler(harness.UnitOfWork);
        ErrorOr<SharedComponentVersionContentResult> result = await handler.Handle(
            new GetSharedComponentVersionContentQuery(ORG, USER, component.Id, foreign.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.UNKNOWN_VERSION, result.FirstError.Code);
    }

    [Fact]
    public async Task VersionContent_PayloadAbsentRow_ReturnsNotFoundOnReadChannel()
    {
        using var harness = new SharedComponentsTestHarness();
        SharedComponent component = await harness.AddComponentAsync(SharedComponentKind.Section, "Alpha");
        SharedComponentVersion absent = await harness.AddVersionAsync(
            component.Id, 1, new string('a', 64), contentJson: null, payloadAbsentAt: DateTime.UtcNow);

        var handler = new GetSharedComponentVersionContentQueryHandler(harness.UnitOfWork);
        ErrorOr<SharedComponentVersionContentResult> result = await handler.Handle(
            new GetSharedComponentVersionContentQuery(ORG, USER, component.Id, absent.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.PAYLOAD_ABSENT, result.FirstError.Code);
    }
}
