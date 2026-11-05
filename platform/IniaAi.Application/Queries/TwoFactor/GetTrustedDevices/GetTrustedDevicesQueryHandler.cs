using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.TwoFactor;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.TwoFactor.GetTrustedDevices;

/// <summary>
/// Handles the query to get the list of trusted devices for a user.
/// </summary>
public class GetTrustedDevicesQueryHandler : IRequestHandler<GetTrustedDevicesQuery, ErrorOr<List<TrustedDeviceResponse>>>
{
    private readonly IUserDataUnitOfWork _unitOfWork;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetTrustedDevicesQueryHandler"/> class.
    /// </summary>
    /// <param name="unitOfWork">The unit of work.</param>
    public GetTrustedDevicesQueryHandler(IUserDataUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <inheritdoc/>
    public async Task<ErrorOr<List<TrustedDeviceResponse>>> Handle(GetTrustedDevicesQuery request, CancellationToken cancellationToken)
    {
        List<TrustedDeviceResponse> devices = await _unitOfWork.Repository<UserTrustedDevice>()
            .GetAll()
            .AsNoTracking()
            .Where(d => d.UserId == request.UserId && d.TrustedUntil > DateTime.UtcNow)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new TrustedDeviceResponse
            {
                Id = d.Id,
                DeviceName = d.DeviceName,
                CreatedAt = d.CreatedAt,
                TrustedUntil = d.TrustedUntil,
                IsCurrent = false, // Would need to compare with current device fingerprint
            })
            .ToListAsync(cancellationToken);

        return devices;
    }
}
