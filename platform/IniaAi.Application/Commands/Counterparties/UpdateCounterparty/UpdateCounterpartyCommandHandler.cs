using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Persistence;
using IniaAi.Application.Errors;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Commands.Counterparties.UpdateCounterparty;

/// <summary>
/// Handles updates to an existing counterparty.
/// </summary>
public class UpdateCounterpartyCommandHandler : IRequestHandler<UpdateCounterpartyCommand, ErrorOr<Updated>>
{
    private readonly IUserDataUnitOfWork _userDataDbContext;
    private readonly IUserDataDbContext _context;
    private readonly IDbExceptionAnalyzer _exceptionAnalyzer;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateCounterpartyCommandHandler"/> class.
    /// </summary>
    /// <param name="unitOfWork">The unit of Work.</param>
    /// <param name="context">The database context.</param>
    /// <param name="exceptionAnalyzer">The database exception analyzer.</param>
    public UpdateCounterpartyCommandHandler(IUserDataUnitOfWork unitOfWork, IUserDataDbContext context, IDbExceptionAnalyzer exceptionAnalyzer)
    {
        _userDataDbContext = unitOfWork;
        _context = context;
        _exceptionAnalyzer = exceptionAnalyzer;
    }

    /// <inheritdoc/>
    public async Task<ErrorOr<Updated>> Handle(UpdateCounterpartyCommand request, CancellationToken cancellationToken)
    {
        Counterparty? entity = await _context.Counterparties
            .Include(c => c.CounterpartyTypes)
            .FirstOrDefaultAsync(c => c.Id == request.Id && c.OrganizationId == request.OrganizationId, cancellationToken);

        if (entity is null)
        {
            return CounterpartyErrorFactory.NotFound(request.Id.ToString());
        }

        if (!string.IsNullOrEmpty(request.ParentId)
            && !await _context.Counterparties.AnyAsync(parent => parent.Id == request.ParentId && parent.OrganizationId == request.OrganizationId, cancellationToken))
        {
            return CounterpartyErrorFactory.ParentNotFound(request.ParentId);
        }

        entity.ParentId = request.ParentId;
        CounterpartyFields.CopyText(request, entity);

        // Update many-to-many CounterpartyTypes
        entity.CounterpartyTypes.Clear();
        if (request.CounterpartyTypeIds is { Count: > 0 })
        {
            IEnumerable<CounterpartyType> types = await _userDataDbContext.Repository<CounterpartyType>()
                .FindAsync(t => request.CounterpartyTypeIds.Contains(t.Id));

            foreach (CounterpartyType type in types)
            {
                entity.CounterpartyTypes.Add(type);
            }
        }

        CounterpartyPropertiesWriter.Apply(entity, request.Properties);

        try
        {
            _ = await _userDataDbContext.CompleteAsync(cancellationToken);
            return Result.Updated;
        }
        catch (Exception ex) when (_exceptionAnalyzer.IsCounterpartyNameDuplicate(ex))
        {
            return CounterpartyErrorFactory.DuplicateName(
                request.Name,
                request.OrganizationId ?? "unknown");
        }
    }
}
