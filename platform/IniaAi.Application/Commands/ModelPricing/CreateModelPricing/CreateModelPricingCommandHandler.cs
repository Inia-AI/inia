using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Persistence;
using IniaAi.Application.Queries.ModelPricing;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.WebApi.Contracts.Responses.ModelPricing;

namespace IniaAi.Application.Commands.ModelPricing.CreateModelPricing;

/// <summary>
/// Handles creation of a new model pricing entry.
/// </summary>
public class CreateModelPricingCommandHandler : IRequestHandler<CreateModelPricingCommand, ErrorOr<ModelPricingDto>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly IDbExceptionAnalyzer _exceptionAnalyzer;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateModelPricingCommandHandler"/> class.
    /// </summary>
    /// <param name="coreUnitOfWork">The core unit of work.</param>
    /// <param name="exceptionAnalyzer">The database exception analyzer.</param>
    public CreateModelPricingCommandHandler(
        ICoreUnitOfWork coreUnitOfWork,
        IDbExceptionAnalyzer exceptionAnalyzer)
    {
        _coreUnitOfWork = coreUnitOfWork;
        _exceptionAnalyzer = exceptionAnalyzer;
    }

    /// <inheritdoc/>
    public async Task<ErrorOr<ModelPricingDto>> Handle(CreateModelPricingCommand request, CancellationToken cancellationToken)
    {
        try
        {
            string pricingId = Guid.NewGuid().ToString();

            var entity = new Domain.Entities.TokenUsage.ModelPricing
            {
                Id = pricingId,
                Provider = request.Provider,
                Family = request.Family,
                LongPromptThresholdTokens = request.LongPromptThresholdTokens is > 0 ? request.LongPromptThresholdTokens : null,
                ModelId = request.ModelId,
                ModelAlias = request.ModelAlias,
                DisplayName = request.DisplayName,
                PricingType = request.PricingType,
                EffectiveFrom = request.EffectiveFrom,
                EffectiveTo = request.EffectiveTo,
                IsActive = request.IsActive,
                Components = [.. request.Components.Select(c => new ModelPricingComponent
                {
                    Id = Guid.NewGuid().ToString(),
                    ModelPricingId = pricingId,
                    ComponentType = c.ComponentType,
                    Cost = c.Cost,
                    Unit = c.Unit,
                    Currency = c.Currency,
                })],
            };

            await _coreUnitOfWork.Repository<Domain.Entities.TokenUsage.ModelPricing>().AddAsync(entity);
            _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);

            return ModelPricingProjections.DtoOf(entity);
        }
        catch (Exception ex) when (_exceptionAnalyzer.IsUniqueConstraintViolation(ex))
        {
            return Error.Conflict("ModelPricing.Duplicate", $"A model pricing entry with Provider '{request.Provider}' and ModelId '{request.ModelId}' already exists.");
        }
    }
}
