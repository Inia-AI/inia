using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Core.Workflows.Agents;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

/// <summary>
/// One run's trace as the panel shows it: its own digest, its own files, and the round that
/// bought it. A digest is per run because every figure on it — model, tokens, cost, stop reason,
/// timeline — belongs to one run; parsing every file of every round into a single digest
/// reported a verification group's three legs as one impossible run.
/// </summary>
public sealed record AgentTracePanelRun(
    string RunExecutionId,
    int Attempt,
    AgentRunPass? Pass,
    AgentTraceDigest? Digest,
    List<AgentRunTraceFile> Files);

/// <summary>
/// The split of a block's collected trace files into the runs behind them, and the ledger total
/// that titles the panel. Lives beside <see cref="AgentTraceDigest"/> rather than inside the
/// component so a test pins the split the panel really makes — a test that regroups the files
/// itself passes just as well against a panel that never split them.
/// </summary>
public static class AgentTracePanelRuns
{
    /// <summary>
    /// Groups the files by the run that produced them, pairs each run with its ledger row, and
    /// orders by round then machinery pass. Retry passes collect their own pack and every pack
    /// names its files the same way, so the run is the only thing telling two
    /// <c>trace/trace.ndjson</c> rows apart. A run the ledger does not name sorts as round zero:
    /// the ledger is optional, and a block whose consumer holds none keeps the old attempt order.
    /// </summary>
    public static List<AgentTracePanelRun> Split(
        IEnumerable<AgentRunTraceFile> files,
        IReadOnlyList<AgentRunPass>? passes)
    {
        Dictionary<string, AgentRunPass> passByRun = (passes ?? [])
            .DistinctBy(static pass => pass.ExecutionId, StringComparer.Ordinal)
            .ToDictionary(static pass => pass.ExecutionId, static pass => pass, StringComparer.Ordinal);

        return [.. files
            .GroupBy(static file => file.RunExecutionId, StringComparer.Ordinal)
            .Select(run => new AgentTracePanelRun(
                run.Key,
                run.Max(static file => file.Attempt),
                passByRun.GetValueOrDefault(run.Key),
                AgentTraceDigest.Parse([.. run]),
                [.. run.OrderBy(static file => file.Path)]))
            .OrderBy(static run => run.Pass?.Round ?? 0)
            .ThenBy(static run => run.Attempt)];
    }

    /// <summary>
    /// What the reader was billed for across the runs, from the run ledger — what the runtime
    /// charged — rather than from the parsed traces. Empty when no run carries a cost, so the
    /// panel title says nothing instead of claiming a zero total.
    /// </summary>
    public static string LedgerSpend(IReadOnlyList<AgentTracePanelRun> runs)
    {
        decimal total = runs.Sum(static run => run.Pass?.EstimatedCostUsd ?? 0m);

        return total > 0m ? $" · ${total:0.####} total" : string.Empty;
    }

    /// <summary>
    /// The round leads when the ledger supplied one: a reader looking at a redone member's block
    /// needs to know which round's attempt they are reading before anything else. A redo leg
    /// then names the mode it actually ran — resumed against scratch is the difference between
    /// continuing the member's own conversation and starting over, and the ledger's actual mode
    /// already carries any in-flight degrade — and a leg that settled failed says so with the
    /// runtime's subtype, because a killed run's pack has no result trace to say it otherwise.
    /// </summary>
    public static string Header(AgentTracePanelRun run)
    {
        var parts = new List<string>();
        if (run.Pass is { } pass)
        {
            parts.Add($"round {pass.Round}");
        }

        if (run.Attempt > 0)
        {
            parts.Add($"pass {run.Attempt}");
        }

        if (run.Pass?.ActualRedoMode is { } mode)
        {
            parts.Add(mode == VerificationRedoLeg.REVISE_MODE ? "resumed" : mode.ToLowerInvariant());
        }

        if (run.Pass is { Status: nameof(AgentRunStatus.Failed) } failedPass)
        {
            parts.Add(failedPass.FailureSubtype is { } subtype ? $"failed ({subtype})" : "failed");
        }

        parts.Add(run.RunExecutionId);

        return string.Join(" · ", parts);
    }
}
