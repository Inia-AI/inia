using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Domain.Entities.Signatures;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace IniaAi.WebApp.Services.Signatures;

/// <summary>
/// EF implementation of <see cref="ISignatureOverrideReader"/> with a per-circuit cache.
/// Uses <see cref="IServiceScopeFactory"/> to resolve a fresh <see cref="ICoreDbContext"/>
/// per query so concurrent block renders on the same Blazor circuit don't collide on
/// a shared scoped DbContext instance (EF's single-active-operation rule). Results are
/// cached per (organization, workflow type) — override rows are workflow-level and one
/// load serves every signature-aware component in the editor — and the cache is dropped
/// on the same events the components react to, so freshness semantics are unchanged.
/// </summary>
public sealed class SignatureOverrideReader : ISignatureOverrideReader, IDisposable
{
    private static readonly IReadOnlyDictionary<string, string> _noOverrides =
        ReadOnlyDictionary<string, string>.Empty;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AppState _appState;
    private readonly ConcurrentDictionary<(string OrganizationId, string WorkflowTypeId), Task<IReadOnlyDictionary<string, string>>> _cache = new();

    public SignatureOverrideReader(IServiceScopeFactory scopeFactory, AppState appState)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _appState = appState ?? throw new ArgumentNullException(nameof(appState));

        _appState.OrganizationChanged += OnCacheInvalidated;
        _appState.SignatureOverridesInvalidated += OnCacheInvalidated;
    }

    public Task<IReadOnlyDictionary<string, string>> LoadForWorkflowAsync(
        string organizationId,
        string? workflowTypeId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(organizationId);

        // Override rows are keyed by (Org, WorkflowTypeId, PlaceholderName); a workflow
        // that was never saved as a type cannot have rows keyed to it.
        if (string.IsNullOrEmpty(workflowTypeId))
        {
            return Task.FromResult(_noOverrides);
        }

        // WaitAsync lets the caller cancel its own await without the token reaching —
        // and poisoning — the cached task other callers share.
        return _cache.GetOrAdd((organizationId, workflowTypeId), QueryAsync).WaitAsync(cancellationToken);
    }

    public async Task<(int Active, int Pending)> CountOverridesForFieldAsync(
        string? workflowTypeId,
        string fieldName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(fieldName);

        if (string.IsNullOrEmpty(workflowTypeId))
        {
            return (0, 0);
        }

        using IServiceScope scope = _scopeFactory.CreateScope();
        ICoreDbContext coreDbContext = scope.ServiceProvider.GetRequiredService<ICoreDbContext>();

        int active = await coreDbContext.SignatureFieldOverrides
            .AsNoTracking()
            .CountAsync(
                o => o.WorkflowTypeId == workflowTypeId && o.PlaceholderName == fieldName,
                cancellationToken);

        int pending = await coreDbContext.PendingSignatureOverrides
            .AsNoTracking()
            .CountAsync(
                p => p.WorkflowTypeId == workflowTypeId && p.PlaceholderName == fieldName,
                cancellationToken);

        return (active, pending);
    }

    public void Dispose()
    {
        _appState.OrganizationChanged -= OnCacheInvalidated;
        _appState.SignatureOverridesInvalidated -= OnCacheInvalidated;
    }

    private void OnCacheInvalidated(object? sender, EventArgs e)
    {
        _cache.Clear();
    }

    // The cached task is shared by every caller, so it must not be tied to the first
    // caller's CancellationToken; a failed query is evicted so the next call retries.
    private async Task<IReadOnlyDictionary<string, string>> QueryAsync(
        (string OrganizationId, string WorkflowTypeId) key)
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ICoreDbContext coreDbContext = scope.ServiceProvider.GetRequiredService<ICoreDbContext>();

            List<SignatureFieldOverride> rows = await coreDbContext.SignatureFieldOverrides
                .AsNoTracking()
                .Where(o => o.OrganizationId == key.OrganizationId
                    && o.WorkflowTypeId == key.WorkflowTypeId)
                .ToListAsync();

            Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
            foreach (SignatureFieldOverride row in rows)
            {
                map[row.PlaceholderName] = row.Value;
            }

            return new ReadOnlyDictionary<string, string>(map);
        }
        catch
        {
            _cache.TryRemove(key, out _);
            throw;
        }
    }
}
