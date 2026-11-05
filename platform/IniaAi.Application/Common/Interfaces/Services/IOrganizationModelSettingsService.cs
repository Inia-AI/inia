using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.TokenUsage.Enums;

namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// Provides model resolution for different usage contexts based on organization settings.
/// </summary>
public interface IOrganizationModelSettingsService
{
    /// <summary>
    /// Gets the model pricing record for a specific organization and usage context.
    /// Falls back to global defaults if no organization-specific setting exists.
    /// </summary>
    /// <param name="organizationId">The organization ID, or null to only check global settings.</param>
    /// <param name="context">The usage context (e.g., BlockExecution, Embedding).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The model pricing record, or null if no setting is configured.</returns>
    Task<ModelPricing?> GetModelForContextAsync(string? organizationId, UsageContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the model pricing ID for a specific organization and usage context.
    /// Falls back to global defaults if no organization-specific setting exists.
    /// </summary>
    /// <param name="organizationId">The organization ID, or null to only check global settings.</param>
    /// <param name="context">The usage context (e.g., BlockExecution, Embedding).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The model pricing ID, or null if no setting is configured.</returns>
    Task<string?> GetModelIdForContextAsync(string? organizationId, UsageContext context, CancellationToken cancellationToken = default);
}
