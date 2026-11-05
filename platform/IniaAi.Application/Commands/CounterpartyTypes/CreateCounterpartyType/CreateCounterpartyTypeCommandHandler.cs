using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Errors;
using IniaAi.Common;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.CounterpartyTypes;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.CounterpartyTypes.CreateCounterpartyType;

/// <summary>
/// Handles the creation of a counterparty type.
/// </summary>
public class CreateCounterpartyTypeCommandHandler : IRequestHandler<CreateCounterpartyTypeCommand, ErrorOr<CounterpartyTypeListItemDto>>
{
    private readonly IUserDataUnitOfWork _unitOfWork;
    private readonly ILogger<CreateCounterpartyTypeCommandHandler> _logger;
    private readonly BuiltInCounterpartyTypes _builtIn;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateCounterpartyTypeCommandHandler"/> class.
    /// </summary>
    /// <param name="unitOfWork">The user data unit of work.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="builtIn">The counterparty types the deployment seeds and protects.</param>
    public CreateCounterpartyTypeCommandHandler(IUserDataUnitOfWork unitOfWork, ILogger<CreateCounterpartyTypeCommandHandler> logger, BuiltInCounterpartyTypes builtIn)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _builtIn = builtIn;
    }

    /// <inheritdoc/>
    public async Task<ErrorOr<CounterpartyTypeListItemDto>> Handle(CreateCounterpartyTypeCommand request, CancellationToken cancellationToken)
    {
        string name = request.Name.Trim();

        if (await CounterpartyTypeRules.NameIsTakenAsync(_unitOfWork, name, exceptId: null, cancellationToken))
        {
            return CounterpartyTypeErrorFactory.DuplicateName(name);
        }

        var counterpartyType = new CounterpartyType(name)
        {
            Description = request.Description.NullIfWhiteSpace(),
        };

        await _unitOfWork.Repository<CounterpartyType>().AddAsync(counterpartyType);
        _ = await _unitOfWork.CompleteAsync(cancellationToken);

        _logger.LogInformation("User {UserId} created counterparty type {CounterpartyTypeId}", request.UserId, counterpartyType.Id);

        return CounterpartyTypeRules.ToDto(counterpartyType, counterpartyCount: 0, _builtIn);
    }
}
