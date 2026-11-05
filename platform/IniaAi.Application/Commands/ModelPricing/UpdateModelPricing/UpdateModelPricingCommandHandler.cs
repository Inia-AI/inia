using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Commands.ModelPricing.UpdateModelPricing;

/// <summary>
/// Handles updates to an existing model pricing entry.
/// </summary>
public class UpdateModelPricingCommandHandler : IRequestHandler<UpdateModelPricingCommand, ErrorOr<Updated>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly ICoreDbContext _coreDbContext;
    private readonly IDbExceptionAnalyzer _exceptionAnalyzer;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateModelPricingCommandHandler"/> class.
    /// </summary>
    /// <param name="coreUnitOfWork">The core unit of work.</param>
    /// <param name="coreDbContext">The core database context.</param>
    /// <param name="exceptionAnalyzer">The database exception analyzer.</param>
    public UpdateModelPricingCommandHandler(
        ICoreUnitOfWork coreUnitOfWork,
        ICoreDbContext coreDbContext,
        IDbExceptionAnalyzer exceptionAnalyzer)
    {
        _coreUnitOfWork = coreUnitOfWork;
        _coreDbContext = coreDbContext;
        _exceptionAnalyzer = exceptionAnalyzer;
    }

    /// <inheritdoc/>
    public async Task<ErrorOr<Updated>> Handle(UpdateModelPricingCommand request, CancellationToken cancellationToken)
    {
        // Load entity with components for update
        Domain.Entities.TokenUsage.ModelPricing? entity = await _coreDbContext.ModelPricings
            .Include(m => m.Components)
            .FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken);

        if (entity is null)
        {
            return Error.NotFound("ModelPricing.NotFound", $"Model pricing with ID '{request.Id}' was not found.");
        }

        // Update basic properties
        entity.Provider = request.Provider;
        entity.ModelId = request.ModelId;
        entity.ModelAlias = request.ModelAlias;
        entity.DisplayName = request.DisplayName;
        entity.PricingType = request.PricingType;
        entity.EffectiveFrom = request.EffectiveFrom;
        entity.EffectiveTo = request.EffectiveTo;
        entity.IsActive = request.IsActive;

        if (request.Family is not null)
        {
            entity.Family = request.Family;
        }

        if (request.LongPromptThresholdTokens is int threshold)
        {
            entity.LongPromptThresholdTokens = threshold > 0 ? threshold : null;
        }

        // Update components: remove existing and add new ones. A request naming no long-prompt
        // component keeps the stored ones, so a form that knows only the standard prices does not
        // delete them.
        bool requestNamesLongPrompt = request.Components.Any(static c => ModelPricingRates.IsLongPromptComponent(c.ComponentType));
        List<ModelPricingComponent> keptLongPrompt = requestNamesLongPrompt
            ? []
            : [.. entity.Components.Where(static c => ModelPricingRates.IsLongPromptComponent(c.ComponentType))];
        _coreDbContext.ModelPricingComponents.RemoveRange(entity.Components.Except(keptLongPrompt));

        entity.Components =
        [
            .. keptLongPrompt,
            .. request.Components.Select(c => new ModelPricingComponent
            {
                Id = Guid.NewGuid().ToString(),
                ModelPricingId = entity.Id,
                ComponentType = c.ComponentType,
                Cost = c.Cost,
                Unit = c.Unit,
                Currency = c.Currency,
            }),
        ];

        try
        {
            _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);
            return Result.Updated;
        }
        catch (Exception ex) when (_exceptionAnalyzer.IsUniqueConstraintViolation(ex))
        {
            return Error.Conflict("ModelPricing.Duplicate", $"A model pricing entry with Provider '{request.Provider}' and ModelId '{request.ModelId}' already exists.");
        }
    }
}
