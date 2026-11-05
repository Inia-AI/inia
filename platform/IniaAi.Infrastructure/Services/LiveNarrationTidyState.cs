using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// The per-run ladder state behind the live feed's narration tidying. Each line climbs
/// exactly one way: unsettled (renders raw, pending) → settled tidied/dropped when a tidy
/// pass answers for it, or settled raw after the attempt budget is spent. A settled line's
/// wording never changes again and is never sent to the tidier again; an unsettled line
/// renders the agent's raw words in the meantime, so a transient failure leaves no hole and
/// a later success upgrades the line before the reader has lived with it for long.
/// </summary>
public sealed class LiveNarrationTidyState
{
    public const int MAX_TIDY_ATTEMPTS = 3;

    private readonly Dictionary<string, string?> _settled = [];
    private readonly Dictionary<string, int> _attempts = [];

    /// <summary>
    /// The settled lines only — the map handed to the projector, so unsettled lines fall
    /// through to their raw words.
    /// </summary>
    public IReadOnlyDictionary<string, string?> SettledNarration => _settled;

    public async Task ApplyAsync(
        IReadOnlyList<AgentNarrationLine> lines,
        IAgentNarrationTidier? tidier,
        CancellationToken cancellationToken)
    {
        List<AgentNarrationLine> unsettled = [.. lines.Where(line => !_settled.ContainsKey(line.Key))];
        if (unsettled.Count == 0)
        {
            return;
        }

        if (tidier is null)
        {
            foreach (AgentNarrationLine line in unsettled)
            {
                _settled[line.Key] = line.Text;
            }

            return;
        }

        IReadOnlyDictionary<string, string?> tidied = await tidier.TidyAsync(unsettled, cancellationToken);
        foreach (AgentNarrationLine line in unsettled)
        {
            if (tidied.TryGetValue(line.Key, out string? value))
            {
                _settled[line.Key] = value;
                continue;
            }

            int attempts = _attempts.GetValueOrDefault(line.Key) + 1;
            _attempts[line.Key] = attempts;
            if (attempts >= MAX_TIDY_ATTEMPTS)
            {
                _settled[line.Key] = line.Text;
            }
        }
    }
}
