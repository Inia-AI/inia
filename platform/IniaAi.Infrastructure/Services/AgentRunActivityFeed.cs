using System.Collections.Concurrent;
using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// The process-local implementation of <see cref="IAgentRunActivityFeed"/>. Entries are
/// pruned lazily on every write and read: a settled entry survives one grace window so the
/// next poll still sees the final beats, and any entry — settled or not — expires once the run
/// behind it has written nothing for the age bound, so an abandoned registration (a host that
/// died mid-run, an editor test that never polls) cannot accumulate. Only a write touches an
/// entry (<c>BeginRun</c>, <c>PublishDigest</c>, <c>ConcludeRun</c>); a desk reading the feed
/// does not, deliberately — the bound is meant to measure whether the run is still alive, and a
/// reader watching a dead run would otherwise keep its entry forever. The bound counts from the
/// last write rather than from registration because the executor publishes a digest on a timed
/// cadence for as long as it polls: a live long-deadline run keeps its entry alive, while an
/// abandoned one, which publishes nothing, still ages out on schedule.
/// </summary>
public sealed class AgentRunActivityFeed : IAgentRunActivityFeed
{
    private const int SETTLED_RETENTION_MINUTES = 10;
    private const int MAX_ENTRY_AGE_MINUTES = 60;

    private sealed record FeedEntry(
        AgentRunActivityRegistration Registration,
        AgentActivityDigest? Digest,
        bool Settled,
        DateTimeOffset RegisteredAt,
        DateTimeOffset LastTouchedAt,
        DateTimeOffset? SettledAt);

    private readonly ConcurrentDictionary<string, FeedEntry> _entries = new();
    private readonly TimeProvider _timeProvider;

    public event EventHandler<AgentRunActivityEntry>? EntryChanged;

    public AgentRunActivityFeed(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void BeginRun(AgentRunActivityRegistration registration)
    {
        Prune();
        DateTimeOffset now = _timeProvider.GetUtcNow();
        var entry = new FeedEntry(registration, null, Settled: false, now, now, null);
        _entries[registration.RunExecutionId] = entry;
        Announce(entry);
    }

    public void PublishDigest(string runExecutionId, AgentActivityDigest digest)
    {
        Prune();
        _ = _entries.TryGetValue(runExecutionId, out FeedEntry? entry);
        if (entry is null)
        {
            return;
        }

        FeedEntry published = entry with { Digest = digest, LastTouchedAt = _timeProvider.GetUtcNow() };
        _entries[runExecutionId] = published;
        Announce(published);
    }

    public void ConcludeRun(string runExecutionId)
    {
        Prune();
        _ = _entries.TryGetValue(runExecutionId, out FeedEntry? entry);
        if (entry is null)
        {
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        FeedEntry settled = entry with { Settled = true, SettledAt = now, LastTouchedAt = now };
        _entries[runExecutionId] = settled;
        Announce(settled);
    }

    public IReadOnlyList<AgentRunActivityEntry> SnapshotForExecution(
        string organizationId,
        string workflowExecutionId)
    {
        Prune();
        return [.. _entries.Values
            .Where(entry => entry.Registration.OrganizationId == organizationId
                && entry.Registration.ConversationId is null
                && entry.Registration.WorkflowExecutionId == workflowExecutionId)
            .OrderBy(static entry => entry.RegisteredAt)
            .Select(static entry => new AgentRunActivityEntry(entry.Registration, entry.Digest, entry.Settled))];
    }

    public IReadOnlyList<AgentRunActivityEntry> SnapshotForConversation(string organizationId, string conversationId)
    {
        Prune();
        return [.. _entries.Values
            .Where(entry => entry.Registration.OrganizationId == organizationId
                && entry.Registration.ConversationId == conversationId)
            .OrderBy(static entry => entry.RegisteredAt)
            .Select(static entry => new AgentRunActivityEntry(entry.Registration, entry.Digest, entry.Settled))];
    }

    /// <summary>
    /// A subscriber's fault must never reach the executor that wrote the entry: activity is
    /// garnish, and the run it describes goes on regardless.
    /// </summary>
    private void Announce(FeedEntry entry)
    {
        try
        {
            EntryChanged?.Invoke(this, new AgentRunActivityEntry(entry.Registration, entry.Digest, entry.Settled));
        }
        catch (Exception)
        {
            // Deliberately swallowed; see the summary.
        }
    }

    private void Prune()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        foreach ((string key, FeedEntry entry) in _entries)
        {
            bool settledExpired = entry.SettledAt is DateTimeOffset settledAt
                && (now - settledAt).TotalMinutes >= SETTLED_RETENTION_MINUTES;
            bool aged = (now - entry.LastTouchedAt).TotalMinutes >= MAX_ENTRY_AGE_MINUTES;
            if (settledExpired || aged)
            {
                _ = _entries.TryRemove(key, out _);
            }
        }
    }
}
