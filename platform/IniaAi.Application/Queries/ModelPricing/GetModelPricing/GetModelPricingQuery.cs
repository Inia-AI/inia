using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.ModelPricing;

namespace IniaAi.Application.Queries.ModelPricing.GetModelPricing;

/// <summary>
/// Query to retrieve a single model pricing entry by its ID.
/// </summary>
/// <param name="Id">The ID of the model pricing to retrieve.</param>
public record GetModelPricingQuery(string Id) : IRequest<ErrorOr<ModelPricingDto>>;
