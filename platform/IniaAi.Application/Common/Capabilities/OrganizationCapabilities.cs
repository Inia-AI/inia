using System.Collections.Frozen;

namespace IniaAi.Application.Common.Capabilities;

/// <summary>
/// The neutral names of what an organization may do. The server checks them on every request, and
/// the Vue app reads the same names from the tenants read to hide what is missing.
/// </summary>
public static class OrganizationCapabilities
{
    public const string RUN = "run";
    public const string UPLOAD = "upload";
    public const string EXPLORE = "explore";
    public const string REFINE = "refine";
    public const string RUN_LOGS = "run-logs";
    public const string RUN_EVIDENCE = "run-evidence";
    public const string USAGE_COST = "usage-cost";
    public const string BASELINES = "baselines";
    public const string VALIDATION = "validation";
    public const string REPORT_TEMPLATES = "report-templates";
    public const string STANDALONE_PROCESSING = "standalone-processing";
    public const string TEAM_VIEW = "team-view";
    public const string SIGNATURES = "signatures";
    public const string MONITORING = "monitoring";
    public const string ADMIN_APP = "admin-app";

    public static readonly FrozenSet<string> All = FrozenSet.ToFrozenSet(
        [
            RUN,
            UPLOAD,
            EXPLORE,
            REFINE,
            RUN_LOGS,
            RUN_EVIDENCE,
            USAGE_COST,
            BASELINES,
            VALIDATION,
            REPORT_TEMPLATES,
            STANDALONE_PROCESSING,
            TEAM_VIEW,
            SIGNATURES,
            MONITORING,
            ADMIN_APP,
        ],
        StringComparer.Ordinal);
}
