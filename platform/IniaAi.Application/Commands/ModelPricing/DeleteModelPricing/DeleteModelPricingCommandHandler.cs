using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Core.Interfaces;

namespace IniaAi.Application.Commands.ModelPricing.DeleteModelPricing;

/// <summary>
/// Handles deletion of a model pricing entry.
/// </summary>
public class DeleteModelPricingCommandHandler : IRequestHandler<DeleteModelPricingCommand, ErrorOr<Deleted>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteModelPricingCommandHandler"/> class.
    /// </summary>
    /// <param name="coreUnitOfWork">Core unit of work.</param>
    public DeleteModelPricingCommandHandler(ICoreUnitOfWork coreUnitOfWork)
    {
        _coreUnitOfWork = coreUnitOfWork;
    }

    /// <inheritdoc/>
    public async Task<ErrorOr<Deleted>> Handle(DeleteModelPricingCommand request, CancellationToken cancellationToken)
    {
        Domain.Entities.TokenUsage.ModelPricing? entity = await _coreUnitOfWork.Repository<Domain.Entities.TokenUsage.ModelPricing>()
            .SingleOrDefaultAsync(
                m => m.Id == request.Id,
                asNoTracking: false,
                cancellationToken: cancellationToken);

        if (entity is null)
        {
            return Error.NotFound("ModelPricing.NotFound", $"Model pricing with ID '{request.Id}' was not found.");
        }

        _coreUnitOfWork.Repository<Domain.Entities.TokenUsage.ModelPricing>().Remove(entity);
        _ = await _coreUnitOfWork.CompleteAsync();

        return Result.Deleted;
    }
}
