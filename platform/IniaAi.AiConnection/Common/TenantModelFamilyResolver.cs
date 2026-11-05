using IniaAi.AiConnection.AwsBedrock;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ConfigEnums = IniaAi.Configuration.Enums;

namespace IniaAi.AiConnection.Common;

/// <summary>
/// Provides tenant and function-based ModelFamily resolution.
/// Supports both legacy tenant-only routing and new function-specific routing.
/// </summary>
public class TenantModelFamilyResolver
{
    private readonly ModelRoutingSettings _routingSettings;
    private readonly ILogger<TenantModelFamilyResolver> _logger;

    public TenantModelFamilyResolver(
        IOptions<PlatformSettings> platformSettings,
        ILogger<TenantModelFamilyResolver> logger)
    {
        _routingSettings = platformSettings.Value.ModelRouting;
        _logger = logger;
    }

    /// <summary>
    /// Gets the ModelFamily for a given tenant ID and function type.
    /// </summary>
    /// <param name="tenantId">The tenant ID to resolve family for.</param>
    /// <param name="functionType">The type of AI operation being performed.</param>
    /// <returns>The ModelFamily to use for this operation.</returns>
    public ModelFamily GetModelFamilyForFunction(string? tenantId, FunctionType functionType)
    {
        ConfigEnums.FunctionType configFunctionType = ConvertToConfigFunctionType(functionType);
        ConfigEnums.ModelFamily configFamily = _routingSettings.GetModelFamily(tenantId, configFunctionType);
        ModelFamily aiConnectionFamily = ConvertToAiConnectionModelFamily(configFamily);

        _logger.LogTrace(
            "Resolved model family: Tenant={TenantId}, Function={FunctionType}, Family={ModelFamily}",
            tenantId ?? "null", functionType, aiConnectionFamily);

        return aiConnectionFamily;
    }

    /// <summary>
    /// Gets the ModelFamily for a given tenant ID (backward compatibility).
    /// Uses GeneralReasoning as the default function type.
    /// </summary>
    /// <param name="tenantId">The tenant ID to resolve family for.</param>
    /// <returns>The ModelFamily to use for the tenant, defaulting to Anthropic.</returns>
    public ModelFamily GetModelFamilyForTenant(string? tenantId)
    {
        return GetModelFamilyForFunction(tenantId, FunctionType.GeneralReasoning);
    }

    /// <summary>
    /// Gets the model ID override for a given tenant, if configured.
    /// </summary>
    /// <param name="tenantId">The tenant ID to resolve override for.</param>
    /// <param name="shouldUseAdvancedModel">Whether to return the advanced or basic model override.</param>
    /// <returns>Model ID string, or null if no override is configured.</returns>
    public string? GetModelOverride(string? tenantId, bool shouldUseAdvancedModel)
    {
        return _routingSettings.GetModelOverride(tenantId, shouldUseAdvancedModel);
    }

    /// <summary>
    /// Converts Configuration.Enums.ModelFamily to AiConnection.AwsBedrock.ModelFamily.
    /// </summary>
    private static ModelFamily ConvertToAiConnectionModelFamily(ConfigEnums.ModelFamily configFamily)
    {
        return configFamily switch
        {
            ConfigEnums.ModelFamily.Anthropic => ModelFamily.Anthropic,
            ConfigEnums.ModelFamily.Cohere => ModelFamily.Cohere,
            _ => ModelFamily.Anthropic // Default fallback
        };
    }

    /// <summary>
    /// Converts AiConnection.Common.FunctionType to Configuration.Enums.FunctionType.
    /// </summary>
    private static ConfigEnums.FunctionType ConvertToConfigFunctionType(FunctionType aiConnectionType)
    {
        return aiConnectionType switch
        {
            FunctionType.GeneralReasoning => ConfigEnums.FunctionType.GeneralReasoning,
            FunctionType.WorkflowReasoning => ConfigEnums.FunctionType.WorkflowReasoning,
            FunctionType.DocumentOCR => ConfigEnums.FunctionType.DocumentOCR,
            FunctionType.FinancialAnalysis => ConfigEnums.FunctionType.FinancialAnalysis,
            FunctionType.SearchSummarization => ConfigEnums.FunctionType.SearchSummarization,
            FunctionType.Proofreading => ConfigEnums.FunctionType.Proofreading,
            FunctionType.BackgroundProcessing => ConfigEnums.FunctionType.BackgroundProcessing,
            FunctionType.SystemMonitoring => ConfigEnums.FunctionType.SystemMonitoring,
            FunctionType.DocumentProcessing => ConfigEnums.FunctionType.DocumentProcessing,
            FunctionType.BulkProcessing => ConfigEnums.FunctionType.BulkProcessing,
            FunctionType.SpreadsheetAnalysis => ConfigEnums.FunctionType.SpreadsheetAnalysis,
            FunctionType.Validation => ConfigEnums.FunctionType.Validation,
            _ => ConfigEnums.FunctionType.GeneralReasoning // Default fallback
        };
    }
}
