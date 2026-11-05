using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Errors;
using IniaAi.Common;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.CounterpartyTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.CounterpartyTypes.UpdateCounterpartyType;

/// <summary>
/// Handles a change to the name and description of a counterparty type.
/// </summary>
public class UpdateCounterpartyTypeCommandHandler : IRequestHandler<UpdateCounterpartyTypeCommand, ErrorOr<CounterpartyTypeListItemDto>>
{
    private readonly IUserDataUnitOfWork _unitOfWork;
    private readonly ILogger<UpdateCounterpartyTypeCommandHandler> _logger;
    private readonly BuiltInCounterpartyTypes _builtIn;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateCounterpartyTypeCommandHandler"/> class.
    /// </summary>
    /// <param name="unitOfWork">The user data unit of work.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="builtIn">The counterparty types the deployment seeds and protects.</param>
    public UpdateCounterpartyTypeCommandHandler(IUserDataUnitOfWork unitOfWork, ILogger<UpdateCounterpartyTypeCommandHandler> logger, BuiltInCounterpartyTypes builtIn)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _builtIn = builtIn;
    }

    /// <inheritdoc/>
    public async Task<ErrorOr<CounterpartyTypeListItemDto>> Handle(UpdateCounterpartyTypeCommand request, CancellationToken cancellationToken)
    {
        CounterpartyType? counterpartyType = await _unitOfWork.Repository<CounterpartyType>()
            .SingleOrDefaultAsync(t => t.Id == request.Id, asNoTracking: false, cancellationToken: cancellationToken);

        if (counterpartyType is null)
        {
            return CounterpartyTypeErrorFactory.NotFound(request.Id);
        }

        string name = request.Name.Trim();

        if (_builtIn.Contains(counterpartyType.Name) && name != counterpartyType.Name)
        {
            return CounterpartyTypeErrorFactory.BuiltInRenamed(counterpartyType.Name);
        }

        if (await CounterpartyTypeRules.NameIsTakenAsync(_unitOfWork, name, exceptId: counterpartyType.Id, cancellationToken))
        {
            return CounterpartyTypeErrorFactory.DuplicateName(name);
        }

        counterpartyType.Name = name;
        counterpartyType.Description = request.Description.NullIfWhiteSpace();
        counterpartyType.UpdatedAt = DateTime.UtcNow;
        counterpartyType.UpdatedByUserId = request.UserId;
        _ = await _unitOfWork.CompleteAsync(cancellationToken);

        _logger.LogInformation("User {UserId} changed counterparty type {CounterpartyTypeId}", request.UserId, counterpartyType.Id);

        int counterpartyCount = await _unitOfWork.Repository<Counterparty>().GetAll()
            .CountAsync(c => c.OrganizationId == request.OrganizationId && c.CounterpartyTypes.Any(t => t.Id == counterpartyType.Id), cancellationToken);

        return CounterpartyTypeRules.ToDto(counterpartyType, counterpartyCount, _builtIn);
    }
}
