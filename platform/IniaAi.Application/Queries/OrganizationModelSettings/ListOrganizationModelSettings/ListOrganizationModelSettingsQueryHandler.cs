using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.OData;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.TenantModelSettings;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.OrganizationModelSettings.ListOrganizationModelSettings;

/// <summary>
/// Handles the query to retrieve a paginated and OData-compliant list of organization model settings.
/// </summary>
public class ListOrganizationModelSettingsQueryHandler : IRequestHandler<ListOrganizationModelSettingsQuery, ErrorOr<ODataPaginatedResponse<TenantModelSettingsListItemDto>>>
{
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly ICoreUnitOfWork _coreUnitOfWork;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListOrganizationModelSettingsQueryHandler"/> class.
    /// </summary>
    /// <param name="userDataUnitOfWork">The user data unit of work for repository access.</param>
    /// <param name="coreUnitOfWork">The core unit of work for model pricing access.</param>
    public ListOrganizationModelSettingsQueryHandler(
        IUserDataUnitOfWork userDataUnitOfWork,
        ICoreUnitOfWork coreUnitOfWork)
    {
        _userDataUnitOfWork = userDataUnitOfWork ?? throw new ArgumentNullException(nameof(userDataUnitOfWork));
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
    }

    /// <summary>
    /// Handles the query request to return paginated organization model settings.
    /// </summary>
    /// <param name="request">The request containing OData query options.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A paginated OData response containing tenant model settings DTOs.</returns>
    public async Task<ErrorOr<ODataPaginatedResponse<TenantModelSettingsListItemDto>>> Handle(ListOrganizationModelSettingsQuery request, CancellationToken cancellationToken)
    {
        ODataPaginatedResponse<TenantModelSettingsListItemDto> page = await _userDataUnitOfWork
            .Repository<Domain.Entities.TokenUsage.OrganizationModelSettings>()
            .GetAll()
            .Select(OrganizationModelSettingsProjections.ToListItem)
            .ToODataPageAsync(request.QueryOptions, cancellationToken);

        // Batch fetch ModelPricing display names from Core database
        var modelPricingIds = page.Value.Select(x => x.ModelPricingId).Distinct().ToList();
        var modelPricings = await _coreUnitOfWork.Repository<Domain.Entities.TokenUsage.ModelPricing>()
            .GetAll()
            .Where(m => modelPricingIds.Contains(m.Id))
            .Select(m => new { m.Id, m.DisplayName })
            .ToListAsync(cancellationToken);

        var modelPricingLookup = modelPricings.ToDictionary(m => m.Id, m => m.DisplayName);

        // Populate model display names
        foreach (TenantModelSettingsListItemDto item in page.Value)
        {
            if (modelPricingLookup.TryGetValue(item.ModelPricingId, out string? displayName))
            {
                item.ModelDisplayName = displayName;
            }
        }

        return page;
    }
}
