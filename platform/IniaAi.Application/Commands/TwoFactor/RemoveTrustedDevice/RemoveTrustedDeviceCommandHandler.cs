using ErrorOr;
using IniaAi.Application.Auditing;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.UserData.Interfaces;

namespace IniaAi.Application.Commands.TwoFactor.RemoveTrustedDevice;

/// <summary>
/// Handles the command to remove a trusted device.
/// </summary>
public class RemoveTrustedDeviceCommandHandler : IRequestHandler<RemoveTrustedDeviceCommand, ErrorOr<Deleted>>
{
    private readonly IUserDataUnitOfWork _unitOfWork;
    private readonly IAuthenticationAuditLog _audit;

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoveTrustedDeviceCommandHandler"/> class.
    /// </summary>
    public RemoveTrustedDeviceCommandHandler(
        IUserDataUnitOfWork unitOfWork,
        IAuthenticationAuditLog audit)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    /// <inheritdoc/>
    public async Task<ErrorOr<Deleted>> Handle(RemoveTrustedDeviceCommand request, CancellationToken cancellationToken)
    {
        UserTrustedDevice? device = await _unitOfWork.Repository<UserTrustedDevice>()
            .SingleOrDefaultAsync(d => d.Id == request.DeviceId && d.UserId == request.UserId, asNoTracking: false, cancellationToken: cancellationToken);

        if (device is null)
        {
            return Error.NotFound("TrustedDevice.NotFound", "Device not found");
        }

        _unitOfWork.Repository<UserTrustedDevice>().Remove(device);

        _ = await _unitOfWork.CompleteAsync(cancellationToken);

        await _audit.RecordAsync(new AuthenticationAuditEvent(AuthenticationAuditKind.TrustedDeviceRemoved, request.UserId, ClientAddress: request.IpAddress), cancellationToken);

        return Result.Deleted;
    }
}
