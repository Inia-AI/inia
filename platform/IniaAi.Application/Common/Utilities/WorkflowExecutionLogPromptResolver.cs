using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Common.Utilities;

/// <summary>
/// Utility class for resolving workflow execution log prompts and extracting placeholder values.
/// </summary>
public static class WorkflowExecutionLogPromptResolver
{
    /// <summary>
    /// Gets the resolved prompt from a workflow execution log, handling InputContext replacement and fallbacks.
    /// </summary>
    /// <param name="log">The workflow execution log.</param>
    /// <returns>The resolved prompt string.</returns>
    public static string GetResolvedPrompt(WorkflowExecutionLog log)
    {
        string? resolved = null;

        var aiProps = log.Block?.Properties as AiQueryProperties;

        if (aiProps is not null
            && !string.IsNullOrEmpty(aiProps.ExecutionState?.InputContext)
            && !string.IsNullOrEmpty(aiProps.ExecutionState?.UserPrompt))
        {
            resolved = aiProps.ExecutionState.UserPrompt.Replace(
                "{InputContext}",
                aiProps.ExecutionState.InputContext,
                StringComparison.OrdinalIgnoreCase);
        }

        return resolved
            ?? aiProps?.ExecutionState?.UserPrompt
            ?? log.Prompt
            ?? string.Empty;
    }

    /// <summary>
    /// Extracts placeholder values from a template using the resolved prompt.
    /// </summary>
    /// <param name="template">The template containing placeholders.</param>
    /// <param name="resolved">The resolved prompt text.</param>
    /// <param name="logId">The log ID for error logging.</param>
    /// <param name="logger">The logger instance.</param>
    /// <returns>A dictionary of placeholder values, or empty dictionary if extraction fails.</returns>
    public static Dictionary<string, string> ExtractPlaceholderValues(
        string template,
        string resolved,
        string logId,
        ILogger logger)
    {
        if (string.IsNullOrEmpty(template))
        {
            return [];
        }

        try
        {
            return PlaceholderExtractor.ExtractPlaceholderValues(template, resolved);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error extracting placeholder values from template '{Template}' for log ID '{LogId}': {ExceptionMessage}",
                template,
                logId,
                ex.Message);

            return [];
        }
    }

    /// <summary>
    /// Extracts placeholder values from a template using the resolved prompt and creates enriched DTOs with truncation.
    /// </summary>
    /// <param name="template">The template containing placeholders.</param>
    /// <param name="resolved">The resolved prompt text.</param>
    /// <param name="logId">The log ID for error logging.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="maxLength">The maximum length before truncation (default: 150).</param>
    /// <returns>A dictionary of enriched placeholder values, or null if extraction fails or template is empty.</returns>
    public static Dictionary<string, PlaceholderValueDto>? ExtractEnrichedPlaceholderValues(
        string template,
        string resolved,
        string logId,
        ILogger logger,
        int maxLength = 150)
    {
        if (string.IsNullOrEmpty(template))
        {
            return null;
        }

        try
        {
            Dictionary<string, string> inferredValues = PlaceholderExtractor.ExtractPlaceholderValues(template, resolved);

            return inferredValues.ToDictionary(
                kvp => kvp.Key,
                kvp =>
                {
                    string val = kvp.Value;
                    bool isTruncated = val.Length > maxLength;

                    return new PlaceholderValueDto
                    {
                        Value = isTruncated ? val[..maxLength] + "…" : val,
                        Truncated = isTruncated,
                        Translated = false,
                    };
                });
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error extracting placeholder values from template '{Template}' for log ID '{LogId}': {ExceptionMessage}",
                template,
                logId,
                ex.Message);

            return null;
        }
    }

    /// <summary>
    /// Filters a dictionary of placeholder values to only include specified keys.
    /// </summary>
    /// <param name="placeholderValues">The original placeholder values dictionary.</param>
    /// <param name="keysToInclude">The keys to include in the filtered result.</param>
    /// <returns>A filtered dictionary containing only the specified keys.</returns>
    public static Dictionary<string, string> FilterPlaceholderValues(
        Dictionary<string, string> placeholderValues,
        IEnumerable<string> keysToInclude)
    {
        if (placeholderValues is null || placeholderValues.Count == 0 || keysToInclude is null)
        {
            return placeholderValues ?? [];
        }

        var keysSet = new HashSet<string>(keysToInclude, StringComparer.OrdinalIgnoreCase);

        return placeholderValues
            .Where(kvp => keysSet.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>
    /// Filters a dictionary of enriched placeholder values to only include specified keys.
    /// </summary>
    /// <param name="placeholderValues">The original enriched placeholder values dictionary.</param>
    /// <param name="keysToInclude">The keys to include in the filtered result.</param>
    /// <returns>A filtered dictionary containing only the specified keys.</returns>
    public static Dictionary<string, PlaceholderValueDto>? FilterEnrichedPlaceholderValues(
        Dictionary<string, PlaceholderValueDto>? placeholderValues,
        IEnumerable<string> keysToInclude)
    {
        if (placeholderValues is null || placeholderValues.Count == 0 || keysToInclude is null)
        {
            return placeholderValues;
        }

        var keysSet = new HashSet<string>(keysToInclude, StringComparer.OrdinalIgnoreCase);

        return placeholderValues
            .Where(kvp => keysSet.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }
}
