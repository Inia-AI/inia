using IniaAi.WebApi.Contracts.Responses.WorkflowVersions;
using System.Text.Json;

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

/// <summary>
/// Reads a deployment report file for the compare page. Any JSON object binds to the report record,
/// with every member it lacks left null despite the record's non-nullable types, and the comparison
/// cannot read such a report. So a file is a report only when it carries what the comparison reads.
/// </summary>
public static class DeploymentReportFile
{
    public static CurrencyCardDto? Read(string json, JsonSerializerOptions options)
    {
        CurrencyCardDto? report;
        try
        {
            report = JsonSerializer.Deserialize<CurrencyCardDto>(json, options);
        }
        catch (JsonException)
        {
            return null;
        }

        return report is not null && CarriesWhatTheComparisonReads(report) ? report : null;
    }

    private static bool CarriesWhatTheComparisonReads(CurrencyCardDto report)
    {
        return !string.IsNullOrWhiteSpace(report.InstanceName)
            && report.Workflows is not null
            && report.Workflows.All(entry => entry is not null
                && !string.IsNullOrWhiteSpace(entry.WorkflowTypeId)
                && entry.ContentHash is not null
                && entry.HistoryHashes is not null);
    }
}
