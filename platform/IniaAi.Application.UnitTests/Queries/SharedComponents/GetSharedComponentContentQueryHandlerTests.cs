using ErrorOr;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.SharedComponents.GetSharedComponentContent;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;

namespace IniaAi.Application.UnitTests.Queries.SharedComponents;

public class GetSharedComponentContentQueryHandlerTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";

    [Fact]
    public async Task Content_ServesHeadPayloadVerbatim_WithTokens()
    {
        using var harness = new SharedComponentsTestHarness();
        SharedComponent component = await harness.AddComponentAsync(SharedComponentKind.Block, "Alpha");
        _ = await harness.AddVersionAsync(component.Id, 1, new string('a', 64), """{"v":1}""");
        SharedComponentVersion head = await harness.AddVersionAsync(
            component.Id, 2, new string('b', 64), """{"Type":"AiQuery","Name":"Alpha"}""");

        var handler = new GetSharedComponentContentQueryHandler(harness.UnitOfWork);
        ErrorOr<SharedComponentContentResult> result = await handler.Handle(
            new GetSharedComponentContentQuery(ORG, USER, component.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(head.Id, result.Value.HeadVersionId);
        Assert.Equal(new string('b', 64), result.Value.HeadContentHash);
        Assert.Equal("""{"Type":"AiQuery","Name":"Alpha"}""", result.Value.ContentJson);
    }

    [Fact]
    public async Task Content_PayloadAbsentHead_ReturnsPayloadAbsentError()
    {
        using var harness = new SharedComponentsTestHarness();
        SharedComponent component = await harness.AddComponentAsync(SharedComponentKind.Block, "Alpha");
        _ = await harness.AddVersionAsync(
            component.Id, 1, new string('a', 64), contentJson: null, payloadAbsentAt: DateTime.UtcNow);

        var handler = new GetSharedComponentContentQueryHandler(harness.UnitOfWork);
        ErrorOr<SharedComponentContentResult> result = await handler.Handle(
            new GetSharedComponentContentQuery(ORG, USER, component.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.PAYLOAD_ABSENT, result.FirstError.Code);
    }

    [Fact]
    public async Task Content_ZeroVersions_ReturnsNoVersionsConflict()
    {
        using var harness = new SharedComponentsTestHarness();
        SharedComponent component = await harness.AddComponentAsync(SharedComponentKind.Block, "Alpha");

        var handler = new GetSharedComponentContentQueryHandler(harness.UnitOfWork);
        ErrorOr<SharedComponentContentResult> result = await handler.Handle(
            new GetSharedComponentContentQuery(ORG, USER, component.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.NO_VERSIONS, result.FirstError.Code);
    }

    [Fact]
    public async Task Content_UnknownId_ReturnsNotFound()
    {
        using var harness = new SharedComponentsTestHarness();

        var handler = new GetSharedComponentContentQueryHandler(harness.UnitOfWork);
        ErrorOr<SharedComponentContentResult> result = await handler.Handle(
            new GetSharedComponentContentQuery(ORG, USER, "missing"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.UNKNOWN_COMPONENT, result.FirstError.Code);
    }
}
