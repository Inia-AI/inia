using System.Globalization;
using IniaAi.Application.Workflows.EditorAgent;
using IniaAi.Common.Constants;
using IniaAi.Domain.Entities.ReportFeedback;

namespace IniaAi.WebApp.Components.WorkflowEditor.Agent;

/// <summary>Prices and costs as the conversation panel shows them.</summary>
public static class EditorAgentText
{
    public const string NO_PRICE = "Price not set";
    public const string RECOMMENDED = "Recommended";
    public const string NO_COUNTERPARTY = "No " + CounterpartyTerms.SINGULAR_LOWER;

    private const decimal SMALLEST_SHOWN_COST = 0.01m;

    public static string Cost(decimal usd)
    {
        if (usd <= 0m)
        {
            return "$0.00";
        }

        return usd < SMALLEST_SHOWN_COST
            ? "under $0.01"
            : usd.ToString("$#,0.00", CultureInfo.InvariantCulture);
    }

    /// <summary>The model's name without the provider it runs through, such as "Claude Sonnet 5".</summary>
    public static string ModelName(EditorAgentModelOption model)
    {
        string name = model.DisplayName.Trim();
        int provider = name.LastIndexOf(" (", StringComparison.Ordinal);
        return provider > 0 && name.EndsWith(')') ? name[..provider].TrimEnd() : name;
    }

    /// <summary>
    /// The model's name in the menu: "Advanced · Claude Sonnet 5.5" for one of the platform's
    /// default models, otherwise its name, with the default marked as recommended.
    /// </summary>
    public static string ModelMenuName(EditorAgentModelOption model)
    {
        if (model.Tier is not null)
        {
            return $"{model.Tier} · {ModelName(model)}";
        }

        return model.IsDefault ? $"{ModelName(model)} · {RECOMMENDED}" : ModelName(model);
    }

    /// <summary>
    /// A past report as the picker and its chips name it:
    /// "Corp Part 1 - Type A · 6 Oct 2026 · Acme Holdings · Completed".
    /// </summary>
    public static string ReportLabel(EditorAgentReportSummary report)
    {
        string counterparty = string.IsNullOrWhiteSpace(report.Counterparty) ? NO_COUNTERPARTY : report.Counterparty;
        string label = string.Create(CultureInfo.InvariantCulture, $"{report.Date:d MMM yyyy} · {counterparty} · {ReportStatus(report.Status)}");

        return string.IsNullOrWhiteSpace(report.WorkflowTypeName) ? label : $"{report.WorkflowTypeName} · {label}";
    }

    /// <summary>A report status as the picker shows it: "Completed".</summary>
    public static string ReportStatus(string status)
    {
        return status.Length == 0 ? status : char.ToUpperInvariant(status[0]) + status[1..];
    }

    public static string Price(EditorAgentModelOption model)
    {
        if (model.InputUsdPerMillionTokens is not decimal input || model.OutputUsdPerMillionTokens is not decimal output)
        {
            return NO_PRICE;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"${input:0.00} reading, ${output:0.00} writing, per million tokens");
    }

    /// <summary>The feedback a message was sent with: "1 feedback item", "7 feedback items".</summary>
    public static string FeedbackCount(int count)
    {
        return count == 1 ? "1 feedback item" : string.Create(CultureInfo.InvariantCulture, $"{count} feedback items");
    }

    /// <summary>A feedback item's status once it is no longer open: "Dismissed", "Addressed".</summary>
    public static string FeedbackStatus(ReportFeedbackStatus status)
    {
        return status.ToString();
    }
}
