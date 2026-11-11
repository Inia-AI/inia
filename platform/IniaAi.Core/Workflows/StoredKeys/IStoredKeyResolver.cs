namespace IniaAi.Core.Workflows.StoredKeys;

public interface IStoredKeyResolver
{
    /// <summary>
    /// The value of the organization's stored key. When the organization has none by that name, the
    /// deployment's own value for it, or null when the deployment sets none.
    /// </summary>
    Task<string?> ResolveAsync(string organizationId, string name, CancellationToken cancellationToken);
}
