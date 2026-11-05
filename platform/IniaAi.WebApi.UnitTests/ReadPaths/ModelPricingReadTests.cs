using ErrorOr;
using IniaAi.Application.Commands.ModelPricing.CreateModelPricing;
using IniaAi.Application.Commands.ModelPricing.UpdateModelPricing;
using IniaAi.Application.Queries.ModelPricing.GetModelPricing;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.WebApi.Contracts.Responses.ModelPricing;

namespace IniaAi.WebApi.UnitTests.ReadPaths;

public class ModelPricingReadTests : ReadPathTests
{
    [Fact]
    public async Task ACreatedModelPricing_AnswersWithTheSameJson()
    {
        ErrorOr<ModelPricingDto> result = await Handler<CreateModelPricingCommandHandler>().Handle(
            new CreateModelPricingCommand(
                "aws-bedrock",
                "claude-haiku",
                "haiku",
                "Claude Haiku",
                PricingType.Llm,
                Created,
                Created.AddYears(1),
                IsActive: true,
                [new PricingComponentData(PricingComponentType.Input, 0.8m), new PricingComponentData(PricingComponentType.Output, 4m, PricingUnit.PerMillion, "EUR")],
                Family: "Anthropic"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError, string.Join("; ", result.ErrorsOrEmptyList.Select(error => error.Description)));
        ModelPricing stored = Assert.Single(Core.ModelPricings.Local);
        Assert.Equal(stored.Id, result.Value.Id);
        Assert.Equal(stored.Components.Select(component => component.Id).Order(), result.Value.Components.Select(component => component.Id).Order());
        result.Value.Id = "pricing-created";
        foreach (ModelPricingComponentDto component in result.Value.Components)
        {
            component.Id = $"component-{component.ComponentType}";
        }

        AssertMatchesFixture(result.Value, "ModelPricingCreated");
    }

    [Fact]
    public async Task AModelPricing_ReadsAsTheSameJson()
    {
        var pricing = new ModelPricing
        {
            Id = "pricing-1",
            Provider = "aws-bedrock",
            Family = "anthropic",
            ModelId = "claude-sonnet",
            ModelAlias = "sonnet",
            DisplayName = "Claude Sonnet",
            PricingType = PricingType.Llm,
            EffectiveFrom = Created,
            EffectiveTo = Created.AddYears(1),
            IsActive = true,
            CreatedAt = Created,
            UpdatedAt = Created,
            Components =
            [
                new ModelPricingComponent { Id = "component-input", ModelPricingId = "pricing-1", ComponentType = PricingComponentType.Input, Cost = 3m, Unit = PricingUnit.PerMillion, Currency = "USD", CreatedAt = Created, UpdatedAt = Created },
                new ModelPricingComponent { Id = "component-output", ModelPricingId = "pricing-1", ComponentType = PricingComponentType.Output, Cost = 15m, Unit = PricingUnit.PerMillion, Currency = "USD", CreatedAt = Created, UpdatedAt = Created },
            ],
        };
        _ = Core.ModelPricings.Add(pricing);
        _ = await Core.SaveChangesAsync(TestContext.Current.CancellationToken);
        Core.ChangeTracker.Clear();

        ErrorOr<ModelPricingDto> result = await Handler<GetModelPricingQueryHandler>().Handle(
            new GetModelPricingQuery("pricing-1"),
            TestContext.Current.CancellationToken);

        result.Value.Components = [.. result.Value.Components.OrderBy(component => component.Id)];
        AssertMatchesFixture(result.Value, "ModelPricing");
    }

    [Fact]
    public async Task AMissingModelPricing_IsNotFound()
    {
        ErrorOr<ModelPricingDto> result = await Handler<GetModelPricingQueryHandler>().Handle(
            new GetModelPricingQuery("pricing-missing"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Theory]
    [InlineData("Cohere", "Cohere")]
    [InlineData(null, "Anthropic")]
    public async Task AnUpdate_ReplacesTheFamilyOnlyWhenItNamesOne(string? sentFamily, string expectedFamily)
    {
        _ = Core.ModelPricings.Add(new ModelPricing
        {
            Id = "pricing-family",
            Provider = "aws-bedrock",
            Family = "Anthropic",
            ModelId = "claude-family",
            DisplayName = "Claude Family",
            PricingType = PricingType.Llm,
            EffectiveFrom = Created,
            IsActive = true,
            CreatedAt = Created,
            UpdatedAt = Created,
        });
        _ = await Core.SaveChangesAsync(TestContext.Current.CancellationToken);
        Core.ChangeTracker.Clear();

        ErrorOr<Updated> updated = await Handler<UpdateModelPricingCommandHandler>().Handle(
            new UpdateModelPricingCommand(
                "pricing-family",
                "aws-bedrock",
                "claude-family",
                null,
                "Claude Family",
                PricingType.Llm,
                Created,
                null,
                IsActive: true,
                [new PricingComponentData(PricingComponentType.Input, 1m)],
                sentFamily),
            TestContext.Current.CancellationToken);
        Core.ChangeTracker.Clear();

        Assert.False(updated.IsError, string.Join("; ", updated.ErrorsOrEmptyList.Select(error => error.Description)));
        ErrorOr<ModelPricingDto> read = await Handler<GetModelPricingQueryHandler>().Handle(
            new GetModelPricingQuery("pricing-family"),
            TestContext.Current.CancellationToken);
        Assert.Equal(expectedFamily, read.Value.Family);
    }

    [Theory]
    [InlineData(false, 0.50, 100_000)]
    [InlineData(true, 0.70, 100_000)]
    public async Task AnUpdate_KeepsTheLongPromptPricesItDoesNotName(bool namesLongPrompt, double expectedLongInput, int expectedThreshold)
    {
        _ = Core.ModelPricings.Add(new ModelPricing
        {
            Id = "pricing-long",
            Provider = "aws-bedrock",
            Family = "Anthropic",
            ModelId = "claude-haiku-5-5",
            DisplayName = "Claude Haiku 5.5",
            PricingType = PricingType.Llm,
            EffectiveFrom = Created,
            IsActive = true,
            LongPromptThresholdTokens = 100_000,
            CreatedAt = Created,
            UpdatedAt = Created,
            Components =
            [
                new ModelPricingComponent { Id = "c-in", ModelPricingId = "pricing-long", ComponentType = PricingComponentType.Input, Cost = 0.10m },
                new ModelPricingComponent { Id = "c-long-in", ModelPricingId = "pricing-long", ComponentType = PricingComponentType.LongPromptInput, Cost = 0.50m },
            ],
        });
        _ = await Core.SaveChangesAsync(TestContext.Current.CancellationToken);
        Core.ChangeTracker.Clear();

        List<PricingComponentData> sent = [new PricingComponentData(PricingComponentType.Input, 0.12m)];
        if (namesLongPrompt)
        {
            sent.Add(new PricingComponentData(PricingComponentType.LongPromptInput, 0.70m));
        }

        ErrorOr<Updated> updated = await Handler<UpdateModelPricingCommandHandler>().Handle(
            new UpdateModelPricingCommand(
                "pricing-long", "aws-bedrock", "claude-haiku-5-5", null, "Claude Haiku 5.5", PricingType.Llm, Created, null,
                IsActive: true, sent),
            TestContext.Current.CancellationToken);
        Core.ChangeTracker.Clear();

        Assert.False(updated.IsError, string.Join("; ", updated.ErrorsOrEmptyList.Select(error => error.Description)));
        ErrorOr<ModelPricingDto> read = await Handler<GetModelPricingQueryHandler>().Handle(
            new GetModelPricingQuery("pricing-long"),
            TestContext.Current.CancellationToken);
        Assert.Equal(expectedThreshold, read.Value.LongPromptThresholdTokens);
        Assert.Equal(0.12m, Assert.Single(read.Value.Components, static c => c.ComponentType == IniaAi.WebApi.Contracts.Enums.PricingComponentType.Input).Cost);
        Assert.Equal((decimal)expectedLongInput, Assert.Single(read.Value.Components, static c => c.ComponentType == IniaAi.WebApi.Contracts.Enums.PricingComponentType.LongPromptInput).Cost);
    }

    [Fact]
    public async Task AnUpdate_RemovesTheThresholdWhenItSendsZero()
    {
        _ = Core.ModelPricings.Add(new ModelPricing
        {
            Id = "pricing-clear",
            Provider = "aws-bedrock",
            ModelId = "claude-haiku-5-5",
            DisplayName = "Claude Haiku 5.5",
            PricingType = PricingType.Llm,
            EffectiveFrom = Created,
            IsActive = true,
            LongPromptThresholdTokens = 100_000,
            CreatedAt = Created,
            UpdatedAt = Created,
        });
        _ = await Core.SaveChangesAsync(TestContext.Current.CancellationToken);
        Core.ChangeTracker.Clear();

        _ = await Handler<UpdateModelPricingCommandHandler>().Handle(
            new UpdateModelPricingCommand(
                "pricing-clear", "aws-bedrock", "claude-haiku-5-5", null, "Claude Haiku 5.5", PricingType.Llm, Created, null,
                IsActive: true, [], LongPromptThresholdTokens: 0),
            TestContext.Current.CancellationToken);
        Core.ChangeTracker.Clear();

        ErrorOr<ModelPricingDto> read = await Handler<GetModelPricingQueryHandler>().Handle(
            new GetModelPricingQuery("pricing-clear"),
            TestContext.Current.CancellationToken);
        Assert.Null(read.Value.LongPromptThresholdTokens);
    }
}
