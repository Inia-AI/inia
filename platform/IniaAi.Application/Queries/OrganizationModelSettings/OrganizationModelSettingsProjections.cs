using IniaAi.WebApi.Contracts.Responses.TenantModelSettings;
using System.Linq.Expressions;
using ContractUsageContext = IniaAi.WebApi.Contracts.Enums.UsageContext;
using DomainUsageContext = IniaAi.Domain.Entities.TokenUsage.Enums.UsageContext;
using SettingsEntity = IniaAi.Domain.Entities.TokenUsage.OrganizationModelSettings;

namespace IniaAi.Application.Queries.OrganizationModelSettings;

/// <summary>
/// The model's display name lives in the Core database, so the list handler fills it in after
/// this projection runs against user data.
/// </summary>
public static class OrganizationModelSettingsProjections
{
    public const string GLOBAL_DEFAULT_TENANT_NAME = "Global Default";

    public static readonly Expression<Func<SettingsEntity, TenantModelSettingsListItemDto>> ToListItem = settings => new TenantModelSettingsListItemDto
    {
        Id = settings.Id,
        TenantId = settings.OrganizationId,
        TenantDisplayName = settings.Organization != null ? settings.Organization.Name : GLOBAL_DEFAULT_TENANT_NAME,
        UsageContext = ToContract(settings.UsageContext),
        ModelPricingId = settings.ModelPricingId,
        IsEnabled = settings.IsEnabled,
        Priority = settings.Priority,
    };

    /// <summary>
    /// The usage context is stored as its name. A cast inside the expression becomes a SQL cast
    /// of that name to a number, which both providers refuse, so the conversion runs after the
    /// database returns the row.
    /// </summary>
    private static ContractUsageContext ToContract(DomainUsageContext context)
    {
        return (ContractUsageContext)context;
    }
}
