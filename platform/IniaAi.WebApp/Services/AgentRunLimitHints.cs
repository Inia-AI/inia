using IniaAi.Common.Interfaces.Services.AgentRuntime;
using System.Globalization;

namespace IniaAi.WebApp.Services;

/// <summary>
/// The run-limit input ceilings and empty-state placeholders the agent-family block editors quote
/// from the runtime's limits. A snapshot at first render is enough: limits move with runtime
/// deployments, not while an editor sits open, and the save-time guard (not this hint) is what
/// refuses an over-ceiling value. LastKnown deliberately includes a stale snapshot — better an
/// approximate hint than none — while enforcement elsewhere uses only the fresh view.
/// <para>
/// Each placeholder carries the deployed default, because a placeholder is the control's own slot
/// for what happens when the field is left empty. It degrades to the bare
/// <see cref="RUNTIME_DEFAULT"/> on a cold cache: a placeholder that sometimes carries a figure
/// still reads correctly, whereas a sentence that sometimes carries one reads broken.
/// </para>
/// </summary>
public sealed record AgentRunLimitHints(
    int DeadlineMaxMinutes,
    decimal BudgetMaxUsd,
    int TurnsMax,
    int RetainedMaxMib,
    string DeadlinePlaceholder,
    string BudgetPlaceholder,
    string TurnsPlaceholder,
    string RetainedPlaceholder)
{
    public const string RUNTIME_DEFAULT = "runtime default";

    private const decimal MILLISECONDS_PER_MINUTE = 60_000m;
    private const decimal BYTES_PER_MIB = 1024m * 1024m;

    public static AgentRunLimitHints None { get; } = new(
        int.MaxValue,
        decimal.MaxValue,
        int.MaxValue,
        int.MaxValue,
        RUNTIME_DEFAULT,
        RUNTIME_DEFAULT,
        RUNTIME_DEFAULT,
        RUNTIME_DEFAULT);

    public static AgentRunLimitHints From(IAgentRunLimitsProvider provider)
    {
        if (provider.LastKnown is not { Limits: var limits })
        {
            return None;
        }

        return new AgentRunLimitHints(
            DeadlineMaxMinutes: (int)Math.Floor(limits.DeadlineMs.Ceiling / MILLISECONDS_PER_MINUTE),
            BudgetMaxUsd: limits.MaxBudgetUsd.Ceiling,
            TurnsMax: (int)Math.Floor(limits.MaxTurns.Ceiling),
            RetainedMaxMib: (int)Math.Floor(limits.MaxRetainedOutputBytes.Ceiling / BYTES_PER_MIB),
            DeadlinePlaceholder: Placeholder(FormatLimit(limits.DeadlineMs.Default / MILLISECONDS_PER_MINUTE)),
            BudgetPlaceholder: Placeholder(FormatUsd(limits.MaxBudgetUsd.Default)),
            TurnsPlaceholder: Placeholder(FormatLimit(limits.MaxTurns.Default)),
            RetainedPlaceholder: Placeholder(
                FormatLimit(limits.MaxRetainedOutputBytes.Default / BYTES_PER_MIB)));
    }

    private static string Placeholder(string figure)
    {
        return $"{RUNTIME_DEFAULT} {figure}";
    }

    private static string FormatUsd(decimal value)
    {
        return value.ToString("0.00##", CultureInfo.InvariantCulture);
    }

    private static string FormatLimit(decimal value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
