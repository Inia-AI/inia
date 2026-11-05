using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Errors;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Commands.Counterparties.DeleteCounterparty;

/// <summary>
/// Handles deletion of a counterparty and every counterparty beneath it.
/// </summary>
public class DeleteCounterpartyCommandHandler : IRequestHandler<DeleteCounterpartyCommand, ErrorOr<Deleted>>
{
    private readonly IUserDataUnitOfWork _userDataDbContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteCounterpartyCommandHandler"/> class.
    /// </summary>
    /// <param name="context">Unit of Work.</param>
    public DeleteCounterpartyCommandHandler(IUserDataUnitOfWork context)
    {
        _userDataDbContext = context;
    }

    /// <inheritdoc/>
    public async Task<ErrorOr<Deleted>> Handle(DeleteCounterpartyCommand request, CancellationToken cancellationToken)
    {
        Counterparty? entity = await _userDataDbContext.Repository<Counterparty>()
            .SingleOrDefaultAsync(c => c.Id == request.Id && c.OrganizationId == request.OrganizationId, asNoTracking: false, cancellationToken: cancellationToken);

        if (entity is null)
        {
            return CounterpartyErrorFactory.NotFound(request.Id.ToString());
        }

        List<Counterparty> subtree = [entity];
        HashSet<string> visited = [entity.Id];
        List<string> parentIds = [entity.Id];
        while (parentIds.Count > 0)
        {
            List<Counterparty> children = await _userDataDbContext.Repository<Counterparty>().GetAll()
                .Where(c => c.OrganizationId == request.OrganizationId && c.ParentId != null && parentIds.Contains(c.ParentId))
                .ToListAsync(cancellationToken);
            List<Counterparty> unvisited = [.. children.Where(c => visited.Add(c.Id))];
            subtree.AddRange(unvisited);
            parentIds = [.. unvisited.Select(c => c.Id)];
        }

        _userDataDbContext.Repository<Counterparty>().RemoveRange(subtree);
        _ = await _userDataDbContext.CompleteAsync(cancellationToken);

        return Result.Deleted;
    }
}
