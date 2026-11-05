using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.WebApi.Contracts.Responses.TenantModelSettings;

namespace IniaAi.Application.Commands.OrganizationModelSettings.CreateOrganizationModelSettings;

/// <summary>
/// Command to create a new organization model settings entry.
/// </summary>
/// <param name="OrganizationId">The organization this setting applies to. NULL indicates a global default.</param>
/// <param name="UsageContext">The usage context this setting applies to (e.g., BlockExecution, Embedding).</param>
/// <param name="ModelPricingId">The model pricing record to use for this context.</param>
/// <param name="IsEnabled">Whether this setting is currently enabled.</param>
/// <param name="Priority">Priority for ordering when multiple settings exist. Higher takes precedence.</param>
public record CreateOrganizationModelSettingsCommand(
    string? OrganizationId,
    UsageContext UsageContext,
    string ModelPricingId,
    bool IsEnabled,
    int Priority) : IRequest<ErrorOr<TenantModelSettingsDto>>;
