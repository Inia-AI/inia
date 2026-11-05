using ErrorOr;
using IniaAi.Application.Auditing;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Commands.TwoFactor.RemoveAllTrustedDevices;

/// <summary>
/// Handles the command to remove all trusted devices for a user.
/// </summary>
public class RemoveAllTrustedDevicesCommandHandler : IRequestHandler<RemoveAllTrustedDevicesCommand, ErrorOr<Success>>
{
    private readonly IUserDataUnitOfWork _unitOfWork;
    private readonly IAuthenticationAuditLog _audit;

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoveAllTrustedDevicesCommandHandler"/> class.
    /// </summary>
    public RemoveAllTrustedDevicesCommandHandler(
        IUserDataUnitOfWork unitOfWork,
        IAuthenticationAuditLog audit)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    /// <inheritdoc/>
    public async Task<ErrorOr<Success>> Handle(RemoveAllTrustedDevicesCommand request, CancellationToken cancellationToken)
    {
        List<UserTrustedDevice> devices = await _unitOfWork.Repository<UserTrustedDevice>()
            .GetAll()
            .Where(d => d.UserId == request.UserId)
            .ToListAsync(cancellationToken);

        _unitOfWork.Repository<UserTrustedDevice>().RemoveRange(devices);

        _ = await _unitOfWork.CompleteAsync(cancellationToken);

        await _audit.RecordAsync(new AuthenticationAuditEvent(AuthenticationAuditKind.AllTrustedDevicesRemoved, request.UserId, ClientAddress: request.IpAddress), cancellationToken);

        return Result.Success;
    }
}
