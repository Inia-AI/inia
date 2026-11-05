using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.ModelPricing.DeleteModelPricing;

/// <summary>
/// Command to delete a model pricing entry.
/// </summary>
/// <param name="Id">The ID of the model pricing entry to delete.</param>
public record DeleteModelPricingCommand(string Id) : IRequest<ErrorOr<Deleted>>;
