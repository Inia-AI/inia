using ErrorOr;
using IniaAi.Application.Commands.SharedComponents.DeleteSharedComponentCategory;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.UnitTests.Commands.SharedComponents;

public class DeleteSharedComponentCategoryCommandHandlerTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";

    [Fact]
    public async Task Delete_UnreferencedCategory_SoftDeletesTheRow()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("preparation", "Preparation");

        var handler = new DeleteSharedComponentCategoryCommandHandler(harness.UnitOfWork);
        ErrorOr<Deleted> result = await handler.Handle(
            new DeleteSharedComponentCategoryCommand(ORG, USER, "preparation"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Empty(await harness.Context.SharedComponentCategories.ToListAsync(TestContext.Current.CancellationToken));
        SharedComponentCategory buried = Assert.Single(
            await harness.Context.SharedComponentCategories
                .IgnoreQueryFilters()
                .ToListAsync(TestContext.Current.CancellationToken));
        Assert.True(buried.IsDeleted);
    }

    [Fact]
    public async Task Delete_UnknownCategory_ReturnsNotFound()
    {
        using var harness = new SharedComponentsTestHarness();

        var handler = new DeleteSharedComponentCategoryCommandHandler(harness.UnitOfWork);
        ErrorOr<Deleted> result = await handler.Handle(
            new DeleteSharedComponentCategoryCommand(ORG, USER, "ghost"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.UNKNOWN_CATEGORY, result.FirstError.Code);
    }

    [Fact]
    public async Task Delete_ReferencedCategory_Conflicts_IncludingRetiredComponents()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("analysis", "Analysis");
        _ = await harness.AddComponentAsync(
            SharedComponentKind.Block, "Old Analyzer", SharedComponentState.Retired, categoryId: "analysis");

        var handler = new DeleteSharedComponentCategoryCommandHandler(harness.UnitOfWork);
        ErrorOr<Deleted> result = await handler.Handle(
            new DeleteSharedComponentCategoryCommand(ORG, USER, "analysis"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.CATEGORY_IN_USE, result.FirstError.Code);
        SharedComponentCategory survivor = Assert.Single(
            await harness.Context.SharedComponentCategories.ToListAsync(TestContext.Current.CancellationToken));
        Assert.False(survivor.IsDeleted);
    }
}
