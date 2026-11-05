using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Domain.Persistence.UserData.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.Signatures.CancelPendingSignatureOverride;

/// <summary>
/// Deletes a pending override on behalf of its proposer. Cascade delete wipes
/// the votes. Anyone else hitting this gets <c>Signatures.NotProposer</c>.
/// </summary>
public class CancelPendingSignatureOverrideCommandHandler
    : IRequestHandler<CancelPendingSignatureOverrideCommand, ErrorOr<Success>>
{
    private const string ERROR_PENDING_NOT_FOUND = "PendingSignatureOverride.NotFound";
    private const string ERROR_NOT_PROPOSER = "Signatures.NotProposer";

    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly ILogger<CancelPendingSignatureOverrideCommandHandler> _logger;

    public CancelPendingSignatureOverrideCommandHandler(
        ICoreUnitOfWork coreUnitOfWork,
        ILogger<CancelPendingSignatureOverrideCommandHandler> logger)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ErrorOr<Success>> Handle(
        CancelPendingSignatureOverrideCommand request,
        CancellationToken cancellationToken)
    {
        IRepository<PendingSignatureOverride> pendingRepo =
            _coreUnitOfWork.Repository<PendingSignatureOverride>();

        PendingSignatureOverride? pending = await pendingRepo
            .GetAll()
            .Where(p => p.Id == request.PendingOverrideId
                && p.OrganizationId == request.OrganizationId)
            .Include(p => p.Votes)
            .FirstOrDefaultAsync(cancellationToken);

        if (pending is null)
        {
            return Error.NotFound(
                ERROR_PENDING_NOT_FOUND,
                $"Pending override '{request.PendingOverrideId}' was not found for this organisation.");
        }

        if (pending.ProposerUserId != request.UserId)
        {
            return Error.Forbidden(
                ERROR_NOT_PROPOSER,
                "Only the original proposer can cancel this pending override.");
        }

        pendingRepo.HardRemove(pending);
        _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);

        _logger.LogInformation(
            "Proposer {UserId} cancelled pending {PendingId} on field {Placeholder}",
            request.UserId,
            pending.Id,
            pending.PlaceholderName);

        return Result.Success;
    }
}
