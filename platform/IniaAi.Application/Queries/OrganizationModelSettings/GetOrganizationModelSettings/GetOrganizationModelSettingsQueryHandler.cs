using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.TenantModelSettings;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.OrganizationModelSettings.GetOrganizationModelSettings;

/// <summary>
/// Handles retrieval of an organization model settings entry by ID.
/// </summary>
public class GetOrganizationModelSettingsQueryHandler : IRequestHandler<GetOrganizationModelSettingsQuery, ErrorOr<TenantModelSettingsDto>>
{
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly ICoreUnitOfWork _coreUnitOfWork;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetOrganizationModelSettingsQueryHandler"/> class.
    /// </summary>
    /// <param name="userDataUnitOfWork">The user data unit of work.</param>
    /// <param name="coreUnitOfWork">The core unit of work.</param>
    public GetOrganizationModelSettingsQueryHandler(
        IUserDataUnitOfWork userDataUnitOfWork,
        ICoreUnitOfWork coreUnitOfWork)
    {
        _userDataUnitOfWork = userDataUnitOfWork;
        _coreUnitOfWork = coreUnitOfWork;
    }

    /// <inheritdoc/>
    public async Task<ErrorOr<TenantModelSettingsDto>> Handle(GetOrganizationModelSettingsQuery request, CancellationToken cancellationToken)
    {
        Domain.Entities.TokenUsage.OrganizationModelSettings? entity = await _userDataUnitOfWork.Repository<Domain.Entities.TokenUsage.OrganizationModelSettings>()
            .GetAll()
            .Include(x => x.Organization)
            .FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken);

        if (entity is null)
        {
            return Error.NotFound("OrganizationModelSettings.NotFound", $"Organization model settings with ID '{request.Id}' was not found.");
        }

        // Fetch ModelPricing from Core database
        Domain.Entities.TokenUsage.ModelPricing? modelPricing = await _coreUnitOfWork.Repository<Domain.Entities.TokenUsage.ModelPricing>()
            .SingleOrDefaultAsync(
                m => m.Id == entity.ModelPricingId,
                asNoTracking: true,
                cancellationToken: cancellationToken);

        var dto = new TenantModelSettingsDto
        {
            Id = entity.Id,
            TenantId = entity.OrganizationId,
            TenantName = entity.Organization?.Name,
            UsageContext = (WebApi.Contracts.Enums.UsageContext)entity.UsageContext,
            ModelPricingId = entity.ModelPricingId,
            ModelDisplayName = modelPricing?.DisplayName ?? "Unknown",
            IsEnabled = entity.IsEnabled,
            Priority = entity.Priority,
            CreatedAt = entity.CreatedAt,
        };

        return dto;
    }
}
