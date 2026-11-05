namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// Resolves user ids to human-friendly display names for the version-history surfaces, so the UI
/// shows a person's name instead of a raw user id.
/// </summary>
public interface IUserDisplayNameResolver
{
    /// <summary>
    /// Resolves the given user ids to display names. Ids with no matching user are omitted from the
    /// result, so callers should fall back to the raw id when a key is absent.
    /// </summary>
    /// <param name="userIds">The user ids to resolve. Duplicates and blanks are ignored.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A map of user id to display name for the ids that matched a user.</returns>
    Task<IReadOnlyDictionary<string, string>> ResolveAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken = default);
}
