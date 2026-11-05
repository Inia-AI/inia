using IniaAi.WebApi.Contracts.Responses.ModelPricing;
using System.Linq.Expressions;
using ContractComponentType = IniaAi.WebApi.Contracts.Enums.PricingComponentType;
using ContractPricingType = IniaAi.WebApi.Contracts.Enums.PricingType;
using ContractPricingUnit = IniaAi.WebApi.Contracts.Enums.PricingUnit;
using ModelPricingEntity = IniaAi.Domain.Entities.TokenUsage.ModelPricing;

namespace IniaAi.Application.Queries.ModelPricing;

public static class ModelPricingProjections
{
    public static readonly Expression<Func<ModelPricingEntity, ModelPricingListItemDto>> ToListItem = pricing => new ModelPricingListItemDto
    {
        Id = pricing.Id,
        Provider = pricing.Provider,
        Family = pricing.Family,
        ModelId = pricing.ModelId,
        ModelAlias = pricing.ModelAlias,
        DisplayName = pricing.DisplayName,
        PricingType = (ContractPricingType)pricing.PricingType,
        IsActive = pricing.IsActive,
        EffectiveFrom = pricing.EffectiveFrom,
    };

    public static readonly Expression<Func<ModelPricingEntity, ModelPricingDto>> ToDto = pricing => new ModelPricingDto
    {
        Id = pricing.Id,
        Provider = pricing.Provider,
        Family = pricing.Family,
        ModelId = pricing.ModelId,
        ModelAlias = pricing.ModelAlias,
        DisplayName = pricing.DisplayName,
        PricingType = (ContractPricingType)pricing.PricingType,
        EffectiveFrom = pricing.EffectiveFrom,
        EffectiveTo = pricing.EffectiveTo,
        IsActive = pricing.IsActive,
        LongPromptThresholdTokens = pricing.LongPromptThresholdTokens,
        RegionalEndpointMultiplier = pricing.RegionalEndpointMultiplier,
        Components = pricing.Components.Select(component => new ModelPricingComponentDto
        {
            Id = component.Id,
            ComponentType = (ContractComponentType)component.ComponentType,
            Cost = component.Cost,
            Unit = (ContractPricingUnit)component.Unit,
            Currency = component.Currency,
        }).ToList(),
    };

    private static readonly Func<ModelPricingEntity, ModelPricingDto> _toDto = ToDto.Compile();

    public static ModelPricingDto DtoOf(ModelPricingEntity pricing)
    {
        return _toDto(pricing);
    }
}
