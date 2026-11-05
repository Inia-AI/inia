using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.WebApi.Contracts.Responses.ModelPricing;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.ModelPricing.GetModelPricing;

/// <summary>
/// Handles retrieval of a model pricing entry by ID.
/// </summary>
public class GetModelPricingQueryHandler : IRequestHandler<GetModelPricingQuery, ErrorOr<ModelPricingDto>>
{
    private readonly ICoreDbContext _coreDbContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetModelPricingQueryHandler"/> class.
    /// </summary>
    /// <param name="coreDbContext">The core database context.</param>
    public GetModelPricingQueryHandler(ICoreDbContext coreDbContext)
    {
        _coreDbContext = coreDbContext;
    }

    /// <inheritdoc/>
    public async Task<ErrorOr<ModelPricingDto>> Handle(GetModelPricingQuery request, CancellationToken cancellationToken)
    {
        ModelPricingDto? dto = await _coreDbContext.ModelPricings
            .Where(m => m.Id == request.Id)
            .Select(ModelPricingProjections.ToDto)
            .FirstOrDefaultAsync(cancellationToken);

        if (dto is null)
        {
            return Error.NotFound("ModelPricing.NotFound", $"Model pricing with ID '{request.Id}' was not found.");
        }

        return dto;
    }
}
