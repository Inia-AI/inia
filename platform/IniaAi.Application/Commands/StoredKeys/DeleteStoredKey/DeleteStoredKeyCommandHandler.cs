using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.StoredKeys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.StoredKeys.DeleteStoredKey;

public class DeleteStoredKeyCommandHandler : IRequestHandler<DeleteStoredKeyCommand, ErrorOr<Deleted>>
{
    private const string ERROR_NOT_FOUND = "StoredKey.NotFound";

    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly ILogger<DeleteStoredKeyCommandHandler> _logger;

    public DeleteStoredKeyCommandHandler(
        ICoreUnitOfWork coreUnitOfWork,
        ILogger<DeleteStoredKeyCommandHandler> logger)
    {
        _coreUnitOfWork = coreUnitOfWork;
        _logger = logger;
    }

    public async Task<ErrorOr<Deleted>> Handle(DeleteStoredKeyCommand request, CancellationToken cancellationToken)
    {
        OrganizationStoredKey? storedKey = await _coreUnitOfWork.Repository<OrganizationStoredKey>()
            .GetAll()
            .FirstOrDefaultAsync(
                key => key.OrganizationId == request.OrganizationId && key.Name == request.Name,
                cancellationToken);

        if (storedKey is null)
        {
            return Error.NotFound(ERROR_NOT_FOUND, $"This organization has no stored key named '{request.Name}'.");
        }

        // A hard delete: the encrypted value leaves the database, and the name is free to add again.
        _coreUnitOfWork.Repository<OrganizationStoredKey>().HardRemove(storedKey);
        _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);

        _logger.LogInformation(
            "User {UserId} deleted stored key {Name} for organization {OrganizationId}",
            request.UserId,
            request.Name,
            request.OrganizationId);

        return Result.Deleted;
    }
}
