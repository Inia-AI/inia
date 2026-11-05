namespace IniaAi.Core.Workflows.StoredKeys;

public interface IStoredKeyResolver
{
    /// <summary>The value of the organization's stored key, or null when it has none by that name.</summary>
    Task<string?> ResolveAsync(string organizationId, string name, CancellationToken cancellationToken);
}
