using ErrorOr;
using FluentValidation.Results;
using IniaAi.Application.Commands.SharedComponents.SaveSharedComponentCategory;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.WebApi.Contracts.Responses.SharedComponents;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.UnitTests.Commands.SharedComponents;

public class SaveSharedComponentCategoryCommandHandlerTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";

    [Fact]
    public async Task Save_NewId_CreatesCategory()
    {
        using var harness = new SharedComponentsTestHarness();

        var handler = new SaveSharedComponentCategoryCommandHandler(harness.UnitOfWork);
        ErrorOr<SharedComponentCategoryDto> result = await handler.Handle(
            new SaveSharedComponentCategoryCommand(ORG, USER, "extraction", "Extraction", "Pulling data out", 2),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal("extraction", result.Value.Id);
        Assert.Equal("Extraction", result.Value.Name);
        Assert.Equal("Pulling data out", result.Value.Description);
        Assert.Equal(2, result.Value.DisplayOrder);
        Assert.Equal(0, result.Value.ComponentCount);
        SharedComponentCategory row = Assert.Single(harness.Context.SharedComponentCategories);
        Assert.Equal("Extraction", row.Name);
    }

    [Fact]
    public async Task Save_ExistingId_UpdatesInPlace_AndKeepsComponentCount()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("analysis", "Analysis", displayOrder: 5);
        _ = await harness.AddComponentAsync(SharedComponentKind.Block, "Analyzer", categoryId: "analysis");

        var handler = new SaveSharedComponentCategoryCommandHandler(harness.UnitOfWork);
        ErrorOr<SharedComponentCategoryDto> result = await handler.Handle(
            new SaveSharedComponentCategoryCommand(ORG, USER, "analysis", "Deep Analysis", DisplayOrder: 1),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal("Deep Analysis", result.Value.Name);
        Assert.Equal(1, result.Value.DisplayOrder);
        Assert.Equal(1, result.Value.ComponentCount);
        SharedComponentCategory row = Assert.Single(harness.Context.SharedComponentCategories);
        Assert.Equal("Deep Analysis", row.Name);
        Assert.Equal(1, row.DisplayOrder);
    }

    [Fact]
    public async Task Save_SoftDeletedId_ResurrectsTheRow()
    {
        using var harness = new SharedComponentsTestHarness();
        SharedComponentCategory buried = await harness.AddCategoryAsync("utilities", "Utilities");
        buried.IsDeleted = true;
        buried.DeletedAt = DateTime.UtcNow;
        buried.DeletedByUserId = USER;
        _ = await harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new SaveSharedComponentCategoryCommandHandler(harness.UnitOfWork);
        ErrorOr<SharedComponentCategoryDto> result = await handler.Handle(
            new SaveSharedComponentCategoryCommand(ORG, USER, "utilities", "Utilities Reborn"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentCategory row = Assert.Single(
            await harness.Context.SharedComponentCategories.ToListAsync(TestContext.Current.CancellationToken));
        Assert.False(row.IsDeleted);
        Assert.Null(row.DeletedAt);
        Assert.Null(row.DeletedByUserId);
        Assert.Equal("Utilities Reborn", row.Name);
    }

    [Fact]
    public void Validator_EnforcesIdShape_AndLengths()
    {
        var validator = new SaveSharedComponentCategoryCommandValidator();

        Assert.True(validator.Validate(new SaveSharedComponentCategoryCommand(ORG, USER, "load-verify", "Load & Verify")).IsValid);
        Assert.True(validator.Validate(new SaveSharedComponentCategoryCommand(ORG, USER, "a1", "A1")).IsValid);

        ValidationResult spaced = validator.Validate(new SaveSharedComponentCategoryCommand(ORG, USER, "bad id", "Bad"));
        ValidationResult upper = validator.Validate(new SaveSharedComponentCategoryCommand(ORG, USER, "Upper", "Bad"));
        ValidationResult leadingDash = validator.Validate(new SaveSharedComponentCategoryCommand(ORG, USER, "-lead", "Bad"));
        ValidationResult emptyName = validator.Validate(new SaveSharedComponentCategoryCommand(ORG, USER, "ok-id", string.Empty));
        ValidationResult longId = validator.Validate(new SaveSharedComponentCategoryCommand(ORG, USER, new string('a', 101), "Bad"));

        Assert.False(spaced.IsValid);
        Assert.False(upper.IsValid);
        Assert.False(leadingDash.IsValid);
        Assert.False(emptyName.IsValid);
        Assert.False(longId.IsValid);
    }
}
