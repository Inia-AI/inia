using ErrorOr;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.SharedComponents.GetSharedComponent;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.WebApi.Contracts.Responses.SharedComponents;
using ContractsState = IniaAi.WebApi.Contracts.Enums.SharedComponentState;

namespace IniaAi.Application.UnitTests.Queries.SharedComponents;

public class GetSharedComponentQueryHandlerTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";

    [Fact]
    public async Task Get_ReturnsSummaryWithHeadTokens()
    {
        using var harness = new SharedComponentsTestHarness();
        SharedComponent component = await harness.AddComponentAsync(
            SharedComponentKind.Block, "Alpha", SharedComponentState.Retired, "A reusable block");
        SharedComponentVersion head = await harness.AddVersionAsync(component.Id, 1, new string('a', 64), """{"v":1}""");

        var handler = new GetSharedComponentQueryHandler(harness.UnitOfWork);
        ErrorOr<SharedComponentSummaryDto> result = await handler.Handle(
            new GetSharedComponentQuery(ORG, USER, component.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(component.Id, result.Value.Id);
        Assert.Equal("A reusable block", result.Value.Description);
        Assert.Equal(ContractsState.Retired, result.Value.State);
        Assert.Equal(head.Id, result.Value.HeadVersionId);
        Assert.Equal(new string('a', 64), result.Value.HeadContentHash);
        Assert.Equal(0, result.Value.ConsumerCount);
    }

    [Fact]
    public async Task Get_UnknownId_ReturnsNotFound()
    {
        using var harness = new SharedComponentsTestHarness();

        var handler = new GetSharedComponentQueryHandler(harness.UnitOfWork);
        ErrorOr<SharedComponentSummaryDto> result = await handler.Handle(
            new GetSharedComponentQuery(ORG, USER, "missing"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.UNKNOWN_COMPONENT, result.FirstError.Code);
    }
}
