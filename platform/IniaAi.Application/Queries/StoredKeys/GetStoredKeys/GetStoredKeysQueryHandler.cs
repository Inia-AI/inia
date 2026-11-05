using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.StoredKeys;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.StoredKeys.GetStoredKeys;

public class GetStoredKeysQueryHandler : IRequestHandler<GetStoredKeysQuery, ErrorOr<IReadOnlyList<StoredKeySummary>>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;

    public GetStoredKeysQueryHandler(ICoreUnitOfWork coreUnitOfWork)
    {
        _coreUnitOfWork = coreUnitOfWork;
    }

    public async Task<ErrorOr<IReadOnlyList<StoredKeySummary>>> Handle(
        GetStoredKeysQuery request,
        CancellationToken cancellationToken)
    {
        List<StoredKeySummary> storedKeys = await _coreUnitOfWork.Repository<OrganizationStoredKey>()
            .GetAll()
            .AsNoTracking()
            .Where(key => key.OrganizationId == request.OrganizationId)
            .OrderBy(key => key.Name)
            .Select(key => new StoredKeySummary(key.Name, key.UpdatedAt ?? key.CreatedAt))
            .ToListAsync(cancellationToken);

        return storedKeys;
    }
}
