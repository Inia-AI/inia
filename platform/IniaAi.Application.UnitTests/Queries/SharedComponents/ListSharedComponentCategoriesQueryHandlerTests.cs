using ErrorOr;
using FluentValidation.Results;
using IniaAi.Application.Queries.SharedComponents.ListSharedComponentCategories;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.WebApi.Contracts.Responses.SharedComponents;

namespace IniaAi.Application.UnitTests.Queries.SharedComponents;

public class ListSharedComponentCategoriesQueryHandlerTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";

    [Fact]
    public async Task List_OrdersByDisplayOrderThenName_WithNullsLast()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("zeta", "Zeta Helpers");
        _ = await harness.AddCategoryAsync("analysis", "Analysis", displayOrder: 2);
        _ = await harness.AddCategoryAsync("extraction", "Extraction", displayOrder: 1);
        _ = await harness.AddCategoryAsync("alpha", "Alpha Helpers");

        var handler = new ListSharedComponentCategoriesQueryHandler(harness.UnitOfWork);
        ErrorOr<SharedComponentCategoriesDto> result = await handler.Handle(
            new ListSharedComponentCategoriesQuery(ORG, USER),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(
            new[] { "extraction", "analysis", "alpha", "zeta" },
            result.Value.Categories.Select(c => c.Id).ToArray());
    }

    [Fact]
    public async Task List_CountsComponentsPerCategory_IncludingRetired_AndUncategorized()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("extraction", "Extraction", displayOrder: 1);
        _ = await harness.AddCategoryAsync("analysis", "Analysis", displayOrder: 2);
        _ = await harness.AddComponentAsync(SharedComponentKind.Block, "Extract A", categoryId: "extraction");
        _ = await harness.AddComponentAsync(SharedComponentKind.Section, "Extract B", categoryId: "extraction");
        _ = await harness.AddComponentAsync(
            SharedComponentKind.Block, "Old Extractor", SharedComponentState.Retired, categoryId: "extraction");
        _ = await harness.AddComponentAsync(SharedComponentKind.Block, "Loose End");

        var handler = new ListSharedComponentCategoriesQueryHandler(harness.UnitOfWork);
        ErrorOr<SharedComponentCategoriesDto> result = await handler.Handle(
            new ListSharedComponentCategoriesQuery(ORG, USER),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(3, result.Value.Categories.Single(c => c.Id == "extraction").ComponentCount);
        Assert.Equal(0, result.Value.Categories.Single(c => c.Id == "analysis").ComponentCount);
        Assert.Equal(1, result.Value.UncategorizedCount);
    }

    [Fact]
    public async Task List_EmptyVocabulary_ReturnsEmptyListAndZeroUncategorized()
    {
        using var harness = new SharedComponentsTestHarness();

        var handler = new ListSharedComponentCategoriesQueryHandler(harness.UnitOfWork);
        ErrorOr<SharedComponentCategoriesDto> result = await handler.Handle(
            new ListSharedComponentCategoriesQuery(ORG, USER),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Empty(result.Value.Categories);
        Assert.Equal(0, result.Value.UncategorizedCount);
    }

    [Fact]
    public void Validator_RequiresIdentity()
    {
        var validator = new ListSharedComponentCategoriesQueryValidator();

        ValidationResult emptyOrg = validator.Validate(new ListSharedComponentCategoriesQuery(string.Empty, USER));
        ValidationResult emptyUser = validator.Validate(new ListSharedComponentCategoriesQuery(ORG, string.Empty));
        ValidationResult ok = validator.Validate(new ListSharedComponentCategoriesQuery(ORG, USER));

        Assert.False(emptyOrg.IsValid);
        Assert.False(emptyUser.IsValid);
        Assert.True(ok.IsValid);
    }
}
