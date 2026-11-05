using ErrorOr;
using IniaAi.Application.Commands.SharedComponents.SetSharedComponentCategory;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.WebApi.Contracts.Responses.SharedComponents;

namespace IniaAi.Application.UnitTests.Commands.SharedComponents;

public class SetSharedComponentCategoryCommandHandlerTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";

    [Fact]
    public async Task Set_RecategorizesComponent_AndReturnsUpdatedSummary()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("extraction", "Extraction");
        _ = await harness.AddCategoryAsync("analysis", "Analysis");
        SharedComponent component = await harness.AddComponentAsync(
            SharedComponentKind.Block, "Extractor", categoryId: "extraction");

        var handler = new SetSharedComponentCategoryCommandHandler(harness.UnitOfWork);
        ErrorOr<SharedComponentSummaryDto> result = await handler.Handle(
            new SetSharedComponentCategoryCommand(ORG, USER, component.Id, "analysis"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal("analysis", result.Value.CategoryId);
        Assert.Equal("Analysis", result.Value.CategoryName);
        SharedComponent persisted = harness.Context.SharedComponents.Single(c => c.Id == component.Id);
        Assert.Equal("analysis", persisted.CategoryId);
    }

    [Fact]
    public async Task Set_UnknownComponent_ReturnsNotFound()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("analysis", "Analysis");

        var handler = new SetSharedComponentCategoryCommandHandler(harness.UnitOfWork);
        ErrorOr<SharedComponentSummaryDto> result = await handler.Handle(
            new SetSharedComponentCategoryCommand(ORG, USER, "ghost-component", "analysis"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.UNKNOWN_COMPONENT, result.FirstError.Code);
    }

    [Fact]
    public async Task Set_UnknownCategory_ReturnsNotFound_AndLeavesComponentUntouched()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("extraction", "Extraction");
        SharedComponent component = await harness.AddComponentAsync(
            SharedComponentKind.Block, "Extractor", categoryId: "extraction");

        var handler = new SetSharedComponentCategoryCommandHandler(harness.UnitOfWork);
        ErrorOr<SharedComponentSummaryDto> result = await handler.Handle(
            new SetSharedComponentCategoryCommand(ORG, USER, component.Id, "ghost"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.UNKNOWN_CATEGORY, result.FirstError.Code);
        SharedComponent persisted = harness.Context.SharedComponents.Single(c => c.Id == component.Id);
        Assert.Equal("extraction", persisted.CategoryId);
    }
}
