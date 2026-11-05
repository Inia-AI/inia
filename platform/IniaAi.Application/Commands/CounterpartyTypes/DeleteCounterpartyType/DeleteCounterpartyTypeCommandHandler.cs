using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Errors;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.CounterpartyTypes.DeleteCounterpartyType;

/// <summary>
/// Handles the deletion of a counterparty type.
/// </summary>
public class DeleteCounterpartyTypeCommandHandler : IRequestHandler<DeleteCounterpartyTypeCommand, ErrorOr<Deleted>>
{
    private readonly IUserDataUnitOfWork _unitOfWork;
    private readonly ILogger<DeleteCounterpartyTypeCommandHandler> _logger;
    private readonly BuiltInCounterpartyTypes _builtIn;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteCounterpartyTypeCommandHandler"/> class.
    /// </summary>
    /// <param name="unitOfWork">The user data unit of work.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="builtIn">The counterparty types the deployment seeds and protects.</param>
    public DeleteCounterpartyTypeCommandHandler(IUserDataUnitOfWork unitOfWork, ILogger<DeleteCounterpartyTypeCommandHandler> logger, BuiltInCounterpartyTypes builtIn)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _builtIn = builtIn;
    }

    /// <inheritdoc/>
    public async Task<ErrorOr<Deleted>> Handle(DeleteCounterpartyTypeCommand request, CancellationToken cancellationToken)
    {
        CounterpartyType? counterpartyType = await _unitOfWork.Repository<CounterpartyType>()
            .SingleOrDefaultAsync(t => t.Id == request.Id, asNoTracking: false, cancellationToken: cancellationToken);

        if (counterpartyType is null)
        {
            return CounterpartyTypeErrorFactory.NotFound(request.Id);
        }

        if (_builtIn.Contains(counterpartyType.Name))
        {
            return CounterpartyTypeErrorFactory.BuiltInDeleted(counterpartyType.Name);
        }

        _unitOfWork.Repository<CounterpartyType>().Remove(counterpartyType);
        _ = await _unitOfWork.CompleteAsync(cancellationToken);

        _logger.LogInformation("User {UserId} deleted counterparty type {CounterpartyTypeId}", request.UserId, counterpartyType.Id);

        return Result.Deleted;
    }
}
