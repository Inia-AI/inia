using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.StoredKeys;
using IniaAi.Domain.StoredKeys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.StoredKeys.SetStoredKey;

public class SetStoredKeyCommandHandler : IRequestHandler<SetStoredKeyCommand, ErrorOr<Success>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly IStoredKeyCipher _cipher;
    private readonly ILogger<SetStoredKeyCommandHandler> _logger;

    public SetStoredKeyCommandHandler(
        ICoreUnitOfWork coreUnitOfWork,
        IStoredKeyCipher cipher,
        ILogger<SetStoredKeyCommandHandler> logger)
    {
        _coreUnitOfWork = coreUnitOfWork;
        _cipher = cipher;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(SetStoredKeyCommand request, CancellationToken cancellationToken)
    {
        OrganizationStoredKey? storedKey = await _coreUnitOfWork.Repository<OrganizationStoredKey>()
            .GetAll()
            .FirstOrDefaultAsync(
                key => key.OrganizationId == request.OrganizationId && key.Name == request.Name,
                cancellationToken);

        if (storedKey is null)
        {
            storedKey = new OrganizationStoredKey
            {
                OrganizationId = request.OrganizationId,
                Name = request.Name,
            };
            _coreUnitOfWork.Repository<OrganizationStoredKey>().Add(storedKey);
        }

        storedKey.ProtectedValue = _cipher.Protect(request.Value.Trim());
        storedKey.UpdatedAt = DateTime.UtcNow;
        storedKey.UpdatedByUserId = request.UserId;

        _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);

        _logger.LogInformation(
            "User {UserId} set stored key {Name} for organization {OrganizationId}",
            request.UserId,
            request.Name,
            request.OrganizationId);

        return Result.Success;
    }
}
